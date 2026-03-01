using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BotMain
{
    public class ProfileLoader
    {
        private readonly string _profileDir;
        private readonly List<MetadataReference> _refs = new();
        private readonly List<string> _errors = new();

        // 为缺失的命名空间提供空定义，避免 using 编译失败
        private static readonly SyntaxTree StubTree = CSharpSyntaxTree.ParseText(@"
namespace SmartBotAPI.Plugins.API {}
namespace SmartBotAPI.Battlegrounds {}
namespace SmartBotAPI.Plugins.API.HSReplayArchetypes {}
namespace SmartBot.Plugins.API.Actions {}
");

        public ProfileLoader(string profileDir, params string[] referenceDlls)
        {
            _profileDir = profileDir;

            _refs.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
            {
                var name = Path.GetFileName(dll);
                if (name.StartsWith("System.", StringComparison.Ordinal) ||
                    name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                    name == "mscorlib.dll" || name == "netstandard.dll")
                {
                    if (IsManagedAssembly(dll))
                        _refs.Add(MetadataReference.CreateFromFile(dll));
                }
            }

            foreach (var dll in referenceDlls)
            {
                if (File.Exists(dll))
                    _refs.Add(MetadataReference.CreateFromFile(dll));
            }
        }

        /// <summary>
        /// 两轮编译：第一轮逐文件编译，第二轮用成功的程序集作为引用重试失败的
        /// </summary>
        public List<Assembly> CompileAll()
        {
            var files = Directory.GetFiles(_profileDir, "*.cs", SearchOption.AllDirectories);
            var assemblies = new List<Assembly>();
            var compiledBytes = new List<byte[]>();
            _errors.Clear();

            var failed = new List<string>();
            foreach (var file in files)
            {
                var (asm, bytes) = CompileSingle(file, _refs);
                if (asm != null)
                {
                    assemblies.Add(asm);
                    compiledBytes.Add(bytes);
                }
                else
                {
                    failed.Add(file);
                }
            }

            // 第二轮：用第一轮的成功产物作为额外引用
            if (failed.Count > 0 && compiledBytes.Count > 0)
            {
                var extraRefs = new List<MetadataReference>(_refs);
                foreach (var b in compiledBytes)
                    extraRefs.Add(MetadataReference.CreateFromImage(b));

                foreach (var file in failed)
                {
                    _errors.RemoveAll(e => e.StartsWith(Path.GetFileName(file) + ":"));
                    var (asm, _) = CompileSingle(file, extraRefs);
                    if (asm != null) assemblies.Add(asm);
                }
            }

            if (assemblies.Count == 0 && _errors.Count > 0)
                throw new Exception("编译失败:\n" + string.Join("\n", _errors));

            return assemblies;
        }

        private (Assembly asm, byte[] bytes) CompileSingle(string filePath, List<MetadataReference> refs)
        {
            var code = File.ReadAllText(filePath);
            var tree = CSharpSyntaxTree.ParseText(code, path: filePath);

            var compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(filePath),
                new[] { tree, StubTree },
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var ms = new MemoryStream();
            var result = compilation.Emit(ms);

            if (!result.Success)
            {
                var errs = result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString());
                _errors.Add($"{Path.GetFileName(filePath)}:\n  " + string.Join("\n  ", errs));
                return (null, null);
            }

            var bytes = ms.ToArray();
            return (Assembly.Load(bytes), bytes);
        }

        public List<T> LoadInstances<T>(List<Assembly> assemblies) where T : class
        {
            if (assemblies == null) return new List<T>();
            return assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => typeof(T).IsAssignableFrom(t) && !t.IsAbstract)
                .Select(t => { try { return Activator.CreateInstance(t) as T; } catch { return null; } })
                .Where(x => x != null)
                .ToList();
        }

        public List<string> Errors => _errors;

        private static bool IsManagedAssembly(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                using var pe = new PEReader(fs);
                return pe.HasMetadata;
            }
            catch { return false; }
        }
    }
}

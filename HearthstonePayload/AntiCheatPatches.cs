using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace HearthstonePayload
{
    /// <summary>
    /// 动态枚举并全量拦截 AntiCheatManager 及 Telemetry 相关类的全部方法。
    /// 支持类名变更时的模糊匹配和方法快照比对。
    /// </summary>
    public static class AntiCheatPatches
    {
        // System.Object 基础方法 + 构造函数，不应 patch
        private static readonly HashSet<string> SkipMethods = new HashSet<string>
        {
            "ToString", "GetHashCode", "Equals", "GetType", "Finalize",
            "MemberwiseClone", "ReferenceEquals", "obj_address"
        };

        // AntiCheatManager 特征方法，用于类名变更时的模糊匹配
        private static readonly string[] AcSignatureMethods =
        {
            "ReportCheat", "InitSDK", "TryCallSDK", "ReportActivity"
        };

        // Telemetry 类名关键字
        private static readonly string[] TelemetryKeywords =
        {
            "Telemetry", "Analytics", "Reporting"
        };

        private static readonly List<string> _patchLog = new List<string>();

        public static void Apply(Harmony harmony)
        {
            _patchLog.Clear();
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asm == null)
                {
                    Log("Assembly-CSharp not found");
                    return;
                }

                // --- 1. AntiCheatManager 拦截 ---
                var acmType = asm.GetType("AntiCheatManager");
                if (acmType != null)
                {
                    PatchAllMethods(harmony, acmType, "AntiCheatManager");
                }
                else
                {
                    // 类名可能被改，模糊搜索
                    Log("[AC_CHANGE] AntiCheatManager class not found, scanning by signature methods...");
                    var candidates = FindTypesBySignature(asm, AcSignatureMethods, 2);
                    foreach (var candidate in candidates)
                    {
                        Log("[AC_CHANGE] Fuzzy match: " + candidate.FullName);
                        PatchAllMethods(harmony, candidate, candidate.Name);
                    }
                    if (candidates.Count == 0)
                        Log("[AC_CHANGE] WARNING: No AntiCheatManager candidate found!");
                }

                // --- 2. Telemetry 类拦截 ---
                PatchTypesByKeyword(harmony, asm, TelemetryKeywords);

                // --- 3. 快照比对 ---
                CompareAndSaveSnapshot(asm);
            }
            catch (Exception ex)
            {
                Log("AntiCheatPatches.Apply failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 获取本次 Apply 的全部日志行（供 Entry.cs 写入 startup log）
        /// </summary>
        public static IReadOnlyList<string> GetPatchLog() => _patchLog;

        private static void PatchAllMethods(Harmony harmony, Type type, string label)
        {
            var methods = type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly);

            var count = 0;
            foreach (var mi in methods)
            {
                if (SkipMethods.Contains(mi.Name))
                    continue;
                if (mi.IsSpecialName) // 跳过属性 getter/setter、事件 add/remove
                    continue;

                try
                {
                    var prefix = ChoosePrefix(mi);
                    harmony.Patch(mi, prefix: prefix);
                    count++;
                }
                catch (Exception ex)
                {
                    Log(string.Format("  Failed to patch {0}.{1}: {2}", label, mi.Name, ex.Message));
                }
            }
            Log(string.Format("Patched {0}: {1} methods intercepted", label, count));
        }

        private static HarmonyMethod ChoosePrefix(MethodInfo mi)
        {
            if (mi.ReturnType == typeof(bool))
                return new HarmonyMethod(typeof(AntiCheatPatches), nameof(ReturnFalse));
            // void 和其他返回值类型：跳过原方法，__result 保持默认值
            return new HarmonyMethod(typeof(AntiCheatPatches), nameof(SkipOriginal));
        }

        private static void PatchTypesByKeyword(Harmony harmony, Assembly asm, string[] keywords)
        {
            Type[] allTypes;
            try { allTypes = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { allTypes = ex.Types.Where(t => t != null).ToArray(); }

            foreach (var type in allTypes)
            {
                if (type.Name == null) continue;
                var match = false;
                foreach (var kw in keywords)
                {
                    if (type.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        match = true;
                        break;
                    }
                }
                if (match)
                {
                    PatchAllMethods(harmony, type, type.Name);
                }
            }
        }

        private static List<Type> FindTypesBySignature(Assembly asm, string[] signatureMethods, int minMatchCount)
        {
            var result = new List<Type>();
            Type[] allTypes;
            try { allTypes = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { allTypes = ex.Types.Where(t => t != null).ToArray(); }

            foreach (var type in allTypes)
            {
                var methods = type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly);

                var methodNames = new HashSet<string>(methods.Select(m => m.Name));
                var matchCount = signatureMethods.Count(s => methodNames.Contains(s));
                if (matchCount >= minMatchCount)
                    result.Add(type);
            }
            return result;
        }

        // --- 快照比对 ---

        private static void CompareAndSaveSnapshot(Assembly asm)
        {
            try
            {
                var snapshotPath = GetSnapshotPath();
                var current = BuildSnapshot(asm);

                if (File.Exists(snapshotPath))
                {
                    var previous = File.ReadAllText(snapshotPath, System.Text.Encoding.UTF8);
                    var previousLines = new HashSet<string>(
                        previous.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
                    var currentLines = new HashSet<string>(
                        current.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

                    // 新增的方法
                    foreach (var line in currentLines)
                    {
                        if (!previousLines.Contains(line))
                            Log("[AC_CHANGE] NEW: " + line);
                    }

                    // 消失的方法
                    foreach (var line in previousLines)
                    {
                        if (!currentLines.Contains(line))
                            Log("[AC_CHANGE] REMOVED: " + line);
                    }
                }
                else
                {
                    Log("First run: creating AC method snapshot.");
                }

                File.WriteAllText(snapshotPath, current, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log("Snapshot compare/save failed: " + ex.Message);
            }
        }

        private static string BuildSnapshot(Assembly asm)
        {
            var sb = new System.Text.StringBuilder();
            Type[] allTypes;
            try { allTypes = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { allTypes = ex.Types.Where(t => t != null).ToArray(); }

            foreach (var type in allTypes)
            {
                if (type.Name == null) continue;
                var isAc = type.Name == "AntiCheatManager";
                var isTelemetry = false;
                if (!isAc)
                {
                    foreach (var kw in TelemetryKeywords)
                    {
                        if (type.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isTelemetry = true;
                            break;
                        }
                    }
                }
                if (!isAc && !isTelemetry) continue;

                var methods = type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly);
                foreach (var mi in methods)
                {
                    if (SkipMethods.Contains(mi.Name) || mi.IsSpecialName) continue;
                    sb.AppendLine(type.Name + "::" + mi.Name);
                }
            }
            return sb.ToString();
        }

        private static string GetSnapshotPath()
        {
            var dir = Path.GetDirectoryName(typeof(AntiCheatPatches).Assembly.Location);
            if (string.IsNullOrWhiteSpace(dir))
                dir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(dir, "ac_method_snapshot.txt");
        }

        private static void Log(string message)
        {
            _patchLog.Add(message);
        }

        // --- Harmony prefix 方法 ---

        private static bool ReturnFalse() => false;

        private static bool SkipOriginal() => false;
    }
}

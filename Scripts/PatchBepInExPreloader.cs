using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private const string PlatformUtilsTypeName = "BepInEx.Preloader.PlatformUtils";
    private const string SetPlatformMethodName = "SetPlatform";
    private const string ShimMethodName = "SafeGetPEKind";

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: PatchBepInExPreloader <BepInEx.Preloader.dll path>");
            return 1;
        }

        var dllPath = args[0];
        if (!File.Exists(dllPath))
        {
            Console.WriteLine("Target file not found: " + dllPath);
            return 1;
        }

        var backupPath = dllPath + ".bak_getpekind";

        try
        {
            if (!File.Exists(backupPath))
            {
                File.Copy(dllPath, backupPath);
                Console.WriteLine("Backup created: " + backupPath);
            }
            else
            {
                Console.WriteLine("Backup exists: " + backupPath);
            }

            byte[] inputBytes = File.ReadAllBytes(dllPath);
            var inputStream = new MemoryStream(inputBytes);
            var assembly = AssemblyDefinition.ReadAssembly(inputStream, new ReaderParameters { ReadSymbols = false });
            var module = assembly.MainModule;

            var platformUtils = module.Types.FirstOrDefault(t => t.FullName == PlatformUtilsTypeName);
            if (platformUtils == null)
            {
                Console.WriteLine("Type not found: " + PlatformUtilsTypeName);
                return 2;
            }

            var setPlatform = platformUtils.Methods.FirstOrDefault(m => m.Name == SetPlatformMethodName);
            if (setPlatform == null)
            {
                Console.WriteLine("Method not found: " + SetPlatformMethodName);
                return 3;
            }

            var shim = platformUtils.Methods.FirstOrDefault(m => m.Name == ShimMethodName)
                ?? CreateShimMethod(module, platformUtils);
            var shimRef = module.ImportReference(shim);

            var patchedCalls = 0;
            foreach (var instruction in setPlatform.Body.Instructions)
            {
                var method = instruction.Operand as MethodReference;
                if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                    method != null &&
                    method.Name == "GetPEKind" &&
                    method.DeclaringType.FullName == "System.Reflection.Module")
                {
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = shimRef;
                    patchedCalls++;
                }
            }

            if (patchedCalls == 0 && !IsAlreadyPatched(setPlatform))
            {
                Console.WriteLine("No target call was patched. File may use another implementation.");
                return 4;
            }

            var outStream = new MemoryStream();
            assembly.Write(outStream);
            File.WriteAllBytes(dllPath, outStream.ToArray());
            Console.WriteLine("Patched successfully. Replaced call count: " + patchedCalls);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Patch failed: " + ex);
            return 10;
        }
    }

    private static MethodDefinition CreateShimMethod(ModuleDefinition module, TypeDefinition owner)
    {
        var moduleType = module.ImportReference(typeof(System.Reflection.Module));
        var peKindType = module.ImportReference(typeof(System.Reflection.PortableExecutableKinds));
        var machineType = module.ImportReference(typeof(System.Reflection.ImageFileMachine));

        var method = new MethodDefinition(
            ShimMethodName,
            Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.HideBySig,
            module.TypeSystem.Void);

        method.Parameters.Add(new ParameterDefinition("module", Mono.Cecil.ParameterAttributes.None, moduleType));
        method.Parameters.Add(new ParameterDefinition("peKind", Mono.Cecil.ParameterAttributes.Out, new ByReferenceType(peKindType)));
        method.Parameters.Add(new ParameterDefinition("machine", Mono.Cecil.ParameterAttributes.Out, new ByReferenceType(machineType)));

        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stind_I4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stind_I4);
        il.Emit(OpCodes.Ret);

        owner.Methods.Add(method);
        return method;
    }

    private static bool IsAlreadyPatched(MethodDefinition setPlatform)
    {
        foreach (var instruction in setPlatform.Body.Instructions)
        {
            var method = instruction.Operand as MethodReference;
            if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                method != null &&
                method.Name == ShimMethodName &&
                method.DeclaringType.FullName == PlatformUtilsTypeName)
            {
                return true;
            }
        }
        return false;
    }
}

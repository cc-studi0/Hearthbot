using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace HearthstonePayload
{
    /// <summary>
    /// 通过反射查找 AntiCheatManager 并禁用反作弊回调
    /// 参考 HsMod 的 PatchAntiCheatManager 实现
    /// </summary>
    public static class AntiCheatPatches
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asm == null) return;

                var acmType = asm.GetType("AntiCheatManager");
                if (acmType == null) return;

                var prefix = new HarmonyMethod(typeof(AntiCheatPatches), nameof(ReturnFalse));

                // patch 所有可能触发反作弊上报的方法
                string[] targets = {
                    "OnLoginComplete", "TryCallSDK",
                    "ReportActivity", "ReportCheat",
                    "Initialize", "InitSDK"
                };

                foreach (var name in targets)
                {
                    var mi = acmType.GetMethod(name,
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static);
                    if (mi != null)
                    {
                        harmony.Patch(mi, prefix: prefix);
                    }
                }
            }
            catch { }
        }

        private static bool ReturnFalse() => false;
    }
}

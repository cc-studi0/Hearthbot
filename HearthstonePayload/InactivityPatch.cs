using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace HearthstonePayload
{
    /// <summary>
    /// 通过 Harmony patch 禁用 InactivePlayerKicker 的 AFK 检测。
    ///
    /// 炉石的 InactivePlayerKicker 每帧通过 UnityEngine.Input.anyKey 检测玩家活动，
    /// 30 分钟（m_kickSec = 1800f）无输入后断开连接并弹出"重新连接"弹窗。
    /// 
    /// Bot 通过反射/注入操作游戏对象，不产生原生系统输入事件，
    /// 因此 Input.anyKey 始终为 false，导致即使 bot 一直在操作也会被踢。
    ///
    /// 本 patch 直接将 m_activityDetected 字段在 CheckActivity 方法执行前设为 true，
    /// 使 InactivePlayerKicker 认为始终有玩家活动，彻底解决 AFK 断线问题。
    /// </summary>
    public static class InactivityPatch
    {
        private static FieldInfo _activityDetectedField;

        public static void Apply(Harmony harmony)
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asm == null) return;

                var kickerType = asm.GetType("InactivePlayerKicker");
                if (kickerType == null) return;

                // 缓存 m_activityDetected 字段引用
                _activityDetectedField = kickerType.GetField("m_activityDetected",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                // Patch CheckActivity 方法：在原方法执行前强制设置 m_activityDetected = true
                var checkActivity = kickerType.GetMethod("CheckActivity",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (checkActivity != null && _activityDetectedField != null)
                {
                    harmony.Patch(checkActivity,
                        prefix: new HarmonyMethod(typeof(InactivityPatch), nameof(ForceActivity)));
                }
            }
            catch { }
        }

        /// <summary>
        /// Harmony prefix: 在 CheckActivity 执行前，强制将 m_activityDetected 设为 true。
        /// 参数 __instance 由 Harmony 自动注入，指向当前 InactivePlayerKicker 实例。
        /// </summary>
        private static void ForceActivity(object __instance)
        {
            try
            {
                _activityDetectedField?.SetValue(__instance, true);
            }
            catch { }
        }
    }
}

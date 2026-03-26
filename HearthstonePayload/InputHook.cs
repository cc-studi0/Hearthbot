using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HearthstonePayload
{
    /// <summary>
    /// Harmony hook Unity Input，模拟鼠标输入
    /// 不控制真实鼠标，窗口化/后台均可工作
    /// </summary>
    public static class InputHook
    {
        public static bool Simulating;
        public static float SimX, SimY; // Unity屏幕坐标（Y=0在底部）
        public static bool LeftHeld;
        public static int PressFrame = -1, ReleaseFrame = -1;
        public static int FrameCount;

        public static void NewFrame() { FrameCount++; }

        public static void ResetSimulationState()
        {
            Simulating = false;
            LeftHeld = false;
            PressFrame = -1;
            ReleaseFrame = -1;
            SimX = -9999;
            SimY = -9999;
        }

        public static void Apply(Harmony harmony)
        {
            var inputType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UnityEngine.Input"))
                .FirstOrDefault(t => t != null);
            if (inputType == null) return;

            var applicationType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UnityEngine.Application"))
                .FirstOrDefault(t => t != null);

            TryPatch(harmony, inputType, "mousePosition", nameof(MousePosPre));
            TryPatch(harmony, inputType, "GetMouseButton", nameof(GetButtonPre));
            TryPatch(harmony, inputType, "GetMouseButtonDown", nameof(GetButtonDownPre));
            TryPatch(harmony, inputType, "GetMouseButtonUp", nameof(GetButtonUpPre));
            TryPatch(harmony, applicationType, "isFocused", nameof(IsFocusedPre));

            // 拦截 HearthstoneApplication.OnApplicationFocus，防止窗口失焦时
            // PegUI.m_hasFocus 被置为 false 从而屏蔽所有鼠标输入
            TryPatchFocusCallback(harmony);
        }

        private static void TryPatchFocusCallback(Harmony harmony)
        {
            try
            {
                var hsAppType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("Hearthstone.HearthstoneApplication"))
                    .FirstOrDefault(t => t != null);
                if (hsAppType == null) return;

                var onFocus = hsAppType.GetMethod("OnApplicationFocus",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { typeof(bool) }, null);
                if (onFocus != null)
                {
                    harmony.Patch(onFocus,
                        prefix: new HarmonyMethod(typeof(InputHook), nameof(OnApplicationFocusPre)));
                }
            }
            catch { }
        }

        private static void TryPatch(Harmony harmony, Type inputType, string name, string prefixName)
        {
            try
            {
                MethodInfo target;
                if (name == "mousePosition")
                {
                    target = inputType.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
                }
                else
                {
                    target = inputType.GetMethod(name, new[] { typeof(int) });
                }
                if (target == null) return;
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(InputHook), prefixName));
            }
            catch { }
        }

        // Prefix返回false=跳过原方法，适用于InternalCall
        static bool MousePosPre(ref Vector3 __result)
        {
            if (!Simulating) return true;
            __result = new Vector3(SimX, SimY, 0f);
            return false;
        }

        static bool GetButtonPre(int button, ref bool __result)
        {
            if (!Simulating || button != 0) return true;
            __result = LeftHeld;
            return false;
        }

        static bool GetButtonDownPre(int button, ref bool __result)
        {
            if (!Simulating || button != 0) return true;
            __result = (PressFrame == FrameCount);
            return false;
        }

        static bool GetButtonUpPre(int button, ref bool __result)
        {
            if (!Simulating || button != 0) return true;
            __result = (ReleaseFrame == FrameCount);
            return false;
        }

        static bool IsFocusedPre(ref bool __result)
        {
            if (!Simulating) return true;
            __result = true;
            return false;
        }

        /// <summary>
        /// 模拟输入时拦截失焦，避免在后台保留按住状态；
        /// 非模拟期间让游戏自行处理焦点变更。
        /// </summary>
        static bool OnApplicationFocusPre(bool focus)
        {
            if (!focus)
            {
                ResetSimulationState();
            }

            return !Simulating;
        }
    }
}

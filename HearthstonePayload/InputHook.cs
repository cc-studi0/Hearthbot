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

        // PegUI.m_hasFocus 反射缓存（独立于 Application.isFocused）
        private static Type _pegUIType;
        private static MethodInfo _pegUIGet;
        private static FieldInfo _pegUIHasFocus;
        private static FieldInfo _pegUIUguiActive;
        private static bool _pegUIReflectionReady;

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

            // 拦截 PegUI.OnAppFocusChanged — 这是 PegUI 独立维护的焦点状态，
            // 失焦时会导致 MouseInputUpdate 提前返回，所有点击失效
            TryPatchPegUIFocusCallback(harmony);
        }

        private static void TryPatchPegUIFocusCallback(Harmony harmony)
        {
            try
            {
                EnsurePegUIReflection();
                if (_pegUIType == null) return;

                var onFocus = _pegUIType.GetMethod("OnAppFocusChanged",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { typeof(bool), typeof(object) }, null);
                if (onFocus != null)
                {
                    harmony.Patch(onFocus,
                        prefix: new HarmonyMethod(typeof(InputHook), nameof(OnPegUIFocusChangedPre)));
                }
            }
            catch { }
        }

        private static void EnsurePegUIReflection()
        {
            if (_pegUIReflectionReady) return;
            try
            {
                _pegUIType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("PegUI"))
                    .FirstOrDefault(t => t != null);
                if (_pegUIType != null)
                {
                    _pegUIGet = _pegUIType.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
                    _pegUIHasFocus = _pegUIType.GetField("m_hasFocus", BindingFlags.NonPublic | BindingFlags.Instance);
                    _pegUIUguiActive = _pegUIType.GetField("m_uguiActive", BindingFlags.NonPublic | BindingFlags.Instance);
                }
            }
            catch { }
            _pegUIReflectionReady = true;
        }

        /// <summary>
        /// 强制 PegUI 认为有焦点。必须在每次鼠标点击模拟前调用，
        /// 因为窗口可能在脚本运行前就已经失焦，m_hasFocus=false 会使
        /// MouseInputUpdate 完全跳过鼠标事件处理。
        /// </summary>
        public static void ForcePegUIFocus()
        {
            try
            {
                EnsurePegUIReflection();
                if (_pegUIGet == null || _pegUIHasFocus == null) return;
                var instance = _pegUIGet.Invoke(null, null);
                if (instance == null) return;
                _pegUIHasFocus.SetValue(instance, true);
                // m_uguiActive 为 true 时 MouseInputUpdate 也会跳过，顺便清掉
                _pegUIUguiActive?.SetValue(instance, false);
            }
            catch { }
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

        /// <summary>
        /// 模拟鼠标输入时阻止 PegUI 的焦点状态被置为 false；
        /// 否则 PegUI.MouseInputUpdate 会因 m_hasFocus=false 而跳过所有点击。
        /// </summary>
        static bool OnPegUIFocusChangedPre(bool focus)
        {
            // 模拟期间一律忽略失焦事件；非模拟期间放行
            return !Simulating;
        }
    }
}

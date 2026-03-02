using System;
using System.Linq;
using System.Reflection;

namespace HearthstonePayload
{
    /// <summary>
    /// 通过Harmony Input hook模拟鼠标输入
    /// 所有方法都是即时设置状态，不阻塞主线程
    /// 平滑移动由协程多帧驱动
    /// </summary>
    public static class MouseSimulator
    {
        private static int _curX, _curY;

        public static int CurX => _curX;
        public static int CurY => _curY;

        public static void MoveTo(int x, int y)
        {
            _curX = x;
            _curY = y;
            int sh = GetScreenHeight();
            if (sh > 0)
            {
                InputHook.SimX = x;
                InputHook.SimY = sh - y;
            }
        }

        public static void LeftDown()
        {
            InputHook.LeftHeld = true;
            InputHook.PressFrame = InputHook.FrameCount;
        }

        public static void LeftUp()
        {
            InputHook.LeftHeld = false;
            InputHook.ReleaseFrame = InputHook.FrameCount;
        }

        #region Unity坐标转换

        private static Assembly _asm;
        private static MethodInfo _worldToScreenPoint;
        private static object _mainCamera;
        private static PropertyInfo _screenWidth;
        private static PropertyInfo _screenHeight;

        public static bool WorldToScreen(float worldX, float worldY, float worldZ, out int screenX, out int screenY)
        {
            screenX = screenY = 0;
            try
            {
                EnsureReflection();
                if (_mainCamera == null) RefreshCamera();
                if (_mainCamera == null || _worldToScreenPoint == null) return false;

                var vector3Type = _asm.GetType("UnityEngine.Vector3")
                    ?? Type.GetType("UnityEngine.Vector3, UnityEngine.CoreModule")
                    ?? Type.GetType("UnityEngine.Vector3, UnityEngine");
                if (vector3Type == null) return false;

                var worldPos = Activator.CreateInstance(vector3Type, worldX, worldY, worldZ);
                var screenPos = _worldToScreenPoint.Invoke(_mainCamera, new[] { worldPos });
                if (screenPos == null) return false;

                float sx = (float)vector3Type.GetField("x").GetValue(screenPos);
                float sy = (float)vector3Type.GetField("y").GetValue(screenPos);

                int sh = GetScreenHeight();
                if (sh <= 0) return false;

                screenX = (int)sx;
                screenY = sh - (int)sy;
                return true;
            }
            catch { return false; }
        }

        public static int GetScreenWidth()
        {
            try { EnsureReflection(); return _screenWidth != null ? (int)_screenWidth.GetValue(null) : 0; }
            catch { return 0; }
        }

        public static int GetScreenHeight()
        {
            try { EnsureReflection(); return _screenHeight != null ? (int)_screenHeight.GetValue(null) : 0; }
            catch { return 0; }
        }

        private static void EnsureReflection()
        {
            if (_asm != null) return;
            _asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "UnityEngine.CoreModule")
                ?? AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "UnityEngine");
            if (_asm == null) return;

            var cameraType = _asm.GetType("UnityEngine.Camera");
            if (cameraType != null)
            {
                var mainProp = cameraType.GetProperty("main", BindingFlags.Public | BindingFlags.Static);
                _mainCamera = mainProp?.GetValue(null);
                _worldToScreenPoint = cameraType.GetMethod("WorldToScreenPoint",
                    new[] { _asm.GetType("UnityEngine.Vector3") ?? Type.GetType("UnityEngine.Vector3, UnityEngine") });
            }

            var screenType = _asm.GetType("UnityEngine.Screen");
            if (screenType != null)
            {
                _screenWidth = screenType.GetProperty("width", BindingFlags.Public | BindingFlags.Static);
                _screenHeight = screenType.GetProperty("height", BindingFlags.Public | BindingFlags.Static);
            }
        }

        public static void RefreshCamera()
        {
            try
            {
                if (_asm == null) return;
                var cameraType = _asm.GetType("UnityEngine.Camera");
                var mainProp = cameraType?.GetProperty("main", BindingFlags.Public | BindingFlags.Static);
                _mainCamera = mainProp?.GetValue(null);
            }
            catch { }
        }

        #endregion
    }
}

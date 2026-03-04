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
                if (_worldToScreenPoint == null) return false;

                // 每次都刷新 Camera.main 引用。
                // Unity 销毁对象后 C# 引用不为 null，但通过反射调用会抛异常。
                // 频繁调 Camera.main 代价极低（Unity 内部有缓存），远比因陈旧引用导致
                // 连锁定位失败要好。
                RefreshCamera();
                if (_mainCamera == null) return false;

                // 用 Unity 的隐式 bool 转换检测已销毁的对象
                if (IsUnityObjectDestroyed(_mainCamera))
                {
                    _mainCamera = null;
                    RefreshCamera();
                    if (_mainCamera == null) return false;
                }

                var vector3Type = _asm.GetType("UnityEngine.Vector3")
                    ?? Type.GetType("UnityEngine.Vector3, UnityEngine.CoreModule")
                    ?? Type.GetType("UnityEngine.Vector3, UnityEngine");
                if (vector3Type == null) return false;

                var worldPos = Activator.CreateInstance(vector3Type, worldX, worldY, worldZ);

                object screenPos;
                try
                {
                    screenPos = _worldToScreenPoint.Invoke(_mainCamera, new[] { worldPos });
                }
                catch
                {
                    // 可能是陈旧的相机引用，刷新后重试一次
                    RefreshCamera();
                    if (_mainCamera == null) return false;
                    screenPos = _worldToScreenPoint.Invoke(_mainCamera, new[] { worldPos });
                }

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

        /// <summary>
        /// 检查 Unity 对象是否已被销毁（利用 UnityEngine.Object 的隐式 bool 转换）
        /// </summary>
        private static bool IsUnityObjectDestroyed(object obj)
        {
            if (obj == null) return true;
            try
            {
                // Unity 重载了 Object 的 == 操作符和 implicit bool，
                // 对已销毁对象返回 false。通过反射调用 op_Implicit
                var type = obj.GetType();
                // 向上找到 UnityEngine.Object 基类
                var unityObjType = type;
                while (unityObjType != null && unityObjType.FullName != "UnityEngine.Object")
                    unityObjType = unityObjType.BaseType;
                if (unityObjType == null) return false; // 不是 Unity 对象

                // 方式1: 尝试 op_Implicit(Object) -> bool
                var opImplicit = unityObjType.GetMethod("op_Implicit",
                    BindingFlags.Public | BindingFlags.Static,
                    null, new[] { unityObjType }, null);
                if (opImplicit != null && opImplicit.ReturnType == typeof(bool))
                {
                    return !(bool)opImplicit.Invoke(null, new[] { obj });
                }

                // 方式2: 尝试 op_Equality(Object, Object) 与 null 比较
                var opEquality = unityObjType.GetMethod("op_Equality",
                    BindingFlags.Public | BindingFlags.Static,
                    null, new[] { unityObjType, unityObjType }, null);
                if (opEquality != null)
                {
                    return (bool)opEquality.Invoke(null, new object[] { obj, null });
                }
            }
            catch { }
            return false;
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

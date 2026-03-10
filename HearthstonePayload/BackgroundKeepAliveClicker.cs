using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace HearthstonePayload
{
    internal sealed class BackgroundKeepAliveClicker
    {
        private const string MethodName = "wm_mouse";
        private const uint GW_OWNER = 4;
        private const uint WM_MOUSEMOVE = 0x0200;
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const int MK_LBUTTON = 0x0001;

        private readonly GameReader _reader;
        private readonly SceneNavigator _nav;
        private readonly Random _random = new Random();
        private readonly object _randomLock = new object();

        public BackgroundKeepAliveClicker(GameReader reader, SceneNavigator nav)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _nav = nav ?? throw new ArgumentNullException(nameof(nav));
        }

        public string Click()
        {
            try
            {
                var scene = NormalizeScene(_nav.GetScene());
                KeepAliveRectSpec rectSpec;
                string skipReason;
                if (!TryResolveRectSpec(scene, out rectSpec, out skipReason))
                    return BuildSkip(scene, skipReason);

                IntPtr windowHandle;
                NativeRect clientRect;
                string windowError;
                if (!TryFindWindow(out windowHandle, out clientRect, out windowError))
                    return BuildError(windowError);

                if (clientRect.Width < 100 || clientRect.Height < 100)
                    return BuildError("client_rect_invalid");

                // 激活窗口以确保点击生效
                if (GetForegroundWindow() != windowHandle)
                    SetForegroundWindow(windowHandle);

                var targetRect = rectSpec.Resolve(clientRect);
                if (targetRect.Width < 8 || targetRect.Height < 8)
                    return BuildError("target_rect_invalid");

                var point = PickPoint(targetRect);
                string dispatchError;
                if (!TryDispatchClick(windowHandle, point, out dispatchError))
                    return BuildError(dispatchError);

                LogInfo(
                    string.Format(
                        "[KeepAlive] scene={0} method={1} targetRect={2},{3},{4},{5} x={6} y={7}",
                        scene,
                        MethodName,
                        targetRect.Left,
                        targetRect.Top,
                        targetRect.Right,
                        targetRect.Bottom,
                        point.X,
                        point.Y));

                return string.Format("OK:KEEPALIVE:{0}:{1}:{2},{3}", scene, MethodName, point.X, point.Y);
            }
            catch (Exception ex)
            {
                return BuildError("exception_" + SanitizeToken(ex.GetBaseException().Message));
            }
        }

        private bool TryResolveRectSpec(string scene, out KeepAliveRectSpec rectSpec, out string skipReason)
        {
            rectSpec = null;
            skipReason = null;

            if (string.Equals(scene, "HUB", StringComparison.OrdinalIgnoreCase))
            {
                rectSpec = new KeepAliveRectSpec(0.38f, 0.62f, 0.62f, 0.74f);
                return true;
            }

            if (string.Equals(scene, "TOURNAMENT", StringComparison.OrdinalIgnoreCase))
            {
                var isFindingGame = _nav.IsFindingGame();
                rectSpec = isFindingGame
                    ? new KeepAliveRectSpec(0.38f, 0.34f, 0.62f, 0.44f)
                    : new KeepAliveRectSpec(0.39f, 0.58f, 0.61f, 0.69f);
                return true;
            }

            if (string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
            {
                var state = _reader.ReadGameState();
                if (state == null)
                {
                    skipReason = "gameplay_state_null";
                    return false;
                }

                if (state.IsMulliganPhase)
                {
                    skipReason = "mulligan";
                    return false;
                }

                if (state.IsOurTurn)
                {
                    skipReason = "our_turn";
                    return false;
                }

                if (state.IsGameOver || state.Result != GameResult.None)
                {
                    skipReason = "game_over";
                    return false;
                }

                if (_reader.IsEndGameScreenShown(out var endgameClass))
                {
                    skipReason = string.IsNullOrWhiteSpace(endgameClass)
                        ? "endgame_screen"
                        : "endgame_" + SanitizeToken(endgameClass);
                    return false;
                }

                rectSpec = new KeepAliveRectSpec(0.18f, 0.63f, 0.32f, 0.73f);
                return true;
            }

            skipReason = "scene_" + SanitizeToken(scene);
            return false;
        }

        private static string NormalizeScene(string scene)
        {
            scene = (scene ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(scene) ? "UNKNOWN" : scene.ToUpperInvariant();
        }

        private bool TryFindWindow(out IntPtr windowHandle, out NativeRect clientRect, out string error)
        {
            var pid = Process.GetCurrentProcess().Id;
            var bestArea = -1;
            var bestHandle = IntPtr.Zero;
            var bestRect = default(NativeRect);
            var bestError = "window_not_found";

            EnumWindows((candidate, _) =>
            {
                int windowPid;
                GetWindowThreadProcessId(candidate, out windowPid);
                if (windowPid != pid)
                    return true;

                if (!IsWindowVisible(candidate))
                    return true;

                if (GetWindow(candidate, GW_OWNER) != IntPtr.Zero)
                    return true;

                if (IsIconic(candidate))
                {
                    bestError = "window_minimized";
                    return true;
                }

                NativeRect rect;
                if (!GetClientRect(candidate, out rect))
                    return true;

                if (rect.Width <= 0 || rect.Height <= 0)
                    return true;

                var area = rect.Width * rect.Height;
                if (area <= bestArea)
                    return true;

                bestArea = area;
                bestHandle = candidate;
                bestRect = rect;
                bestError = null;
                return true;
            }, IntPtr.Zero);

            windowHandle = bestHandle;
            clientRect = bestRect;
            error = bestError;
            return windowHandle != IntPtr.Zero;
        }

        private ClientPoint PickPoint(NativeRect rect)
        {
            lock (_randomLock)
            {
                var minX = rect.Left + 2;
                var maxX = Math.Max(minX + 1, rect.Right - 2);
                var minY = rect.Top + 2;
                var maxY = Math.Max(minY + 1, rect.Bottom - 2);

                return new ClientPoint
                {
                    X = _random.Next(minX, maxX),
                    Y = _random.Next(minY, maxY)
                };
            }
        }

        private bool TryDispatchClick(IntPtr windowHandle, ClientPoint point, out string error)
        {
            error = null;
            var lParam = MakeLParam(point.X, point.Y);

            if (!PostMessage(windowHandle, WM_MOUSEMOVE, IntPtr.Zero, lParam))
            {
                error = "mouse_move_" + Marshal.GetLastWin32Error();
                return false;
            }

            Thread.Sleep(15);

            if (!PostMessage(windowHandle, WM_LBUTTONDOWN, new IntPtr(MK_LBUTTON), lParam))
            {
                error = "mouse_down_" + Marshal.GetLastWin32Error();
                return false;
            }

            Thread.Sleep(35);

            if (!PostMessage(windowHandle, WM_LBUTTONUP, IntPtr.Zero, lParam))
            {
                error = "mouse_up_" + Marshal.GetLastWin32Error();
                return false;
            }

            return true;
        }

        private static IntPtr MakeLParam(int x, int y)
        {
            return new IntPtr(((y & 0xFFFF) << 16) | (x & 0xFFFF));
        }

        private string BuildSkip(string scene, string reason)
        {
            LogInfo(
                string.Format(
                    "[KeepAlive] scene={0} method={1} skipReason={2}",
                    scene,
                    MethodName,
                    reason));
            return "SKIP:KEEPALIVE:" + reason;
        }

        private string BuildError(string reason)
        {
            LogWarning(
                string.Format(
                    "[KeepAlive] method={0} skipReason={1}",
                    MethodName,
                    reason));
            return "ERROR:KEEPALIVE:" + reason;
        }

        private static string SanitizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value.Trim())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(char.ToLowerInvariant(ch));
                }
                else if (builder.Length == 0 || builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }
            }

            var result = builder.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
        }

        private static void LogInfo(string message)
        {
            try { UnityEngine.Debug.Log(message); } catch { }
        }

        private static void LogWarning(string message)
        {
            try { UnityEngine.Debug.LogWarning(message); } catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        private struct ClientPoint
        {
            public int X;
            public int Y;
        }

        private sealed class KeepAliveRectSpec
        {
            private readonly float _left;
            private readonly float _top;
            private readonly float _right;
            private readonly float _bottom;

            public KeepAliveRectSpec(float left, float top, float right, float bottom)
            {
                _left = left;
                _top = top;
                _right = right;
                _bottom = bottom;
            }

            public NativeRect Resolve(NativeRect clientRect)
            {
                var width = clientRect.Width;
                var height = clientRect.Height;

                var left = Math.Max(0, Math.Min(width - 1, (int)Math.Round(width * _left)));
                var top = Math.Max(0, Math.Min(height - 1, (int)Math.Round(height * _top)));
                var right = Math.Max(left + 6, Math.Min(width, (int)Math.Round(width * _right)));
                var bottom = Math.Max(top + 6, Math.Min(height, (int)Math.Round(height * _bottom)));

                return new NativeRect
                {
                    Left = left,
                    Top = top,
                    Right = right,
                    Bottom = bottom
                };
            }
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out NativeRect lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}

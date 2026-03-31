using System;
using System.Reflection;
using System.Threading;
using BepInEx.Logging;
using HarmonyLib;

namespace HearthstonePayload
{
    /// <summary>
    /// 段位伪装 v5：调用游戏自带的 CheatLocalOverride 接口
    ///
    /// NetCache.NetCacheMedalInfo 有两个调试方法:
    ///   CheatLocalOverrideStarLevel(FormatType, int starLevel)
    ///   CheatLocalOverrideLegendRank(FormatType, int legendRank)
    ///
    /// 这些方法直接修改 MedalData 字典里的值 (数据源头)
    /// NeteaseHSRecord.dll 读的就是这个数据
    ///
    /// 优点: 不 hook 任何函数, 不做 inline patch, 用游戏自己的 API
    /// </summary>
    public static class MedalSpoof
    {
        private static ManualLogSource _log;
        private static Timer _timer;

        // ── 测试：伪装成传说20000名 ──
        // 验证通过后改成钻石5: starLevel=5, legendRank=0, leagueId=1
        private const int FAKE_STAR_LEVEL = 1;
        private const int FAKE_LEGEND_RANK = 20000;

        public static bool Enabled { get; set; } = true;

        // 反射缓存
        private static Type _netCacheType;
        private static Type _netCacheMedalInfoType;
        private static Type _formatTypeType;
        private static MethodInfo _netCacheGet;
        private static MethodInfo _getNetObject;
        private static MethodInfo _cheatStarLevel;
        private static MethodInfo _cheatLegendRank;
        private static MethodInfo _gameStateGet;
        private static bool _initialized;
        private static bool _spoofed;

        // FormatType 枚举值
        private static object _ftStandard;
        private static object _ftWild;

        public static void Apply(Harmony harmony, ManualLogSource log = null)
        {
            _log = log;
            Log("v5 启动: 使用游戏自带 CheatLocalOverride 接口");
            _timer = new Timer(Tick, null, 3000, 1000);
        }

        private static void Tick(object state)
        {
            if (!Enabled) return;

            try
            {
                if (!_initialized && !TryInit()) return;

                bool inGame = IsInGame();

                if (inGame && !_spoofed)
                {
                    DoSpoof();
                }
                else if (!inGame && _spoofed)
                {
                    // 对局结束, 清除作弊数据让游戏恢复正常
                    DoClear();
                }
            }
            catch (Exception ex)
            {
                Log($"Tick error: {ex.Message}");
            }
        }

        private static bool TryInit()
        {
            try
            {
                var asm = FindAssembly("Assembly-CSharp");
                if (asm == null) return false;

                _netCacheType = asm.GetType("NetCache");
                if (_netCacheType == null) return false;

                // NetCache.Get()
                _netCacheGet = _netCacheType.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
                if (_netCacheGet == null) return false;

                // NetCache.NetCacheMedalInfo (嵌套类)
                _netCacheMedalInfoType = _netCacheType.GetNestedType("NetCacheMedalInfo",
                    BindingFlags.Public | BindingFlags.NonPublic);
                if (_netCacheMedalInfoType == null)
                {
                    Log("NetCacheMedalInfo 嵌套类未找到");
                    return false;
                }

                // GetNetObject<NetCacheMedalInfo>() — 泛型方法
                var getNetObjectGeneric = _netCacheType.GetMethod("GetNetObject",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (getNetObjectGeneric != null && getNetObjectGeneric.IsGenericMethod)
                {
                    _getNetObject = getNetObjectGeneric.MakeGenericMethod(_netCacheMedalInfoType);
                }
                else
                {
                    // 可能有多个重载，找泛型的那个
                    foreach (var m in _netCacheType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (m.Name == "GetNetObject" && m.IsGenericMethod && m.GetParameters().Length == 0)
                        {
                            _getNetObject = m.MakeGenericMethod(_netCacheMedalInfoType);
                            break;
                        }
                    }
                }

                if (_getNetObject == null)
                {
                    Log("GetNetObject 方法未找到");
                    return false;
                }

                // CheatLocalOverrideStarLevel / CheatLocalOverrideLegendRank
                _cheatStarLevel = _netCacheMedalInfoType.GetMethod("CheatLocalOverrideStarLevel",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _cheatLegendRank = _netCacheMedalInfoType.GetMethod("CheatLocalOverrideLegendRank",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (_cheatStarLevel == null || _cheatLegendRank == null)
                {
                    Log($"Cheat 方法: starLevel={_cheatStarLevel != null}, legendRank={_cheatLegendRank != null}");
                    return false;
                }

                // FormatType 枚举
                _formatTypeType = _cheatStarLevel.GetParameters()[0].ParameterType;
                _ftStandard = Enum.Parse(_formatTypeType, "FT_STANDARD");
                _ftWild = Enum.Parse(_formatTypeType, "FT_WILD");

                // GameState.Get() 判断是否在对局中
                var gsType = asm.GetType("GameState");
                _gameStateGet = gsType?.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);

                _initialized = true;
                Log("初始化成功: CheatLocalOverrideStarLevel + CheatLocalOverrideLegendRank 已就绪");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Init error: {ex.Message}");
                return false;
            }
        }

        private static bool IsInGame()
        {
            try { return _gameStateGet?.Invoke(null, null) != null; }
            catch { return false; }
        }

        private static object GetMedalInfo()
        {
            try
            {
                var nc = _netCacheGet.Invoke(null, null);
                if (nc == null) return null;
                return _getNetObject.Invoke(nc, null);
            }
            catch { return null; }
        }

        private static void DoSpoof()
        {
            var medalInfo = GetMedalInfo();
            if (medalInfo == null)
            {
                Log("NetCacheMedalInfo 未就绪");
                return;
            }

            try
            {
                // 标准模式和狂野模式都改
                _cheatStarLevel.Invoke(medalInfo, new object[] { _ftStandard, FAKE_STAR_LEVEL });
                _cheatLegendRank.Invoke(medalInfo, new object[] { _ftStandard, FAKE_LEGEND_RANK });
                _cheatStarLevel.Invoke(medalInfo, new object[] { _ftWild, FAKE_STAR_LEVEL });
                _cheatLegendRank.Invoke(medalInfo, new object[] { _ftWild, FAKE_LEGEND_RANK });

                _spoofed = true;
                Log($"段位已篡改: 标准+狂野 → 传说{FAKE_LEGEND_RANK}名 (starLevel={FAKE_STAR_LEVEL})");
            }
            catch (Exception ex)
            {
                Log($"Spoof error: {ex.Message}");
            }
        }

        private static void DoClear()
        {
            // NetCacheMedalInfo 有 ClearCheatOverrides() 方法
            var medalInfo = GetMedalInfo();
            if (medalInfo != null)
            {
                try
                {
                    var clearMethod = _netCacheMedalInfoType.GetMethod("ClearCheatOverrides",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (clearMethod != null)
                    {
                        clearMethod.Invoke(medalInfo, null);
                        Log("作弊数据已清除");
                    }
                }
                catch { }
            }
            _spoofed = false;
        }

        private static Assembly FindAssembly(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if (asm.GetName().Name == name) return asm;
            return null;
        }

        private static void Log(string msg)
        {
            _log?.LogInfo($"[MedalSpoof] {msg}");
            try
            {
                var logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HearthstoneBot");
                System.IO.Directory.CreateDirectory(logDir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(logDir, "medal_spoof.log"),
                    $"{DateTime.Now:yyyy/MM/dd HH:mm:ss.fff} {msg}\r\n");
            }
            catch { }
        }
    }
}

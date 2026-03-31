using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using HarmonyLib;

namespace HearthstonePayload
{
    /// <summary>
    /// IPC 嗅探器 v2：不 hook 任何函数，安全监听
    ///
    /// 方案: 创建一个隐藏窗口注册为 "heart_overlay_ipc_win" 类
    /// 或者用 SetWindowsHookEx (WH_CALLWNDPROC) 全局监听消息
    ///
    /// 更安全的方案: 直接枚举共享内存，或者监听 PostMessage
    ///
    /// 最安全的方案: 在后台线程定期 dump NeteaseHSRecord.dll 的内存
    /// 搜索 JSON 格式的推荐数据 ({"data":[{"actionName":...)
    /// </summary>
    public static class IpcSniffer
    {
        private static ManualLogSource _log;
        private static string _logPath;
        private static Timer _timer;
        private static IntPtr _neteaseBase;
        private static int _neteaseSize;
        private static bool _initialized;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32.dll")]
        private static extern void GetModuleInformation(IntPtr hProcess, IntPtr hModule,
            out MODULEINFO info, int cb);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [StructLayout(LayoutKind.Sequential)]
        private struct MODULEINFO
        {
            public IntPtr lpBaseOfDll;
            public int SizeOfImage;
            public IntPtr EntryPoint;
        }

        public static void Apply(Harmony harmony, ManualLogSource log)
        {
            _log = log;

            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HearthstoneBot");
            Directory.CreateDirectory(logDir);
            _logPath = Path.Combine(logDir, "ipc_sniff.log");
            File.WriteAllText(_logPath, $"=== IPC Sniffer v2 started {DateTime.Now} ===\r\n");

            // 用定时器扫描 NeteaseHSRecord.dll 的内存
            // 搜索 JSON 推荐数据和段位数据
            _timer = new Timer(ScanTick, null, 5000, 3000);
            LogIpc("定时器已启动, 等待 NeteaseHSRecord.dll 加载...");
        }

        private static void ScanTick(object state)
        {
            try
            {
                if (!_initialized)
                {
                    var handle = GetModuleHandle("NeteaseHSRecord.dll");
                    if (handle == IntPtr.Zero) return;

                    _neteaseBase = handle;
                    try
                    {
                        GetModuleInformation(GetCurrentProcess(), handle,
                            out MODULEINFO info, Marshal.SizeOf<MODULEINFO>());
                        _neteaseSize = info.SizeOfImage;
                    }
                    catch
                    {
                        _neteaseSize = 0x600000;
                    }

                    _initialized = true;
                    LogIpc($"NeteaseHSRecord.dll: base=0x{_neteaseBase.ToInt32():X8} size=0x{_neteaseSize:X}");

                    // 首次: dump 一下 NeteaseHSRecord.dll 内存中的可读字符串
                    // 找到 JSON 数据或段位相关内容
                    ScanForStrings();
                }

                // 每次: 扫描内存中的 JSON 推荐数据
                ScanForJson();
            }
            catch (Exception ex)
            {
                LogIpc($"ScanTick error: {ex.Message}");
            }
        }

        private static void ScanForStrings()
        {
            LogIpc("扫描 NeteaseHSRecord.dll 内存中的字符串...");
            int found = 0;

            try
            {
                // 扫描整个模块地址空间
                for (int offset = 0; offset < _neteaseSize - 8; offset += 1)
                {
                    try
                    {
                        IntPtr addr = new IntPtr(_neteaseBase.ToInt32() + offset);

                        // 读 8 字节看看是否像字符串开头
                        byte b = Marshal.ReadByte(addr);
                        if (b < 0x20 || b > 0x7E) continue;

                        // 尝试读一段 ASCII
                        var sb = new StringBuilder();
                        for (int i = 0; i < 200; i++)
                        {
                            byte c = Marshal.ReadByte(addr + i);
                            if (c == 0) break;
                            if (c < 0x20 || c > 0x7E) { sb.Clear(); break; }
                            sb.Append((char)c);
                        }

                        string s = sb.ToString();
                        if (s.Length < 6) continue;

                        string low = s.ToLower();
                        if (low.Contains("medal") || low.Contains("league") || low.Contains("rank") ||
                            low.Contains("legend") || low.Contains("star_level") || low.Contains("starlevel") ||
                            low.Contains("recommend") || low.Contains("action") || low.Contains("format") ||
                            low.Contains("ladder") || low.Contains("30000") || low.Contains("50000") ||
                            low.Contains("actionname") || low.Contains("choiceid"))
                        {
                            LogIpc($"  0x{offset:X6}: {s.Substring(0, Math.Min(s.Length, 120))}");
                            found++;
                            offset += s.Length; // 跳过这个字符串
                        }
                    }
                    catch { break; } // 访问违规，到边界了
                }
            }
            catch { }

            LogIpc($"字符串扫描完成, 找到 {found} 个");
        }

        private static string _lastJsonHash = "";
        private static int _scanCount = 0;

        [DllImport("kernel32.dll")]
        private static extern int VirtualQuery(IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, int dwLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public int AllocationProtect;
            public IntPtr RegionSize;
            public int State;
            public int Protect;
            public int Type;
        }

        private const int MEM_COMMIT = 0x1000;
        private const int PAGE_READWRITE = 0x04;
        private const int PAGE_EXECUTE_READWRITE = 0x40;
        private const int PAGE_READONLY = 0x02;
        private const int PAGE_WRITECOPY = 0x08;

        private static void ScanForJson()
        {
            _scanCount++;
            // 每 10 次才全量扫描一次 (约30秒), 避免性能问题
            if (_scanCount % 10 != 1) return;

            int found = 0;
            IntPtr addr = new IntPtr(0x10000);
            IntPtr maxAddr = new IntPtr(0x7FFE0000);

            byte[] searchActionName = Encoding.ASCII.GetBytes("\"actionName\"");
            byte[] searchChoiceId = Encoding.ASCII.GetBytes("{\"choiceId\"");
            byte[] searchLeagueId = Encoding.ASCII.GetBytes("leagueId");
            byte[] searchLegendRank = Encoding.ASCII.GetBytes("legendRank");
            byte[] searchStarLevel = Encoding.ASCII.GetBytes("starLevel");
            byte[] searchMedalInfo = Encoding.ASCII.GetBytes("medalInfo");

            try
            {
                while (addr.ToInt32() < maxAddr.ToInt32() && found < 50)
                {
                    MEMORY_BASIC_INFORMATION mbi;
                    int result = VirtualQuery(addr, out mbi, Marshal.SizeOf<MEMORY_BASIC_INFORMATION>());
                    if (result == 0) break;

                    int regionBase = mbi.BaseAddress.ToInt32();
                    int regionSize = mbi.RegionSize.ToInt32();

                    // 只扫可读写的已提交内存 (堆)
                    if (mbi.State == MEM_COMMIT && regionSize > 0 && regionSize < 0x4000000 &&
                        (mbi.Protect == PAGE_READWRITE || mbi.Protect == PAGE_EXECUTE_READWRITE ||
                         mbi.Protect == PAGE_READONLY || mbi.Protect == PAGE_WRITECOPY))
                    {
                        try
                        {
                            byte[] region = new byte[regionSize];
                            Marshal.Copy(mbi.BaseAddress, region, 0, regionSize);

                            // 搜索所有模式
                            byte[][] patterns = { searchActionName, searchChoiceId, searchLeagueId,
                                                  searchLegendRank, searchStarLevel, searchMedalInfo };

                            foreach (var pattern in patterns)
                            {
                                int pos = 0;
                                while (pos < region.Length - pattern.Length)
                                {
                                    pos = IndexOf(region, pattern, pos);
                                    if (pos < 0) break;

                                    // 向前找 JSON 开头 {
                                    int jsonStart = pos;
                                    for (int back = 1; back < 500 && pos - back >= 0; back++)
                                    {
                                        if (region[pos - back] == (byte)'{')
                                        {
                                            jsonStart = pos - back;
                                            break;
                                        }
                                    }

                                    // 读内容
                                    int len = 0;
                                    var sb = new StringBuilder();
                                    for (int i = jsonStart; i < Math.Min(region.Length, jsonStart + 2000); i++)
                                    {
                                        byte c = region[i];
                                        if (c == 0) break;
                                        if (c >= 0x20 && c <= 0x7E)
                                            sb.Append((char)c);
                                        len++;
                                    }

                                    string content = sb.ToString();
                                    if (content.Length > 20)
                                    {
                                        string hash = (regionBase + pos) + "_" + content.Length;
                                        LogIpc($"[堆扫描] 地址=0x{regionBase + pos:X8} 模式={Encoding.ASCII.GetString(pattern)} 长度={content.Length}");
                                        LogIpc($"  {content.Substring(0, Math.Min(content.Length, 400))}");
                                        found++;
                                    }

                                    pos += pattern.Length;
                                    if (found >= 50) break;
                                }
                                if (found >= 50) break;
                            }
                        }
                        catch { } // 某些页面可能无法读取
                    }

                    addr = new IntPtr(regionBase + Math.Max(regionSize, 0x1000));
                }
            }
            catch { }

            if (found > 0)
                LogIpc($"堆扫描完成: {found} 个匹配");
        }

        private static int IndexOf(byte[] data, byte[] pattern, int startIndex)
        {
            int end = data.Length - pattern.Length;
            for (int i = startIndex; i <= end; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        private static bool StartsWith(byte[] data, byte[] prefix)
        {
            if (data.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (data[i] != prefix[i]) return false;
            }
            return true;
        }

        private static void LogIpc(string msg)
        {
            try
            {
                if (_logPath != null)
                    File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}\r\n");
            }
            catch { }
        }
    }
}

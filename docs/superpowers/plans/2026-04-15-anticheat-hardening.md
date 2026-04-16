# 反作弊加固方案A 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在现有 BepInEx + Harmony 架构上补全反作弊检测缺口，实现动态全量拦截、Telemetry 阻断、BepInEx 指纹消除、日志安全管理、方法变更监控。

**Architecture:** HearthstonePayload DLL 在游戏进程内运行，通过 Harmony prefix patch 拦截 Assembly-CSharp 中的反作弊和遥测方法。新增 LogManager 处理日志轮转，AntiCheatPatches 重写为动态枚举 + 快照比对架构。

**Tech Stack:** C# / .NET Framework / BepInEx 5.4 / HarmonyLib 2.x / System.Reflection

---

### Task 1: 新增 LogManager 日志安全管理

**Files:**
- Create: `HearthstonePayload/LogManager.cs`

- [ ] **Step 1: 创建 LogManager.cs**

```csharp
using System;
using System.IO;
using System.Text;

namespace HearthstonePayload
{
    /// <summary>
    /// 启动时清理和轮转日志文件，防止日志无限增长成为取证指纹。
    /// </summary>
    public static class LogManager
    {
        private const long ErrorLogMaxBytes = 5 * 1024 * 1024;      // 5MB
        private const long ErrorLogTailBytes = 1 * 1024 * 1024;     // 保留最后 1MB
        private const long StartupLogMaxBytes = 2 * 1024 * 1024;    // 2MB
        private const long StartupLogTailBytes = 512 * 1024;        // 保留最后 512KB

        /// <summary>
        /// 在 Entry.Awake() 最早期调用，先于任何新日志写入。
        /// </summary>
        public static void CleanupLogs(string pluginDir)
        {
            TruncateIfOversized(Path.Combine(pluginDir, "payload_error.log"), ErrorLogMaxBytes, ErrorLogTailBytes);
            TruncateIfOversized(Path.Combine(pluginDir, "payload_startup.log"), StartupLogMaxBytes, StartupLogTailBytes);
        }

        private static void TruncateIfOversized(string filePath, long maxBytes, long tailBytes)
        {
            try
            {
                if (!File.Exists(filePath))
                    return;

                var info = new FileInfo(filePath);
                if (info.Length <= maxBytes)
                    return;

                // 轮转：当前文件 → .old（覆盖已有 .old）
                var oldPath = filePath + ".old";
                if (File.Exists(oldPath))
                    File.Delete(oldPath);
                File.Move(filePath, oldPath);

                // 从 .old 中读取尾部 tailBytes 写入新文件
                byte[] tail;
                using (var fs = new FileStream(oldPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var offset = fs.Length - tailBytes;
                    if (offset < 0) offset = 0;
                    fs.Seek(offset, SeekOrigin.Begin);
                    tail = new byte[fs.Length - offset];
                    fs.Read(tail, 0, tail.Length);
                }

                // 跳到第一个换行符后开始，避免截断行
                var start = 0;
                for (var i = 0; i < tail.Length && i < 4096; i++)
                {
                    if (tail[i] == (byte)'\n')
                    {
                        start = i + 1;
                        break;
                    }
                }

                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    fs.Write(tail, start, tail.Length - start);
                }
            }
            catch
            {
                // 日志清理失败不能阻止插件启动
            }
        }
    }
}
```

- [ ] **Step 2: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add HearthstonePayload/LogManager.cs
git commit -m "新增 LogManager：日志轮转和安全清理"
```

---

### Task 2: Entry.cs 集成 LogManager

**Files:**
- Modify: `HearthstonePayload/Entry.cs:55-62`

- [ ] **Step 1: 在 Awake 中添加 LogManager.CleanupLogs() 调用**

在 `Entry.cs` 的 Awake 方法中，`StartStartupLogSession()` 之前插入日志清理调用：

```csharp
// 修改前（第 55-62 行）：
        private void Awake()
        {
            try
            {
                _logSource = Logger;
                StartStartupLogSession();
                SetPhase("awake");
                LogStartupInfo("awake", "Plugin Awake started.");

// 修改后：
        private void Awake()
        {
            try
            {
                _logSource = Logger;
                LogManager.CleanupLogs(GetPluginLogDirectory());
                StartStartupLogSession();
                SetPhase("awake");
                LogStartupInfo("awake", "Plugin Awake started.");
```

注意：`GetPluginLogDirectory()` 已存在于 Entry.cs 第 1115 行，是 `private static` 方法。需要将其可见性从 `private` 改为 `internal`，以便 LogManager 也能调用（虽然此处是 Plugin 类内部调用所以不需要改，直接传参即可）。

- [ ] **Step 2: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add HearthstonePayload/Entry.cs
git commit -m "Entry.Awake 集成 LogManager 日志清理"
```

---

### Task 3: 重写 AntiCheatPatches — 动态全量拦截

**Files:**
- Modify: `HearthstonePayload/AntiCheatPatches.cs` (完全重写)

- [ ] **Step 1: 重写 AntiCheatPatches.cs**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace HearthstonePayload
{
    /// <summary>
    /// 动态枚举并全量拦截 AntiCheatManager 及 Telemetry 相关类的全部方法。
    /// 支持类名变更时的模糊匹配和方法快照比对。
    /// </summary>
    public static class AntiCheatPatches
    {
        // System.Object 基础方法 + 构造函数，不应 patch
        private static readonly HashSet<string> SkipMethods = new HashSet<string>
        {
            "ToString", "GetHashCode", "Equals", "GetType", "Finalize",
            "MemberwiseClone", "ReferenceEquals", "obj_address"
        };

        // AntiCheatManager 特征方法，用于类名变更时的模糊匹配
        private static readonly string[] AcSignatureMethods =
        {
            "ReportCheat", "InitSDK", "TryCallSDK", "ReportActivity"
        };

        // Telemetry 类名关键字
        private static readonly string[] TelemetryKeywords =
        {
            "Telemetry", "Analytics", "Reporting"
        };

        private static readonly List<string> _patchLog = new List<string>();

        public static void Apply(Harmony harmony, Action<string, string> log = null)
        {
            _patchLog.Clear();
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asm == null)
                {
                    Log("Assembly-CSharp not found");
                    return;
                }

                // --- 1. AntiCheatManager 拦截 ---
                var acmType = asm.GetType("AntiCheatManager");
                if (acmType != null)
                {
                    PatchAllMethods(harmony, acmType, "AntiCheatManager");
                }
                else
                {
                    // 类名可能被改，模糊搜索
                    Log("[AC_CHANGE] AntiCheatManager class not found, scanning by signature methods...");
                    var candidates = FindTypesBySignature(asm, AcSignatureMethods, 2);
                    foreach (var candidate in candidates)
                    {
                        Log("[AC_CHANGE] Fuzzy match: " + candidate.FullName);
                        PatchAllMethods(harmony, candidate, candidate.Name);
                    }
                    if (candidates.Count == 0)
                        Log("[AC_CHANGE] WARNING: No AntiCheatManager candidate found!");
                }

                // --- 2. Telemetry 类拦截 ---
                PatchTypesByKeyword(harmony, asm, TelemetryKeywords);

                // --- 3. 快照比对 ---
                CompareAndSaveSnapshot(asm);
            }
            catch (Exception ex)
            {
                Log("AntiCheatPatches.Apply failed: " + ex.Message);
            }

            // 输出 patch 日志
            if (log != null)
            {
                foreach (var line in _patchLog)
                    log("anticheat_patch", line);
            }
        }

        /// <summary>
        /// 获取本次 Apply 的全部日志行（供 Entry.cs 写入 startup log）
        /// </summary>
        public static IReadOnlyList<string> GetPatchLog() => _patchLog;

        private static void PatchAllMethods(Harmony harmony, Type type, string label)
        {
            var methods = type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly);

            var count = 0;
            foreach (var mi in methods)
            {
                if (SkipMethods.Contains(mi.Name))
                    continue;
                if (mi.IsSpecialName) // 跳过属性 getter/setter、事件 add/remove
                    continue;

                try
                {
                    var prefix = ChoosePrefix(mi);
                    harmony.Patch(mi, prefix: prefix);
                    count++;
                }
                catch (Exception ex)
                {
                    Log(string.Format("  Failed to patch {0}.{1}: {2}", label, mi.Name, ex.Message));
                }
            }
            Log(string.Format("Patched {0}: {1} methods intercepted", label, count));
        }

        private static HarmonyMethod ChoosePrefix(MethodInfo mi)
        {
            if (mi.ReturnType == typeof(bool))
                return new HarmonyMethod(typeof(AntiCheatPatches), nameof(ReturnFalse));
            if (mi.ReturnType == typeof(void))
                return new HarmonyMethod(typeof(AntiCheatPatches), nameof(SkipOriginal));
            // 其他返回值类型：跳过原方法，__result 保持默认值
            return new HarmonyMethod(typeof(AntiCheatPatches), nameof(SkipOriginal));
        }

        private static void PatchTypesByKeyword(Harmony harmony, Assembly asm, string[] keywords)
        {
            Type[] allTypes;
            try { allTypes = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { allTypes = ex.Types.Where(t => t != null).ToArray(); }

            foreach (var type in allTypes)
            {
                if (type.Name == null) continue;
                var match = false;
                foreach (var kw in keywords)
                {
                    if (type.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        match = true;
                        break;
                    }
                }
                if (match)
                {
                    PatchAllMethods(harmony, type, type.Name);
                }
            }
        }

        private static List<Type> FindTypesBySignature(Assembly asm, string[] signatureMethods, int minMatchCount)
        {
            var result = new List<Type>();
            Type[] allTypes;
            try { allTypes = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { allTypes = ex.Types.Where(t => t != null).ToArray(); }

            foreach (var type in allTypes)
            {
                var methods = type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly);

                var methodNames = new HashSet<string>(methods.Select(m => m.Name));
                var matchCount = signatureMethods.Count(s => methodNames.Contains(s));
                if (matchCount >= minMatchCount)
                    result.Add(type);
            }
            return result;
        }

        // --- 快照比对 ---

        private static void CompareAndSaveSnapshot(Assembly asm)
        {
            try
            {
                var snapshotPath = GetSnapshotPath();
                var current = BuildSnapshot(asm);

                if (File.Exists(snapshotPath))
                {
                    var previous = File.ReadAllText(snapshotPath, System.Text.Encoding.UTF8);
                    var previousLines = new HashSet<string>(previous.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
                    var currentLines = new HashSet<string>(current.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

                    // 新增的方法
                    foreach (var line in currentLines)
                    {
                        if (!previousLines.Contains(line))
                            Log("[AC_CHANGE] NEW: " + line);
                    }

                    // 消失的方法
                    foreach (var line in previousLines)
                    {
                        if (!currentLines.Contains(line))
                            Log("[AC_CHANGE] REMOVED: " + line);
                    }
                }
                else
                {
                    Log("First run: creating AC method snapshot.");
                }

                File.WriteAllText(snapshotPath, current, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log("Snapshot compare/save failed: " + ex.Message);
            }
        }

        private static string BuildSnapshot(Assembly asm)
        {
            var sb = new System.Text.StringBuilder();
            Type[] allTypes;
            try { allTypes = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { allTypes = ex.Types.Where(t => t != null).ToArray(); }

            foreach (var type in allTypes)
            {
                if (type.Name == null) continue;
                var isAc = type.Name == "AntiCheatManager";
                var isTelemetry = false;
                if (!isAc)
                {
                    foreach (var kw in TelemetryKeywords)
                    {
                        if (type.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isTelemetry = true;
                            break;
                        }
                    }
                }
                if (!isAc && !isTelemetry) continue;

                var methods = type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly);
                foreach (var mi in methods)
                {
                    if (SkipMethods.Contains(mi.Name) || mi.IsSpecialName) continue;
                    sb.AppendLine(type.Name + "::" + mi.Name);
                }
            }
            return sb.ToString();
        }

        private static string GetSnapshotPath()
        {
            var dir = Path.GetDirectoryName(typeof(AntiCheatPatches).Assembly.Location);
            if (string.IsNullOrWhiteSpace(dir))
                dir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(dir, "ac_method_snapshot.txt");
        }

        private static void Log(string message)
        {
            _patchLog.Add(message);
        }

        // --- Harmony prefix 方法 ---

        private static bool ReturnFalse() => false;

        private static bool SkipOriginal() => false;
    }
}
```

- [ ] **Step 2: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add HearthstonePayload/AntiCheatPatches.cs
git commit -m "AntiCheatPatches 重写：动态全量拦截 + Telemetry阻断 + 快照比对"
```

---

### Task 4: Entry.cs 适配新版 AntiCheatPatches

**Files:**
- Modify: `HearthstonePayload/Entry.cs:66-71`

- [ ] **Step 1: 修改 AntiCheatPatches.Apply 调用，传入 log 回调并输出 patch 日志**

```csharp
// 修改前（第 64-71 行）：
                UnityEngine.Application.runInBackground = true;
                var harmony = new Harmony("com.bot.hearthstone");
                AntiCheatPatches.Apply(harmony);
                InactivityPatch.Apply(harmony);
                InputHook.Apply(harmony);

                Logger.LogInfo("Harmony patches applied.");
                LogStartupInfo("awake", "Harmony patches applied.");

// 修改后：
                UnityEngine.Application.runInBackground = true;
                var harmony = new Harmony("com.bot.hearthstone");
                AntiCheatPatches.Apply(harmony, LogStartupInfo);
                foreach (var line in AntiCheatPatches.GetPatchLog())
                    LogStartupInfo("anticheat", line);
                InactivityPatch.Apply(harmony);
                InputHook.Apply(harmony);

                Logger.LogInfo("Harmony patches applied.");
                LogStartupInfo("awake", "Harmony patches applied.");
```

注意：`LogStartupInfo` 签名为 `private static void LogStartupInfo(string phase, string message)`，与 `Apply` 的 `Action<string, string> log` 参数类型匹配。

- [ ] **Step 2: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add HearthstonePayload/Entry.cs
git commit -m "Entry.cs 适配新版 AntiCheatPatches，输出 patch 日志到 startup log"
```

---

### Task 5: BepInEx 配置指纹消除

**Files:**
- Modify: `H:\Hearthstone\BepInEx\config\BepInEx.cfg`

- [ ] **Step 1: 修改 4 项配置**

```ini
# 改动 1: 第 17 行
# 改前：HideManagerGameObject = false
# 改后：
HideManagerGameObject = true

# 改动 2: 第 27 行
# 改前：LogChannels = Warn, Error
# 改后：
LogChannels = None

# 改动 3: 第 92 行（[Logging.Disk] 区块）
# 改前：Enabled = true
# 改后：
Enabled = false
```

`[Logging.Console] Enabled = false` 保持不变。

- [ ] **Step 2: 提交（仅提交项目内文件的说明记录，BepInEx.cfg 位于游戏目录非项目仓库）**

BepInEx.cfg 位于 `H:\Hearthstone\BepInEx\config\`，不在 git 仓库内。此变更记录在设计文档中，无需 git 提交。如果项目有部署脚本，应在部署流程中自动应用此配置。

---

### Task 6: 验证与集成测试

**Files:**
- 无新文件，验证现有改动

- [ ] **Step 1: 编译 HearthstonePayload 项目**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
dotnet build HearthstonePayload/ -c Release
```

预期：编译成功，0 errors。

- [ ] **Step 2: 检查编译产物**

确认 `HearthstonePayload.dll` 已生成，复制到 `H:\Hearthstone\BepInEx\plugins\` 目录。

- [ ] **Step 3: 启动游戏验证**

1. 启动炉石传说
2. 检查 `H:\Hearthstone\BepInEx\plugins\payload_startup.log` 最新 session：
   - 确认 `[anticheat]` 开头的日志行存在
   - 确认 `Patched AntiCheatManager: N methods intercepted` 其中 N >= 6
   - 确认有 Telemetry 相关类被 patch 的记录
   - 如有 `[AC_CHANGE]` 行，记录新增/移除的方法
3. 确认 `ac_method_snapshot.txt` 已生成在 plugins 目录
4. 确认 `LogOutput.log` 不再增长（Logging.Disk.Enabled = false）
5. 确认游戏正常运行，bot 功能正常

- [ ] **Step 4: 最终提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add -A
git commit -m "反作弊加固方案A完成：动态拦截 + Telemetry阻断 + 日志管理 + 指纹消除 + 变更监控"
git push
```

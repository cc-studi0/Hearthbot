# 盒子标传传说受限绕过 — 阶段1实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现 Frida hook `HSAng.exe!MatchLogicPrivate::queryMatchAvailable` 强制返回 true 的端到端方案，让 Hearthbot 在标传传说受限档继续拿到盒子推荐。

**Architecture:** 三层 — Hearthbot 内 C# facade 拉起常驻 Python 子进程（`Process.Start`），Python 用 frida-python attach HSAng.exe 加载 Frida JS hook，hook 用 `Interceptor.attach + onLeave` 把判定函数返回值的 0 改成 1。C# facade 监控子进程存活、解析 stdout JSON 行到 Hearthbot 主日志、限速自动重启。

**Tech Stack:** .NET 8 / C#（BotMain + xUnit 测试）, Python 3.13 + frida-tools 17.9.1, Frida JS（V8 runtime）

**Spec 来源：** `docs/superpowers/specs/2026-04-17-hsbox-bypass-stage1-impl-design.md`

---

## 文件结构

| 文件 | 职责 | 操作 |
|------|------|------|
| `BotMain/HsBoxLimitBypass.cs` | C# facade（含 Config/Status/接口/默认实现） | 新增 |
| `BotMain/BotService.cs` | 启停挂钩 | 修改 |
| `BotCore.Tests/HsBoxLimitBypassTests.cs` | 单元测试 | 新增 |
| `tools/active/hsbox_querymatch_hook.js` | Frida JS hook | 新增 |
| `Scripts/queryMatchAvailable_hook.py` | Python 常驻启动器 | 新增 |

注：项目无 appsettings.json，配置走 HumanizerConfig 模式（代码内默认值）。

## 工作原则

- C# facade 部分**严格 TDD**：用 `IBypassProcessHost` 接口隔离 `Process.Start`，单元测试 mock 它
- Frida JS / Python 启动器**不严格 TDD**：mock 工程量比脚本本身大，靠 smoke 验证 + 人工实机回归
- 频繁提交：每个 Task 完成 + 测试通过 = 一次 commit
- 实机回归（4 场景）由用户人工配合验证

## 前置条件

- Python 3.13 + frida-tools 17.9.1 已装（前阶段 0 已验证）
- HSAng.exe 32 位运行时，CDP 9222 可达
- 测试账号能达到标传传说受限档（5 万名以内，月中 11-20 号阈值）

---

## Task 1：C# facade 骨架（接口、配置类、状态枚举）

**Files:**
- Create: `BotMain/HsBoxLimitBypass.cs`

- [ ] **Step 1：创建文件骨架**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace BotMain
{
    internal enum BypassStatus
    {
        Stopped,
        Starting,
        Running,
        Failed,
    }

    internal sealed class BypassConfig
    {
        public bool Enabled { get; set; } = true;
        public string PythonExecutable { get; set; } = "python";
        public int MaxRestartsPerMinute { get; set; } = 3;
        public string LauncherScriptRelative { get; set; } = "Scripts/queryMatchAvailable_hook.py";
        public string FridaJsRelative { get; set; } = "tools/active/hsbox_querymatch_hook.js";
    }

    internal interface IBypassProcess : IDisposable
    {
        bool HasExited { get; }
        int ExitCode { get; }
        StreamReader StandardOutput { get; }
        StreamReader StandardError { get; }
        void Kill();
    }

    internal interface IBypassProcessHost
    {
        IBypassProcess Spawn(string fileName, string arguments, string workingDirectory);
    }

    internal sealed class HsBoxLimitBypass : IDisposable
    {
        private readonly Action<string> _log;
        private readonly IBypassProcessHost _host;
        private readonly BypassConfig _cfg;
        private readonly object _sync = new object();
        private readonly Queue<DateTime> _restartHistory = new Queue<DateTime>();

        private IBypassProcess _proc;
        private Thread _stdoutPump;
        private Thread _stderrPump;
        private bool _disposed;
        private bool _stopRequested;
        private bool _fatalReceived;

        public BypassStatus Status { get; private set; } = BypassStatus.Stopped;

        public HsBoxLimitBypass(Action<string> log, IBypassProcessHost host, BypassConfig cfg)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        }

        public void Start()
        {
            // 留待后续 Task 实现
            throw new NotImplementedException();
        }

        public void Stop()
        {
            // 留待后续 Task 实现
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            // 留待后续 Task 实现
        }
    }
}
```

- [ ] **Step 2：build 通过**

```bash
dotnet build BotMain/BotMain.csproj --nologo --verbosity minimal
```

Expected: `已成功生成` / `Build succeeded` 0 errors。

- [ ] **Step 3：commit**

```bash
git add BotMain/HsBoxLimitBypass.cs
git commit -m "facade: HsBoxLimitBypass 骨架与接口"
```

---

## Task 2：单元测试 — Start 幂等

**Files:**
- Create: `BotCore.Tests/HsBoxLimitBypassTests.cs`

- [ ] **Step 1：创建测试文件骨架 + 第一个测试**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class HsBoxLimitBypassTests
    {
        private sealed class FakeProcess : IBypassProcess
        {
            public bool HasExited { get; private set; }
            public int ExitCode { get; private set; }
            public StreamReader StandardOutput { get; }
            public StreamReader StandardError { get; }
            public bool KillCalled { get; private set; }

            public FakeProcess(string stdout = "", string stderr = "")
            {
                StandardOutput = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(stdout)));
                StandardError = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(stderr)));
            }

            public void Kill() { KillCalled = true; HasExited = true; ExitCode = -1; }
            public void Dispose() { StandardOutput?.Dispose(); StandardError?.Dispose(); }

            public void SimulateExit(int code) { HasExited = true; ExitCode = code; }
        }

        private sealed class FakeHost : IBypassProcessHost
        {
            public int SpawnCount { get; private set; }
            public List<FakeProcess> Spawned { get; } = new List<FakeProcess>();
            public Func<FakeProcess> NextProcessFactory { get; set; } = () => new FakeProcess();
            public Exception NextException { get; set; }

            public IBypassProcess Spawn(string fileName, string arguments, string workingDirectory)
            {
                SpawnCount++;
                if (NextException != null) throw NextException;
                var p = NextProcessFactory();
                Spawned.Add(p);
                return p;
            }
        }

        private static List<string> Logs(out Action<string> sink)
        {
            var list = new List<string>();
            sink = msg => { lock (list) list.Add(msg); };
            return list;
        }

        [Fact]
        public void Start_CalledTwice_OnlyOneSpawn()
        {
            var host = new FakeHost();
            var bypass = new HsBoxLimitBypass(Logs(out var log), host, new BypassConfig());

            bypass.Start();
            bypass.Start();

            Assert.Equal(1, host.SpawnCount);
            Assert.Equal(BypassStatus.Running, bypass.Status);

            bypass.Dispose();
        }
    }
}
```

- [ ] **Step 2：跑测试，预期 FAIL（Start 还没实现）**

```bash
dotnet test BotCore.Tests/BotCore.Tests.csproj --filter Start_CalledTwice_OnlyOneSpawn --nologo --verbosity minimal
```

Expected: FAIL with `NotImplementedException`。

- [ ] **Step 3：实现 Start + 部分 Dispose**

把 `BotMain/HsBoxLimitBypass.cs` 里 Start 和 Dispose 替换为：

```csharp
public void Start()
{
    lock (_sync)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HsBoxLimitBypass));
        if (Status == BypassStatus.Running || Status == BypassStatus.Starting) return;
        Status = BypassStatus.Starting;
        try
        {
            SpawnLocked();
            Status = BypassStatus.Running;
        }
        catch (Exception ex)
        {
            Status = BypassStatus.Failed;
            _log("[LimitBypass] start failed: " + ex.Message);
        }
    }
}

private void SpawnLocked()
{
    var pyPath = _cfg.LauncherScriptRelative.Replace('/', Path.DirectorySeparatorChar);
    var jsPath = _cfg.FridaJsRelative.Replace('/', Path.DirectorySeparatorChar);
    var args = "\"" + pyPath + "\" --script \"" + jsPath + "\"";
    _proc = _host.Spawn(_cfg.PythonExecutable, args, AppContext.BaseDirectory);
    _stopRequested = false;
    _fatalReceived = false;
    StartPumps();
    _log("[LimitBypass] starting python launcher");
}

private void StartPumps()
{
    _stdoutPump = new Thread(() => PumpStdout(_proc))
    {
        IsBackground = true, Name = "LimitBypass-stdout"
    };
    _stderrPump = new Thread(() => PumpStderr(_proc))
    {
        IsBackground = true, Name = "LimitBypass-stderr"
    };
    _stdoutPump.Start();
    _stderrPump.Start();
}

private void PumpStdout(IBypassProcess proc)
{
    try
    {
        string line;
        while ((line = proc.StandardOutput.ReadLine()) != null)
        {
            HandleStdoutLine(line);
        }
    }
    catch { /* 进程死了 readline 抛 OK */ }
}

private void PumpStderr(IBypassProcess proc)
{
    try
    {
        string line;
        while ((line = proc.StandardError.ReadLine()) != null)
        {
            _log("[LimitBypass][stderr] " + line);
        }
    }
    catch { }
}

private void HandleStdoutLine(string line)
{
    _log("[LimitBypass] " + line);
}

public void Stop()
{
    lock (_sync)
    {
        _stopRequested = true;
        TryKillLocked();
        Status = BypassStatus.Stopped;
    }
}

private void TryKillLocked()
{
    if (_proc == null) return;
    try { if (!_proc.HasExited) _proc.Kill(); } catch { }
    try { _proc.Dispose(); } catch { }
    _proc = null;
}

public void Dispose()
{
    lock (_sync)
    {
        if (_disposed) return;
        _disposed = true;
        _stopRequested = true;
        TryKillLocked();
        Status = BypassStatus.Stopped;
    }
}
```

- [ ] **Step 4：跑测试，预期 PASS**

```bash
dotnet test BotCore.Tests/BotCore.Tests.csproj --filter Start_CalledTwice_OnlyOneSpawn --nologo --verbosity minimal
```

Expected: `通过! - 失败: 0, 通过: 1`。

- [ ] **Step 5：commit**

```bash
git add BotMain/HsBoxLimitBypass.cs BotCore.Tests/HsBoxLimitBypassTests.cs
git commit -m "facade: 实现 Start 幂等 + 后台线程泵 stdout/stderr"
```

---

## Task 3：单元测试 — Dispose 关闭子进程

**Files:**
- Modify: `BotCore.Tests/HsBoxLimitBypassTests.cs`

- [ ] **Step 1：在 HsBoxLimitBypassTests 内追加测试**

```csharp
[Fact]
public void Dispose_KillsSubprocess_StatusBecomesStopped()
{
    var host = new FakeHost();
    var bypass = new HsBoxLimitBypass(Logs(out _), host, new BypassConfig());

    bypass.Start();
    Assert.Single(host.Spawned);
    var p = host.Spawned[0];

    bypass.Dispose();

    Assert.True(p.KillCalled);
    Assert.Equal(BypassStatus.Stopped, bypass.Status);
}
```

- [ ] **Step 2：跑测试**

```bash
dotnet test BotCore.Tests/BotCore.Tests.csproj --filter Dispose_KillsSubprocess --nologo --verbosity minimal
```

Expected: PASS（Task 2 已经实现 Dispose）。

- [ ] **Step 3：commit**

```bash
git add BotCore.Tests/HsBoxLimitBypassTests.cs
git commit -m "测试: Dispose 关闭子进程"
```

---

## Task 4：单元测试 — Spawn 失败被吞并 + Status=Failed

**Files:**
- Modify: `BotCore.Tests/HsBoxLimitBypassTests.cs`

- [ ] **Step 1：追加测试**

```csharp
[Fact]
public void Start_SpawnThrows_FacadeDoesNotThrow_StatusFailed()
{
    var host = new FakeHost { NextException = new InvalidOperationException("python not found") };
    var logs = Logs(out var log);
    var bypass = new HsBoxLimitBypass(log, host, new BypassConfig());

    var ex = Record.Exception(() => bypass.Start());

    Assert.Null(ex);
    Assert.Equal(BypassStatus.Failed, bypass.Status);
    Assert.Contains(logs, m => m.Contains("start failed") && m.Contains("python not found"));

    bypass.Dispose();
}
```

- [ ] **Step 2：跑测试**

```bash
dotnet test BotCore.Tests/BotCore.Tests.csproj --filter SpawnThrows --nologo --verbosity minimal
```

Expected: PASS（Task 2 的 try/catch 已经覆盖）。

- [ ] **Step 3：commit**

```bash
git add BotCore.Tests/HsBoxLimitBypassTests.cs
git commit -m "测试: spawn 失败被吞并，Status=Failed"
```

---

## Task 5：实现自动重启 + 限速

**Files:**
- Modify: `BotMain/HsBoxLimitBypass.cs`
- Modify: `BotCore.Tests/HsBoxLimitBypassTests.cs`

- [ ] **Step 1：先写测试**

在 `HsBoxLimitBypassTests` 追加：

```csharp
[Fact]
public void SubprocessExits_AutoRestartUntilLimit()
{
    var host = new FakeHost();
    var bypass = new HsBoxLimitBypass(Logs(out _), host, new BypassConfig { MaxRestartsPerMinute = 3 });

    bypass.Start();
    Assert.Equal(1, host.SpawnCount);

    // 模拟子进程意外退出 4 次（5 秒内）→ 第 4 次不再重启
    for (int i = 0; i < 4; i++)
    {
        host.Spawned[host.Spawned.Count - 1].SimulateExit(1);
        bypass.NotifySubprocessExitedForTest(); // 显式触发重启检查
    }

    Assert.True(host.SpawnCount <= 4, $"spawned {host.SpawnCount} times, expected ≤4 (1 initial + 3 restarts)");
    Assert.Equal(BypassStatus.Failed, bypass.Status);

    bypass.Dispose();
}
```

- [ ] **Step 2：在 HsBoxLimitBypass 内加 `NotifySubprocessExitedForTest` 测试钩子 + 重启逻辑**

把 `PumpStdout` 改为退出后触发重启检查：

```csharp
private void PumpStdout(IBypassProcess proc)
{
    try
    {
        string line;
        while ((line = proc.StandardOutput.ReadLine()) != null)
        {
            HandleStdoutLine(line);
        }
    }
    catch { }
    finally
    {
        // 子进程的 stdout 关闭通常意味着它退出了
        OnSubprocessLikelyExited();
    }
}

private void OnSubprocessLikelyExited()
{
    lock (_sync)
    {
        if (_disposed || _stopRequested) return;
        if (_proc == null || !_proc.HasExited) return;  // 可能是 stdout 关闭但进程还活着，跳过

        var code = _proc.ExitCode;
        try { _proc.Dispose(); } catch { }
        _proc = null;

        if (_fatalReceived)
        {
            _log("[LimitBypass] fatal received, no restart");
            Status = BypassStatus.Failed;
            return;
        }

        if (!CanRestartLocked())
        {
            _log("[LimitBypass] restart limit reached, giving up");
            Status = BypassStatus.Failed;
            return;
        }

        _log($"[LimitBypass] subprocess exited code={code}, restart {_restartHistory.Count}/{_cfg.MaxRestartsPerMinute}");
        try
        {
            SpawnLocked();
            Status = BypassStatus.Running;
        }
        catch (Exception ex)
        {
            _log("[LimitBypass] restart spawn failed: " + ex.Message);
            Status = BypassStatus.Failed;
        }
    }
}

private bool CanRestartLocked()
{
    var cutoff = DateTime.UtcNow.AddSeconds(-60);
    while (_restartHistory.Count > 0 && _restartHistory.Peek() < cutoff)
        _restartHistory.Dequeue();
    if (_restartHistory.Count >= _cfg.MaxRestartsPerMinute)
        return false;
    _restartHistory.Enqueue(DateTime.UtcNow);
    return true;
}

// 测试钩子：让单元测试在 mock 子进程"退出"后显式触发重启检查（绕过后台线程时序）
internal void NotifySubprocessExitedForTest() => OnSubprocessLikelyExited();
```

- [ ] **Step 3：跑测试**

```bash
dotnet test BotCore.Tests/BotCore.Tests.csproj --filter AutoRestartUntilLimit --nologo --verbosity minimal
```

Expected: PASS。

- [ ] **Step 4：跑全部 facade 测试**

```bash
dotnet test BotCore.Tests/BotCore.Tests.csproj --filter HsBoxLimitBypass --nologo --verbosity minimal
```

Expected: 4 个全过（Start_CalledTwice / Dispose_Kills / SpawnThrows / AutoRestartUntilLimit）。

- [ ] **Step 5：commit**

```bash
git add BotMain/HsBoxLimitBypass.cs BotCore.Tests/HsBoxLimitBypassTests.cs
git commit -m "facade: 子进程意外退出自动重启，1分钟限速 N 次"
```

---

## Task 6：单元测试 — 非法 JSON stdout 容错

**Files:**
- Modify: `BotCore.Tests/HsBoxLimitBypassTests.cs`

- [ ] **Step 1：追加测试**

```csharp
[Fact]
public void StdoutNonJsonLine_LoggedAsRaw_NoCrash()
{
    var host = new FakeHost
    {
        NextProcessFactory = () => new FakeProcess(stdout: "this is not json\n{\"tag\":\"ok\"}\n")
    };
    var logs = Logs(out var log);
    var bypass = new HsBoxLimitBypass(log, host, new BypassConfig());

    bypass.Start();
    Thread.Sleep(200);  // 等后台线程消费完 stdout

    bypass.Dispose();

    lock (logs)
    {
        Assert.Contains(logs, m => m.Contains("this is not json"));
        Assert.Contains(logs, m => m.Contains("\"tag\":\"ok\""));
    }
}
```

- [ ] **Step 2：跑测试**

```bash
dotnet test BotCore.Tests/BotCore.Tests.csproj --filter StdoutNonJsonLine --nologo --verbosity minimal
```

Expected: PASS（HandleStdoutLine 当前直接 `_log("[LimitBypass] " + line)`，不解析 JSON 所以非法行也正常 log）。

- [ ] **Step 3：commit**

```bash
git add BotCore.Tests/HsBoxLimitBypassTests.cs
git commit -m "测试: stdout 非 JSON 行原样记日志，不崩"
```

---

## Task 7：fatal 消息停止重启

**Files:**
- Modify: `BotMain/HsBoxLimitBypass.cs`
- Modify: `BotCore.Tests/HsBoxLimitBypassTests.cs`

- [ ] **Step 1：追加测试**

```csharp
[Fact]
public void FatalMessageReceived_StopsAutoRestart()
{
    var host = new FakeHost
    {
        NextProcessFactory = () => new FakeProcess(stdout: "{\"tag\":\"fatal\",\"msg\":\"hook failed\"}\n")
    };
    var bypass = new HsBoxLimitBypass(Logs(out _), host, new BypassConfig { MaxRestartsPerMinute = 5 });

    bypass.Start();
    Thread.Sleep(200);  // 让 stdout pump 处理 fatal
    host.Spawned[0].SimulateExit(2);
    bypass.NotifySubprocessExitedForTest();

    Assert.Equal(1, host.SpawnCount);  // 没有重启
    Assert.Equal(BypassStatus.Failed, bypass.Status);

    bypass.Dispose();
}
```

- [ ] **Step 2：修改 HandleStdoutLine 检测 fatal**

把 `HandleStdoutLine` 替换为：

```csharp
private void HandleStdoutLine(string line)
{
    _log("[LimitBypass] " + line);
    // 简单包含检测，避免引入 JSON 解析依赖
    if (line.Contains("\"tag\":\"fatal\"") || line.Contains("\"tag\": \"fatal\""))
    {
        lock (_sync) { _fatalReceived = true; }
    }
}
```

- [ ] **Step 3：跑测试**

```bash
dotnet test BotCore.Tests/BotCore.Tests.csproj --filter FatalMessage --nologo --verbosity minimal
```

Expected: PASS。

- [ ] **Step 4：跑全部 facade 测试**

```bash
dotnet test BotCore.Tests/BotCore.Tests.csproj --filter HsBoxLimitBypass --nologo --verbosity minimal
```

Expected: 6 个全过。

- [ ] **Step 5：commit**

```bash
git add BotMain/HsBoxLimitBypass.cs BotCore.Tests/HsBoxLimitBypassTests.cs
git commit -m "facade: 收到 fatal 停止自动重启"
```

---

## Task 8：DefaultBypassProcessHost — 真实子进程包装

**Files:**
- Modify: `BotMain/HsBoxLimitBypass.cs`

- [ ] **Step 1：在文件末尾追加默认实现**

```csharp
internal sealed class DefaultBypassProcessHost : IBypassProcessHost
{
    public IBypassProcess Spawn(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,  // 让 Hearthbot 关闭时 pipe EOF 触发 Python 退出
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        var proc = Process.Start(psi);
        if (proc == null) throw new InvalidOperationException("Process.Start returned null");
        return new ProcessAdapter(proc);
    }

    private sealed class ProcessAdapter : IBypassProcess
    {
        private readonly Process _p;
        public ProcessAdapter(Process p) { _p = p; }
        public bool HasExited => _p.HasExited;
        public int ExitCode => _p.HasExited ? _p.ExitCode : -1;
        public StreamReader StandardOutput => _p.StandardOutput;
        public StreamReader StandardError => _p.StandardError;
        public void Kill()
        {
            try { if (!_p.HasExited) _p.Kill(); } catch { }
        }
        public void Dispose() { try { _p.Dispose(); } catch { } }
    }
}
```

- [ ] **Step 2：build 通过**

```bash
dotnet build BotMain/BotMain.csproj --nologo --verbosity minimal
```

Expected: 0 errors。

- [ ] **Step 3：commit**

```bash
git add BotMain/HsBoxLimitBypass.cs
git commit -m "facade: DefaultBypassProcessHost 包装 Process.Start"
```

---

## Task 9：Frida JS hook 脚本

**Files:**
- Create: `tools/active/hsbox_querymatch_hook.js`

- [ ] **Step 1：写脚本**

```javascript
'use strict';

// ============================================================================
// 盒子 queryMatchAvailable 永久 true 补丁
// 用法：python Scripts/queryMatchAvailable_hook.py 自动加载本脚本
//
// 找 HSAng.exe 里 MatchLogicPrivate::queryMatchAvailable 的 export，
// Interceptor.attach + onLeave 把返回的 0 改成 1。
// ============================================================================

// 如果 export 找不到，开发者用 IDA 反编译 HSAng.exe 找到该函数 RVA 填这里
// 例: const MANUAL_RVA = ptr('0x12345');
const MANUAL_RVA = null;

const T0 = Date.now();
function log(tag, payload) {
    const ts = ((Date.now() - T0) / 1000).toFixed(3);
    send({ ts: ts, tag: tag, payload: payload });
}

function locate() {
    const mod = Process.findModuleByName('HSAng.exe');
    if (!mod) return { addr: null, source: 'no-module', symbol: null };

    if (MANUAL_RVA !== null) {
        return { addr: mod.base.add(MANUAL_RVA), source: 'manual-rva', symbol: '<manual>' };
    }

    const exps = mod.enumerateExports();
    for (let i = 0; i < exps.length; i++) {
        if (/queryMatchAvailable/i.test(exps[i].name)) {
            return { addr: exps[i].address, source: 'export', symbol: exps[i].name };
        }
    }
    return { addr: null, source: 'not-found', symbol: null };
}

const target = locate();
if (target.addr === null) {
    log('fatal', { msg: 'queryMatchAvailable not found in HSAng.exe', source: target.source });
} else {
    let hits = 0;
    let forced = 0;
    Interceptor.attach(target.addr, {
        onLeave: function (retval) {
            hits++;
            const orig = retval.toInt32();
            if (orig === 0) {
                retval.replace(1);
                forced++;
            }
            if (hits % 20 === 0) {
                log('patch-stat', { hits: hits, forced: forced });
            }
        }
    });
    log('hook-installed', {
        source: target.source,
        symbol: target.symbol,
        addr: target.addr.toString()
    });
}

log('init', { pid: Process.id, arch: Process.arch });
```

- [ ] **Step 2：Frida 语法静态校验**

```bash
node -e "new Function(require('fs').readFileSync('tools/active/hsbox_querymatch_hook.js','utf8'))" 2>&1
```

Expected: 无输出（成功）。如果有 SyntaxError → 修。
*注：此校验只验语法不真跑（Frida JS 用的 send/Process/Module/Interceptor 是 Frida runtime API，node.js 没有）。*

- [ ] **Step 3：commit**

```bash
git add tools/active/hsbox_querymatch_hook.js
git commit -m "Frida JS: hook queryMatchAvailable 强制返回 true"
```

---

## Task 10：Python 常驻启动器

**Files:**
- Create: `Scripts/queryMatchAvailable_hook.py`

- [ ] **Step 1：写脚本**

```python
"""盒子 queryMatchAvailable hook 常驻启动器。

由 BotMain.HsBoxLimitBypass 通过 Process.Start 拉起。
"""
import argparse
import json
import sys
import threading
import time

import frida


HEARTBEAT_SEC = 30
RECONNECT_BACKOFF_SEC = 5
ATTACH_TIMEOUT_SEC = 300
TARGET_PROCESS = 'HSAng.exe'


class HookSession:
    def __init__(self, script_path):
        self.script_path = script_path
        self.session = None
        self.script = None
        self.hits = 0
        self.forced = 0
        self.fatal = False
        self.detached_event = threading.Event()
        self.lock = threading.Lock()

    def attach_with_retry(self):
        waited = 0.0
        while waited < ATTACH_TIMEOUT_SEC:
            try:
                self.session = frida.attach(TARGET_PROCESS)
                return True
            except frida.ProcessNotFoundError:
                if waited == 0:
                    print(json.dumps({'tag': 'wait-target', 'msg': f'waiting for {TARGET_PROCESS}'}), flush=True)
                time.sleep(0.2)
                waited += 0.2
            except frida.PermissionDeniedError:
                print(json.dumps({'tag': 'fatal', 'msg': 'permission denied (run as admin)'}), flush=True)
                return False
        print(json.dumps({'tag': 'fatal', 'msg': f'attach timeout after {ATTACH_TIMEOUT_SEC}s'}), flush=True)
        return False

    def load_script(self):
        with open(self.script_path, 'r', encoding='utf-8') as f:
            src = f.read()
        self.script = self.session.create_script(src)
        self.script.on('message', self._on_message)
        self.session.on('detached', self._on_detached)
        self.script.load()

    def _on_message(self, message, _data):
        try:
            if message['type'] == 'send':
                payload = message['payload']
                tag = payload.get('tag') if isinstance(payload, dict) else None
                if tag == 'patch-stat' and isinstance(payload.get('payload'), dict):
                    p = payload['payload']
                    with self.lock:
                        self.hits = p.get('hits', self.hits)
                        self.forced = p.get('forced', self.forced)
                if tag == 'fatal':
                    with self.lock:
                        self.fatal = True
                print(json.dumps(payload, ensure_ascii=False), flush=True)
            elif message['type'] == 'error':
                err = {'tag': 'frida-error',
                       'desc': message.get('description', ''),
                       'stack': message.get('stack', '')}
                print(json.dumps(err, ensure_ascii=False), flush=True)
        except Exception as ex:
            print(json.dumps({'tag': 'py-on-message-error', 'err': str(ex)}), flush=True)

    def _on_detached(self, reason, _crash):
        print(json.dumps({'tag': 'detached', 'reason': str(reason)}), flush=True)
        self.detached_event.set()

    def heartbeat_loop(self, stop_event):
        while not stop_event.wait(HEARTBEAT_SEC):
            with self.lock:
                payload = {'tag': 'heartbeat', 'hits': self.hits, 'forced': self.forced}
            print(json.dumps(payload), flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--script', default='tools/active/hsbox_querymatch_hook.js')
    args = ap.parse_args()

    stop_event = threading.Event()
    # 主循环：attach → load → 等 detach → 重连
    while not stop_event.is_set():
        sess = HookSession(args.script)
        if not sess.attach_with_retry():
            return 2
        try:
            sess.load_script()
        except Exception as ex:
            print(json.dumps({'tag': 'fatal', 'msg': f'load script failed: {ex}'}), flush=True)
            return 3

        # 心跳线程
        hb_stop = threading.Event()
        hb_thread = threading.Thread(target=sess.heartbeat_loop, args=(hb_stop,), daemon=True)
        hb_thread.start()

        # 等 detach 或 fatal
        while not sess.detached_event.is_set():
            with sess.lock:
                fatal = sess.fatal
            if fatal:
                print(json.dumps({'tag': 'exit', 'reason': 'fatal-from-js'}), flush=True)
                hb_stop.set()
                return 2
            # 检测 stdin EOF（父进程关闭 pipe）
            if sys.stdin.closed:
                print(json.dumps({'tag': 'exit', 'reason': 'stdin-closed'}), flush=True)
                hb_stop.set()
                return 0
            time.sleep(0.5)

        hb_stop.set()
        hb_thread.join(timeout=1)

        try:
            sess.session.detach()
        except Exception:
            pass

        print(json.dumps({'tag': 'reconnecting', 'after_sec': RECONNECT_BACKOFF_SEC}), flush=True)
        time.sleep(RECONNECT_BACKOFF_SEC)

    return 0


if __name__ == '__main__':
    sys.exit(main())
```

- [ ] **Step 2：语法校验**

```bash
python -c "import ast; ast.parse(open('Scripts/queryMatchAvailable_hook.py', encoding='utf-8').read()); print('parse-ok')"
```

Expected: `parse-ok`。

- [ ] **Step 3：commit**

```bash
git add Scripts/queryMatchAvailable_hook.py
git commit -m "Python: queryMatchAvailable hook 常驻启动器（attach+retry+detach自动重连）"
```

---

## Task 11：BotService 集成

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1：找到 BotService 字段声明区域，加 facade 字段**

定位 `private int _modeIndex;` 附近（约 L131）。在 `_modeIndex` 字段下方追加：

```csharp
private readonly HsBoxLimitBypass _hsBoxLimitBypass;
```

- [ ] **Step 2：找到 BotService 构造函数，初始化 facade**

定位 BotService 的构造函数末尾（搜 `public BotService(` 找入口，找匹配的 `}`）。在 `Log(` 调用相关初始化代码段中插入：

```csharp
_hsBoxLimitBypass = new HsBoxLimitBypass(
    msg => Log(msg),
    new DefaultBypassProcessHost(),
    new BypassConfig());
```

如果构造函数已经有日志相关 init，放在它之后即可。**重要**：facade 不在构造里 Start，只 new；Start 留到 BotService.Start() 末尾。

- [ ] **Step 3：在 BotService.Start() 末尾启动 facade**

定位 `public void Start()` 方法（grep `public void Start\(`）。在方法返回前加：

```csharp
try
{
    _hsBoxLimitBypass.Start();
}
catch (Exception ex)
{
    Log("[LimitBypass] start exception: " + ex.Message);
}
```

- [ ] **Step 4：在 BotService.Dispose() 开头停止 facade**

定位 `public void Dispose()` 或 `protected virtual void Dispose(bool` 方法。在最开头（释放其他资源前）加：

```csharp
try { _hsBoxLimitBypass?.Dispose(); } catch { }
```

- [ ] **Step 5：build 通过**

```bash
dotnet build BotMain/BotMain.csproj --nologo --verbosity minimal
```

Expected: 0 errors。

- [ ] **Step 6：跑全部 facade 测试 + 全量回归**

```bash
dotnet test BotCore.Tests/BotCore.Tests.csproj --filter HsBoxLimitBypass --nologo --verbosity minimal
```

Expected: 6 个测试全过。

```bash
dotnet test BotCore.Tests/BotCore.Tests.csproj --nologo --verbosity minimal 2>&1 | tail -3
```

Expected: 总数 471（之前 465 + 我们新加 6）。失败 2 个仍然是 unrelated face attack 测试（侦察 plan 期间发现的、与本次无关）。

- [ ] **Step 7：commit**

```bash
git add BotMain/BotService.cs
git commit -m "集成: BotService 启停时拉起/释放 HsBoxLimitBypass"
```

---

## Task 12：实机回归（人工，4 场景）

> **本任务需要用户实机配合**。每完成一个场景给主对话一句话反馈即可。

**前置**：构建并启动 Hearthbot；盒子已运行；HS 已登录。

- [ ] **场景 1：狂野对局**

进入狂野模式匹配并开局，看盒子推荐是否正常（应该正常出推荐）。
看 Hearthbot 日志含：
```
[LimitBypass] starting python launcher
[LimitBypass] {"tag": "init", ...}
[LimitBypass] {"tag": "hook-installed", "source": "export", ...}
```

如果看到 `"tag": "fatal"`，说明 export 没找到 → 转 Task 12.5（IDA 找 RVA）。

- [ ] **场景 2：标传非传说（钻石/铂金/任意非传说）**

切到标准模式匹配非传说档对局。盒子应正常出推荐。
看 Hearthbot 日志：每 30 秒出现 `"tag": "heartbeat"` 行（说明 facade + Python 健康）。

- [ ] **场景 3：标传传说受限档（核心验收）**

切到标准模式匹配传说档（你账号当前 49000 名应该受限）对局。
**期望**：盒子出推荐（不是显示"AI 推荐功能实行阶梯开放"）。
看日志：至少有一条 `"tag": "patch-stat", "forced": >0`（说明 hook 命中且改了 retval）。

如果**仍受限**：
- 看日志 forced 是否 > 0：
  - forced > 0 但仍受限 → onLeave 改 retval 不够，需要升级到 `Interceptor.replace` 或更深 hook
  - forced == 0 → hook 没被触发，可能 export 名匹配错 / 或者当前进的对局压根没调 queryMatchAvailable

- [ ] **场景 4：退出 Hearthbot**

正常关闭 Hearthbot。
看：
- HSAng.exe 仍在运行（没被弄崩）
- Python 子进程已退出（任务管理器看不到 python.exe 残留）
- Hearthbot 日志结尾出现：`[LimitBypass] {"tag": "exit", "reason": "stdin-closed"}` 之类

- [ ] **场景全过 → 进 Task 13**

如果某场景失败：停下来反馈给我，按"放弃条件"路径迭代（先尝试 Interceptor.replace，再考虑改 hook 输入参数）。

---

## Task 12.5（条件）：export 找不到 → IDA 离线找 RVA

> **仅在 Task 12 场景 1 看到 `"tag": "fatal"` 时执行**。否则跳过。

- [ ] **Step 1：用 IDA Free 或 Ghidra 反编译 HSAng.exe**

打开 `F:\炉石传说盒子\HSAng.exe`，等待分析完成。

- [ ] **Step 2：通过字符串 "MatchLogicPrivate::queryMatchAvailable" 反查**

在 IDA 的 Strings 窗口搜该字符串。双击进入字符串引用：xref 通常指向写日志的函数。沿调用图上溯一层就是 `queryMatchAvailable`。

- [ ] **Step 3：拿到函数 RVA**

在 IDA 函数视图记下 RVA（函数地址 - 模块基址）。

- [ ] **Step 4：填到 Frida JS 脚本**

修改 `tools/active/hsbox_querymatch_hook.js`：

```javascript
const MANUAL_RVA = ptr('0xXXXXX');  // 填入实际 RVA
```

- [ ] **Step 5：重新跑场景 1 验证**

回到 Task 12 重跑全部场景。

- [ ] **Step 6：commit**

```bash
git add tools/active/hsbox_querymatch_hook.js
git commit -m "Frida JS: 填入实测 RVA 兜底定位 queryMatchAvailable"
```

---

## Task 13：最终清理 + push

- [ ] **Step 1：确认 git 状态干净**

```bash
git status
```

预期：working tree clean（除会话开始就有的旧 spoofer 删除等无关项）。

- [ ] **Step 2：浏览本次所有 commit**

```bash
git log --oneline origin/main..HEAD
```

应看到一串 `facade:` / `Frida JS:` / `Python:` / `集成:` / `测试:` 等前缀的提交。

- [ ] **Step 3：push**

```bash
git push
```

---

## 完成条件

- [ ] 6 个单元测试全过
- [ ] 4 个实机回归场景全过
- [ ] 阶段 1 spec 里的"成功定义"全部满足：
  - 标传传说受限档实机：盒子出推荐
  - 单元测试全过
  - 切回其他模式 bypass 不影响
  - Hearthbot 退出后 HSAng.exe 状态干净
- [ ] 所有提交已 push 到 origin/main

## 失败时的迭代路径

| 失败点 | 行动 |
|--------|------|
| Task 9 export 找不到 | 走 Task 12.5 IDA 找 RVA |
| Task 12 场景 3 forced > 0 但仍受限 | 升级到 `Interceptor.replace` 全函数替换 |
| Task 12 场景 3 forced == 0 | 检查 export 名匹配规则、确认对局类型真的触发判定 |
| Task 12 场景 4 HSAng.exe 崩 | 立即从 BotService 拔掉 `_hsBoxLimitBypass.Start()`（三级回退第 3 级），定位 hook 副作用 |

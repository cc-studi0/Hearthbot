# 战网实例约束的炉石自动重启 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 自动重启炉石时只允许使用当前会话绑定的战网实例启动游戏；没有绑定、PID 失效或战网启动失败时明确停机，并在中控模式下停止整个队列。

**Architecture:** 先抽出一层可测试的战网重启状态模型，把“未绑定 PID / PID 已失效 / 启动结果”变成结构化结果；然后让 `BattleNetWindowManager` 返回详细启动结果，`BotService` 只通过绑定的战网实例执行匹配超时与闪退重启，并在失败时发出致命错误事件。`AccountController` 负责把当前账号的战网 PID 注入给 `BotService` 并在自动重启失败时停止队列；`MainViewModel` 负责清理单号模式下的残留绑定，并同步移除已经失效的 `Hearthstone.exe` 设置入口。

**Tech Stack:** C# / .NET 8 / WPF / xUnit / `System.Diagnostics.Process` / `System.Threading.Tasks`

**Spec:** `docs/superpowers/specs/2026-03-28-battlenet-restart-design.md`

---

## 文件变更清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `BotMain/BattleNetRestartState.cs` | 新建 | 放战网重启绑定、失败类型、启动结果、绑定校验 helper，供 `BotService` 和测试复用 |
| `BotMain/BattleNetWindowManager.cs` | 修改 | 把“通过战网实例启动炉石”升级为返回结构化结果，保留现有 `bool` 包装层兼容中控代码 |
| `BotMain/BotService.cs` | 修改 | 保存/清空战网绑定；新增自动重启失败事件；把匹配超时/闪退重启全部切到战网链路；去掉直启 `Hearthstone.exe` 的重启实现；保留失败态直到线程退出 |
| `BotMain/AccountController.cs` | 修改 | 切号时注入战网 PID；失败或停止时清理绑定；自动重启失败时停止整个队列并标记当前账号失败 |
| `BotMain/MainViewModel.cs` | 修改 | 普通单号启动前清空残留战网绑定；移除失效的 `Hearthstone.exe` 设置胶水代码 |
| `BotMain/SettingsWindow.xaml` | 修改 | 删除 `Hearthstone.exe` 路径输入，避免 UI 继续暗示“可直启游戏” |
| `BotCore.Tests/BotCore.Tests.csproj` | 修改 | 链接新的 `BotMain/BattleNetRestartState.cs` 到测试工程 |
| `BotCore.Tests/BattleNetRestartStateTests.cs` | 新建 | 覆盖绑定缺失、PID 失效、合法绑定三种判定结果 |
| `BotCore.Tests/BotServiceRestartStatusTests.cs` | 新建 | 覆盖 `BotService` 退出时失败状态优先级，防止 `Ready/Waiting Payload` 覆盖失败态 |

## 实施前说明

- `BotService` 当前的直启路径只被自动重启使用：`ResolveHearthstoneLaunchPath(...)`、`TryLaunchHearthstone(...)` 以及 `_hearthstoneExecutablePathOverride` / `_lastKnownHearthstoneExecutablePath` 只要新链路接上，就应一起删除，避免死代码继续误导。
- `BotCore.Tests` 目前显式链接 `BotMain` 文件，不会自动包含新建文件；每增加一个新的 `BotMain/*.cs` helper，都必须同步更新 `BotCore.Tests/BotCore.Tests.csproj`。
- 普通单号模式和中控模式共用同一个 `BotService` 实例，因此必须显式清理残留的战网绑定；不能依赖“新一轮启动会自然覆盖旧值”。

---

### Task 1: 抽取战网重启状态模型并补齐单测

**Files:**
- Create: `BotMain/BattleNetRestartState.cs`
- Create: `BotCore.Tests/BattleNetRestartStateTests.cs`
- Modify: `BotCore.Tests/BotCore.Tests.csproj`

- [ ] **Step 1: 先写失败测试**

  新建 `BotCore.Tests/BattleNetRestartStateTests.cs`，先写 3 个测试，覆盖缺失绑定、PID 已退出、合法绑定：

  ```csharp
  using BotMain;
  using Xunit;

  namespace BotCore.Tests
  {
      public class BattleNetRestartStateTests
      {
          [Fact]
          public void BindingValidator_ReturnsMissingBinding_WhenProcessIdIsNull()
          {
              var binding = new BattleNetRestartBinding(null, string.Empty);

              var result = BattleNetRestartBindingValidator.Validate(binding, _ => true);

              Assert.False(result.Success);
              Assert.Equal(BattleNetRestartFailureKind.MissingBinding, result.FailureKind);
              Assert.Contains("未绑定战网实例", result.Message);
          }

          [Fact]
          public void BindingValidator_ReturnsProcessExited_WhenBattleNetProcessIsGone()
          {
              var binding = new BattleNetRestartBinding(1234, "Battle.net");

              var result = BattleNetRestartBindingValidator.Validate(binding, _ => false);

              Assert.False(result.Success);
              Assert.Equal(BattleNetRestartFailureKind.ProcessExited, result.FailureKind);
              Assert.Contains("PID=1234", result.Message);
          }

          [Fact]
          public void BindingValidator_ReturnsSuccess_WhenBoundBattleNetProcessIsAlive()
          {
              var binding = new BattleNetRestartBinding(5678, "账号A");

              var result = BattleNetRestartBindingValidator.Validate(binding, _ => true);

              Assert.True(result.Success);
              Assert.Equal(5678, result.BattleNetProcessId);
              Assert.Equal(BattleNetRestartFailureKind.None, result.FailureKind);
          }
      }
  }
  ```

- [ ] **Step 2: 把新 helper 文件链接进测试工程并运行失败测试**

  先在 `BotCore.Tests/BotCore.Tests.csproj` 追加链接项：

  ```xml
  <Compile Include="..\BotMain\BattleNetRestartState.cs" Link="BattleNetRestartState.cs" />
  ```

  然后运行：

  ```bash
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~BattleNetRestartStateTests" -v minimal
  ```

  预期：编译失败，提示 `BattleNetRestartBinding` / `BattleNetRestartBindingValidator` / `BattleNetRestartFailureKind` 不存在。

- [ ] **Step 3: 实现 `BotMain/BattleNetRestartState.cs`**

  在 `BotMain/BattleNetRestartState.cs` 中加入可测试的状态模型：

  ```csharp
  namespace BotMain
  {
      internal enum BattleNetRestartFailureKind
      {
          None,
          MissingBinding,
          ProcessExited,
          WindowNotFound,
          BringToFrontFailed,
          WindowRectFailed,
          LaunchTimedOut,
          Cancelled
      }

      internal readonly struct BattleNetRestartBinding
      {
          public BattleNetRestartBinding(int? processId, string windowTitle)
          {
              ProcessId = processId;
              WindowTitle = string.IsNullOrWhiteSpace(windowTitle) ? string.Empty : windowTitle;
          }

          public int? ProcessId { get; }
          public string WindowTitle { get; }
      }

      internal readonly struct BattleNetLaunchResult
      {
          public BattleNetLaunchResult(
              bool success,
              BattleNetRestartFailureKind failureKind,
              string message,
              int? battleNetProcessId = null,
              int? hearthstoneProcessId = null)
          {
              Success = success;
              FailureKind = failureKind;
              Message = message ?? string.Empty;
              BattleNetProcessId = battleNetProcessId;
              HearthstoneProcessId = hearthstoneProcessId;
          }

          public bool Success { get; }
          public BattleNetRestartFailureKind FailureKind { get; }
          public string Message { get; }
          public int? BattleNetProcessId { get; }
          public int? HearthstoneProcessId { get; }

          public static BattleNetLaunchResult Succeeded(int battleNetProcessId, int hearthstoneProcessId, string message) =>
              new(true, BattleNetRestartFailureKind.None, message, battleNetProcessId, hearthstoneProcessId);

          public static BattleNetLaunchResult Failed(BattleNetRestartFailureKind kind, string message, int? battleNetProcessId = null) =>
              new(false, kind, message, battleNetProcessId, null);
      }

      internal static class BattleNetRestartBindingValidator
      {
          public static BattleNetLaunchResult Validate(BattleNetRestartBinding binding, Func<int, bool> isProcessAlive)
          {
              if (!binding.ProcessId.HasValue || binding.ProcessId.Value <= 0)
                  return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.MissingBinding, "未绑定战网实例");

              if (isProcessAlive == null || !isProcessAlive(binding.ProcessId.Value))
                  return BattleNetLaunchResult.Failed(
                      BattleNetRestartFailureKind.ProcessExited,
                      $"战网实例已退出 PID={binding.ProcessId.Value}",
                      binding.ProcessId.Value);

              return new BattleNetLaunchResult(
                  success: true,
                  failureKind: BattleNetRestartFailureKind.None,
                  message: $"战网实例可用 PID={binding.ProcessId.Value}",
                  battleNetProcessId: binding.ProcessId.Value);
          }
      }
  }
  ```

- [ ] **Step 4: 运行测试确认通过**

  ```bash
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~BattleNetRestartStateTests" -v minimal
  ```

  预期：3 个测试全部 `Passed`。

- [ ] **Step 5: 提交**

  ```bash
  git add BotMain/BattleNetRestartState.cs BotCore.Tests/BotCore.Tests.csproj BotCore.Tests/BattleNetRestartStateTests.cs
  git commit -m "新增战网重启状态模型与绑定校验测试"
  ```

---

### Task 2: 升级 `BattleNetWindowManager` 为结构化启动结果

**Files:**
- Modify: `BotMain/BattleNetWindowManager.cs`

- [ ] **Step 1: 新增详细结果方法并保留旧 `bool` 包装层**

  在 `BotMain/BattleNetWindowManager.cs` 中，把现有：

  ```csharp
  public static async Task<bool> LaunchHearthstoneFrom(...)
  ```

  改成“详细结果 + 兼容包装”两层：

  ```csharp
  public static async Task<BattleNetLaunchResult> LaunchHearthstoneFromDetailed(
      int processId,
      Action<string> log,
      CancellationToken ct,
      int timeoutSeconds = 90)
  {
      var hWnd = FindWindowByPid(processId);
      if (hWnd == IntPtr.Zero)
      {
          var message = $"未找到PID={processId}的战网窗口";
          log?.Invoke($"[Restart] 自动重启失败：{message}");
          return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.WindowNotFound, message, processId);
      }

      if (!BringWindowToFront(hWnd))
      {
          var message = $"无法前置PID={processId}的战网窗口";
          log?.Invoke($"[Restart] 自动重启失败：{message}");
          return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.BringToFrontFailed, message, processId);
      }

      await Task.Delay(600, ct);

      if (!GetWindowRect(hWnd, out var rect))
      {
          var message = $"获取PID={processId}的战网窗口位置失败";
          log?.Invoke($"[Restart] 自动重启失败：{message}");
          return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.WindowRectFailed, message, processId);
      }

      // 原有点击逻辑保留
      // ...

      while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
      {
          var hsProcs = Process.GetProcessesByName("Hearthstone");
          if (hsProcs.Length > 0)
          {
              var hsPid = hsProcs[0].Id;
              var message = $"炉石进程已启动 PID={hsPid}";
              log?.Invoke($"[Restart] {message}");
              return BattleNetLaunchResult.Succeeded(processId, hsPid, message);
          }

          await Task.Delay(2000, ct);
      }

      if (ct.IsCancellationRequested)
          return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.Cancelled, $"启动取消 PID={processId}", processId);

      var timeoutMessage = $"从战网启动炉石超时 PID={processId}";
      log?.Invoke($"[Restart] 自动重启失败：{timeoutMessage}");
      return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.LaunchTimedOut, timeoutMessage, processId);
  }

  public static async Task<bool> LaunchHearthstoneFrom(
      int processId,
      Action<string> log,
      CancellationToken ct,
      int timeoutSeconds = 90)
  {
      var result = await LaunchHearthstoneFromDetailed(processId, log, ct, timeoutSeconds);
      return result.Success;
  }
  ```

  要点：

  - `AccountController` 旧调用点暂时还能继续跑。
  - 新方法的失败信息必须足够具体，后续 `BotService` 和中控会直接复用。

- [ ] **Step 2: 构建 `BotMain` 确认没有签名错误**

  ```bash
  dotnet build BotMain/BotMain.csproj -c Debug
  ```

  预期：`Build succeeded.`，此时即使还没接入 `BotService`，中控现有调用也仍然能编译通过。

- [ ] **Step 3: 提交**

  ```bash
  git add BotMain/BattleNetWindowManager.cs
  git commit -m "战网窗口管理器返回结构化启动结果"
  ```

---

### Task 3: 让 `BotService` 的自动重启只走战网链路，并保留失败态

**Files:**
- Modify: `BotMain/BotService.cs`
- Create: `BotCore.Tests/BotServiceRestartStatusTests.cs`

- [ ] **Step 1: 先写 `ResolveStopStatus` 的失败测试**

  新建 `BotCore.Tests/BotServiceRestartStatusTests.cs`，用反射覆盖 `BotService` 的私有静态 helper：

  ```csharp
  using System.Reflection;
  using BotMain;
  using Xunit;

  namespace BotCore.Tests
  {
      public class BotServiceRestartStatusTests
      {
          [Theory]
          [InlineData(true, null, "Ready")]
          [InlineData(false, null, "Waiting Payload")]
          [InlineData(true, "自动重启失败：未绑定战网实例", "自动重启失败：未绑定战网实例")]
          public void ResolveStopStatus_PrefersTerminalOverride_WhenProvided(
              bool prepared,
              string overrideStatus,
              string expected)
          {
              var method = typeof(BotService).GetMethod(
                  "ResolveStopStatus",
                  BindingFlags.NonPublic | BindingFlags.Static);

              Assert.NotNull(method);
              var actual = Assert.IsType<string>(method.Invoke(null, new object[] { prepared, overrideStatus }));
              Assert.Equal(expected, actual);
          }
      }
  }
  ```

- [ ] **Step 2: 运行失败测试，确认 helper 还不存在**

  ```bash
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~BotServiceRestartStatusTests" -v minimal
  ```

  预期：失败，`ResolveStopStatus` 找不到。

- [ ] **Step 3: 在 `BotService.cs` 增加战网绑定字段、失败事件和状态收口 helper**

  在 `BotMain/BotService.cs` 中完成以下改造：

  1. 在字段区新增：

  ```csharp
  private BattleNetRestartBinding _battleNetRestartBinding;
  private string _terminalStatusOverride;
  public event Action<string> OnRestartFailed;
  ```

  2. 新增运行时配置方法：

  ```csharp
  public void SetBattleNetRestartBinding(int? processId, string windowTitle = null)
  {
      _battleNetRestartBinding = new BattleNetRestartBinding(processId, windowTitle);
      Log(processId.HasValue
          ? $"[Restart] 已绑定战网实例 PID={processId}"
          : "[Restart] 已清空战网实例绑定");
  }

  public void ClearBattleNetRestartBinding()
  {
      _battleNetRestartBinding = default;
      Log("[Restart] 已清空战网实例绑定");
  }
  ```

  3. 新增停止状态 helper，并在 `DoStartRun()` 的 `finally` 里改用它：

  ```csharp
  private static string ResolveStopStatus(bool prepared, string terminalStatusOverride)
  {
      return string.IsNullOrWhiteSpace(terminalStatusOverride)
          ? (prepared ? "Ready" : "Waiting Payload")
          : terminalStatusOverride;
  }
  ```

  把原来的：

  ```csharp
  StatusChanged(_prepared ? "Ready" : "Waiting Payload");
  ```

  替换为：

  ```csharp
  StatusChanged(ResolveStopStatus(_prepared, _terminalStatusOverride));
  _terminalStatusOverride = null;
  ```

  4. 新增统一失败收口：

  ```csharp
  private void FailRestartAndStop(string reason)
  {
      var status = $"自动重启失败：{reason}";
      _restartPending = false;
      _terminalStatusOverride = status;
      Log($"[Restart] {status}");
      StatusChanged(status);
      try { OnRestartFailed?.Invoke(reason); } catch { }
      Stop();
  }
  ```

  5. 新增一个只走绑定战网实例的 helper，供 `TryReconnectLoop()` 和 `RestartHearthstone()` 共用：

  ```csharp
  private BattleNetLaunchResult LaunchFromBoundBattleNet(string reason)
  {
      var bindingResult = BattleNetRestartBindingValidator.Validate(
          _battleNetRestartBinding,
          BattleNetWindowManager.IsProcessAlive);
      if (!bindingResult.Success)
          return bindingResult;

      Log($"[Restart] {reason}: 使用战网实例 PID={bindingResult.BattleNetProcessId} 启动炉石");
      return BattleNetWindowManager
          .LaunchHearthstoneFromDetailed(
              bindingResult.BattleNetProcessId.Value,
              Log,
              _cts?.Token ?? CancellationToken.None)
          .GetAwaiter()
          .GetResult();
  }
  ```

- [ ] **Step 4: 替换重启主链路并删除旧的直启代码**

  继续修改 `BotMain/BotService.cs`：

  1. 把 `TryReconnectLoop()` 中“进程已消失后自动启动”的部分替换为：

  ```csharp
  if (!hearthstoneAlive)
  {
      var launchResult = LaunchFromBoundBattleNet(reason);
      if (!launchResult.Success)
      {
          FailRestartAndStop(launchResult.Message);
          return false;
      }

      Log($"[Restart] {reason}: 已通过战网实例 PID={launchResult.BattleNetProcessId} 启动炉石，等待连接...");
  }
  else
  {
      Log($"[Restart] {reason}: 炉石进程仍在，等待重新连接...");
  }
  ```

  2. 把 `RestartHearthstone()` 结尾的：

  ```csharp
  if (TryLaunchHearthstone(launchPath))
      Log("[Restart] 已重置连接状态并重新拉起炉石，等待重新连接...");
  else
      Log("[Restart] 已重置连接状态，但未能自动拉起炉石，等待外部启动后重新连接...");
  ```

  替换为：

  ```csharp
  var launchResult = LaunchFromBoundBattleNet("匹配超时重启");
  if (!launchResult.Success)
  {
      FailRestartAndStop(launchResult.Message);
      return;
  }

  Log($"[Restart] 已通过战网实例 PID={launchResult.BattleNetProcessId} 拉起炉石，等待重新连接...");
  ```

  3. 删除以下不再使用的字段和方法：

  - `_hearthstoneExecutablePathOverride`
  - `_lastKnownHearthstoneExecutablePath`
  - `SetHearthstoneExecutablePath(...)`
  - `ResolveHearthstoneLaunchPath(...)`
  - `TryLaunchHearthstone(...)`

  4. `Start()` 开头补一行，确保上一轮失败态不会污染新运行：

  ```csharp
  _terminalStatusOverride = null;
  ```

- [ ] **Step 5: 跑测试与构建**

  先跑新增状态测试：

  ```bash
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~BotServiceRestartStatusTests" -v minimal
  ```

  再跑两个相关测试文件：

  ```bash
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~BattleNetRestartStateTests|FullyQualifiedName~BotServiceRestartStatusTests" -v minimal
  ```

  最后编译主项目：

  ```bash
  dotnet build BotMain/BotMain.csproj -c Debug
  ```

  预期：测试通过，`BotMain` 编译通过。

- [ ] **Step 6: 提交**

  ```bash
  git add BotMain/BotService.cs BotCore.Tests/BotServiceRestartStatusTests.cs
  git commit -m "BotService 自动重启改为仅使用绑定战网实例"
  ```

---

### Task 4: 让中控注入/清理战网绑定，并在自动重启失败时停整个队列

**Files:**
- Modify: `BotMain/AccountController.cs`

- [ ] **Step 1: 在构造函数里订阅 `BotService.OnRestartFailed`**

  在 `AccountController(BotService bot, Action<string> log)` 构造函数中追加：

  ```csharp
  _bot.OnRestartFailed += OnBotRestartFailed;
  ```

  然后在类里新增私有处理方法：

  ```csharp
  private void OnBotRestartFailed(string reason)
  {
      if (!IsRunning)
          return;

      if (CurrentAccount != null)
          CurrentAccount.Status = AccountStatus.Failed;

      StatusText = $"自动重启失败：{reason}";
      _log?.Invoke($"[中控] 因自动重启失败停止队列: {reason}");
      StopQueue();
  }
  ```

  这里不要继续 `SwitchToNextAccount()`，这次需求明确要求“整队停止”。

- [ ] **Step 2: 在切号流程里清空旧绑定，并在启动前注入新绑定**

  修改 `SwitchToAccount(AccountEntry account)`：

  1. 在方法一开始、停止当前 bot 之前先清理旧绑定：

  ```csharp
  _bot.ClearBattleNetRestartBinding();
  ```

  2. 在 `CurrentAccount = account;` / `account.Status = AccountStatus.Running;` 之后、`_bot.Start();` 之前，注入当前账号的战网信息：

  ```csharp
  _bot.SetBattleNetRestartBinding(account.BattleNetProcessId, account.BattleNetWindowTitle);
  ```

  目标是只让“真正已经切到当前账号的会话”持有该账号的战网 PID。

- [ ] **Step 3: 在队列停止和全部完成时清理残留绑定**

  修改以下两个位置：

  1. `StopQueue()` 中追加：

  ```csharp
  _bot.ClearBattleNetRestartBinding();
  ```

  2. `SwitchToNextAccount()` 里 `next == null` 的“全部完成”分支中追加：

  ```csharp
  _bot.ClearBattleNetRestartBinding();
  CurrentAccount = null;
  ```

  这样中控退出后不会把上一个账号的 PID 留给后面的单号模式。

- [ ] **Step 4: 编译验证**

  ```bash
  dotnet build BotMain/BotMain.csproj -c Debug
  ```

  预期：`AccountController` 编译通过，没有事件签名或空引用错误。

- [ ] **Step 5: 提交**

  ```bash
  git add BotMain/AccountController.cs
  git commit -m "中控接管自动重启失败并停止队列"
  ```

---

### Task 5: 清理单号模式残留绑定，并移除失效的 `Hearthstone.exe` 设置

**Files:**
- Modify: `BotMain/MainViewModel.cs`
- Modify: `BotMain/SettingsWindow.xaml`

- [ ] **Step 1: 让普通单号启动前显式清空战网绑定**

  修改 `MainViewModel.OnMainButton()` 的启动分支，在 `_bot.Start();` 之前追加：

  ```csharp
  _bot.ClearBattleNetRestartBinding();
  ```

  放置位置如下：

  ```csharp
  _startTime = DateTime.Now;
  _timer.Start();
  _bot.ClearBattleNetRestartBinding();
  _bot.Start();
  ```

  这样普通单号模式一定不会复用之前中控留下的战网 PID。

- [ ] **Step 2: 从 `MainViewModel` 删除 `HearthstoneExecutablePath` 胶水代码**

  删除以下内容：

  - 字段：`_hearthstoneExecutablePath`
  - 属性：`HearthstoneExecutablePath`
  - 命令：`BrowseHearthstonePathCmd`
  - 方法：`BrowseHearthstonePath()`
  - `SaveSettings()` 中写入 `HearthstoneExecutablePath`
  - `LoadSettings()` 中读取 `HearthstoneExecutablePath`
  - 启动时调用 `_bot.SetHearthstoneExecutablePath(HearthstoneExecutablePath)`

  同时在 `SaveSettings()` 中主动删除旧键，避免用户本地配置文件继续残留这项无效设置：

  ```csharp
  dict.Remove("HearthstoneExecutablePath");
  ```

- [ ] **Step 3: 从设置窗口删除 `Hearthstone.exe` 配置行**

  在 `BotMain/SettingsWindow.xaml` 中删除这整段：

  ```xml
  <TextBlock Text="Hearthstone.exe:" FontSize="11" Margin="0,0,0,2"/>
  <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
      <TextBox Width="310" Height="20"
               Text="{Binding HearthstoneExecutablePath, UpdateSourceTrigger=PropertyChanged}"
               FontSize="11" VerticalContentAlignment="Center"
               ToolTip="Leave empty to auto-use the current Hearthstone path"/>
      <Button Content="..." Width="24" Height="20" Margin="3,0,0,0"
              Command="{Binding BrowseHearthstonePathCmd}"
              ToolTip="选择 Hearthstone.exe"/>
  </StackPanel>
  ```

  保留 `HSBox (盒子)` 路径配置不变。

- [ ] **Step 4: 全量构建**

  ```bash
  dotnet build 炉石脚本.sln -c Debug
  ```

  预期：WPF 项目和测试工程都编译通过；不会再有 `HearthstoneExecutablePath` / `BrowseHearthstonePathCmd` 的编译引用。

- [ ] **Step 5: 提交**

  ```bash
  git add BotMain/MainViewModel.cs BotMain/SettingsWindow.xaml
  git commit -m "移除失效的炉石路径设置并清理残留战网绑定"
  ```

---

### Task 6: 全量验证并完成交付

**Files:**
- Modify: `BotMain/BattleNetWindowManager.cs`
- Modify: `BotMain/BotService.cs`
- Modify: `BotMain/AccountController.cs`
- Modify: `BotMain/MainViewModel.cs`
- Modify: `BotMain/SettingsWindow.xaml`
- Create: `BotMain/BattleNetRestartState.cs`
- Modify: `BotCore.Tests/BotCore.Tests.csproj`
- Create: `BotCore.Tests/BattleNetRestartStateTests.cs`
- Create: `BotCore.Tests/BotServiceRestartStatusTests.cs`

- [ ] **Step 1: 跑全量单元测试**

  ```bash
  dotnet test BotCore.Tests/BotCore.Tests.csproj -c Debug
  ```

  预期：全部测试通过。

- [ ] **Step 2: 运行 5 个手工回归场景**

  按 spec 执行：

  1. 匹配超时重启，绑定有效战网 PID
  2. 对局中手动杀掉 `Hearthstone.exe`，绑定有效战网 PID
  3. 普通单号模式未绑定战网 PID，触发自动重启
  4. 中控模式下绑定的战网 PID 已退出
  5. 战网窗口存在但 90 秒内没有拉起 `Hearthstone.exe`

  逐项记录日志与 UI 结果，确认：

  - 成功场景会从战网启动并回到 `Running`
  - 失败场景会显示 `自动重启失败：...`
  - 中控失败时停止整个队列，不切下一个账号

- [ ] **Step 3: 核对遗留行为**

  手动确认以下行为没有被破坏：

  - 中控切号仍可从战网正常启动炉石
  - 手动点击主界面 `Stop` 不会误触发“自动重启失败”
  - 关闭中控后再启动普通单号模式，不会复用旧账号的战网 PID
  - 设置窗口中不再出现 `Hearthstone.exe` 路径

- [ ] **Step 4: 最终提交**

  ```bash
  git add BotMain/BattleNetRestartState.cs BotMain/BattleNetWindowManager.cs BotMain/BotService.cs BotMain/AccountController.cs BotMain/MainViewModel.cs BotMain/SettingsWindow.xaml BotCore.Tests/BotCore.Tests.csproj BotCore.Tests/BattleNetRestartStateTests.cs BotCore.Tests/BotServiceRestartStatusTests.cs
  git commit -m "实现战网实例约束的炉石自动重启"
  ```


# HSBox Constructed Hook Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Repair the HSBox constructed `ladder-opp` hook chain so callback payloads are captured reliably via early CDP injection and callback re-wrapping, with no runtime text fallback.

**Architecture:** Keep the constructed recommendation reader inside `BotMain/HsBoxRecommendationProvider.cs`, but split hooking into three explicit phases: pre-document bootstrap registration, immediate current-page installation, and callback-only state read. The injected JavaScript must track callback function identity, re-wrap `onUpdateLadderActionRecommend` when the page replaces it, and expose stable callback-derived state for both the bot and the standalone Python debugger.

**Tech Stack:** C#, Newtonsoft.Json, `ClientWebSocket`, xUnit, Python 3, `websocket-client`, Chrome DevTools Protocol (`Page.addScriptToEvaluateOnNewDocument`, `Runtime.evaluate`)

---

### Task 1: Lock `hook-only` semantics in tests

**Files:**
- Modify: `BotCore.Tests/HsBoxRecommendationProviderTests.cs`
- Modify: `BotMain/HsBoxRecommendationProvider.cs`
- Test: `BotCore.Tests/HsBoxRecommendationProviderTests.cs`

- [ ] **Step 1: Write the failing test that forbids body-text fallback when callback data is missing**

```csharp
[Fact]
public void RecommendActions_WaitsWhenConstructedHookHasNoCallbackPayload_EvenIfBodyTextShowsRecommendations()
{
    var state = new HsBoxRecommendationState
    {
        Ok = true,
        Count = 0,
        UpdatedAtMs = 0,
        Raw = null,
        BodyText = "网易炉石传说盒子 推荐打法 打出2号位随从 派对邪犬 打出1号位法术 咒怨之墓",
        Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
        Reason = "waiting_for_box_payload",
        Envelope = null
    };

    var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);
    var result = provider.RecommendActions(new ActionRecommendationRequest("seed", null, null, null));

    Assert.True(result.ShouldRetryWithoutAction);
    Assert.Empty(result.Actions);
    Assert.Contains("wait_retry", result.Detail, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("play_text", result.Detail, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Write the failing test that checks the constructed hook bootstrap script contains re-wrap state**

```csharp
[Fact]
public void BuildConstructedHookBootstrapScript_ContainsFunctionIdentityTrackingAndSetterHook()
{
    var script = InvokePrivateString("BuildConstructedHookBootstrapScript");

    Assert.Contains("__hbHsBoxHooks", script, StringComparison.Ordinal);
    Assert.Contains("lastSeen", script, StringComparison.Ordinal);
    Assert.Contains("Object.defineProperty", script, StringComparison.Ordinal);
    Assert.Contains("onUpdateLadderActionRecommend", script, StringComparison.Ordinal);
}
```

- [ ] **Step 3: Run the targeted tests to verify they fail on the current implementation**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HsBoxRecommendationProviderTests" -v minimal`

Expected: FAIL because the provider still allows constructed body-text fallback and the new bootstrap helper does not exist yet.

- [ ] **Step 4: Add the minimal test hooks in the provider so the test project can inspect the new script builder**

```csharp
private static string BuildConstructedHookBootstrapScript()
{
    return @"/* bootstrap script body goes here */";
}
```

```csharp
private static string BuildConstructedStateScript()
{
    return @"/* state read script body goes here */";
}
```

- [ ] **Step 5: Run the targeted tests again and keep them red for the missing behavior, not for missing methods**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HsBoxRecommendationProviderTests" -v minimal`

Expected: FAIL only on the intended behavior gaps.

- [ ] **Step 6: Commit the test scaffolding**

```bash
git add BotCore.Tests/HsBoxRecommendationProviderTests.cs BotMain/HsBoxRecommendationProvider.cs
git commit -m "测试：锁定构筑模式 hook-only 语义"
```

### Task 2: Remove constructed runtime text fallback and switch readiness to callback-only

**Files:**
- Modify: `BotMain/HsBoxRecommendationProvider.cs:3604-3608`
- Modify: `BotMain/HsBoxRecommendationProvider.cs:5882-6199`
- Test: `BotCore.Tests/HsBoxRecommendationProviderTests.cs`

- [ ] **Step 1: Implement the minimal provider gate that stops using constructed body-text mapping when no callback payload exists**

```csharp
if (state == null || state.Count <= 0 || state.Envelope == null || state.Envelope.Data == null || state.Envelope.Data.Count == 0)
{
    return ActionRecommendationResult.WaitRetry(
        $"wait_retry source={state?.SourceCallback ?? string.Empty}, freshReason={state?.Reason ?? "waiting_for_box_payload"}");
}
```

- [ ] **Step 2: Remove constructed action fallback calls that derive actions from `BodyText` during normal recommendation flow**

```csharp
// Delete or bypass calls to:
// TryMapPlayActionFromBodyText(...)
// TryMapAttackActionFromBodyText(...)
// TryMapLocationActionFromBodyText(...)
// for constructed runtime recommendation decisions.
```

- [ ] **Step 3: Keep `BodyText` in state objects and logs, but only as diagnostics**

```csharp
detail = $"wait_retry source={state?.SourceCallback ?? string.Empty}, bodyTextPresent={(string.IsNullOrWhiteSpace(state?.BodyText) ? 0 : 1)}";
```

- [ ] **Step 4: Run the targeted tests and verify the new `hook-only` behavior passes**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HsBoxRecommendationProviderTests" -v minimal`

Expected: PASS for the new `RecommendActions_WaitsWhenConstructedHookHasNoCallbackPayload_EvenIfBodyTextShowsRecommendations` test.

- [ ] **Step 5: Commit the provider behavior change**

```bash
git add BotMain/HsBoxRecommendationProvider.cs BotCore.Tests/HsBoxRecommendationProviderTests.cs
git commit -m "修复：构筑推荐仅接受 hook 回调数据"
```

### Task 3: Rebuild the constructed hook bootstrap script with function-identity rewrapping

**Files:**
- Modify: `BotMain/HsBoxRecommendationProvider.cs:3428-3510`
- Test: `BotCore.Tests/HsBoxRecommendationProviderTests.cs`

- [ ] **Step 1: Write the failing test for the bootstrap script content and callback-only reason strings**

```csharp
[Fact]
public void BuildConstructedStateScript_UsesCallbackOnlyReasons()
{
    var script = InvokePrivateString("BuildConstructedStateScript");

    Assert.Contains("ready_callback", script, StringComparison.Ordinal);
    Assert.Contains("waiting_for_box_payload", script, StringComparison.Ordinal);
    Assert.DoesNotContain("body_only", script, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the targeted tests to confirm the current script still fails**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HsBoxRecommendationProviderTests" -v minimal`

Expected: FAIL because the script still emits `ready` and only tracks `__hbHsBoxHooked`.

- [ ] **Step 3: Replace the old single-pass script with a bootstrap script that tracks original, wrapped, and last-seen functions**

```javascript
if (!window.__hbHsBoxHooks) window.__hbHsBoxHooks = {};

function normalizePayload(raw) {
  return (raw || '')
    .replaceAll('opp-target-hero', 'oppTargetHero')
    .replaceAll('opp-target', 'oppTarget')
    .replaceAll('target-hero', 'targetHero');
}

function recordPayload(name, raw) {
  window.__hbHsBoxCount = Number(window.__hbHsBoxCount || 0) + 1;
  window.__hbHsBoxUpdatedAt = Date.now();
  window.__hbHsBoxLastRaw = raw;
  window.__hbHsBoxLastSource = name;
  try {
    window.__hbHsBoxLastData = JSON.parse(normalizePayload(raw));
  } catch (error) {
    window.__hbHsBoxLastData = { __parseError: String(error), raw: raw };
  }
}

function installWrapper(name, candidate) {
  if (typeof candidate !== 'function') return false;
  const slot = window.__hbHsBoxHooks[name] || (window.__hbHsBoxHooks[name] = {});
  if (window[name] === slot.wrapped && slot.original === candidate) return true;

  slot.original = candidate;
  slot.lastSeen = candidate;
  slot.wrapped = function(payload) {
    payload = payload || '';
    recordPayload(name, payload);
    return slot.original.apply(this, arguments);
  };

  window[name] = slot.wrapped;
  return true;
}
```

- [ ] **Step 4: Add a dedicated setter hook for `onUpdateLadderActionRecommend` and fall back to scan-based rewrap if property interception fails**

```javascript
function installLadderSetter() {
  if (window.__hbHsBoxLadderSetterInstalled) return;
  try {
    let current = window.onUpdateLadderActionRecommend;
    Object.defineProperty(window, 'onUpdateLadderActionRecommend', {
      configurable: true,
      enumerable: true,
      get() { return current; },
      set(value) {
        current = value;
        installWrapper('onUpdateLadderActionRecommend', value);
      }
    });
    window.__hbHsBoxLadderSetterInstalled = true;
    if (typeof current === 'function') {
      installWrapper('onUpdateLadderActionRecommend', current);
    }
  } catch (_) {
    window.__hbHsBoxLadderSetterInstalled = false;
  }
}
```

- [ ] **Step 5: Split state reading into a separate script that reports callback-only readiness**

```javascript
const response = {
  ok: true,
  hooked: true,
  count: Number(window.__hbHsBoxCount || 0),
  updatedAt: Number(window.__hbHsBoxUpdatedAt || 0),
  raw: window.__hbHsBoxLastRaw ?? null,
  data: window.__hbHsBoxLastData ?? null,
  href: location.href,
  bodyText: document.body ? document.body.innerText.slice(0, 1500) : '',
  sourceCallback: window.__hbHsBoxLastSource ?? '',
  title: document.title ?? '',
  reason: ''
};

response.reason = response.count > 0 ? 'ready_callback' : 'waiting_for_box_payload';
return JSON.stringify(response);
```

- [ ] **Step 6: Run the targeted tests and verify the script-content tests pass**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HsBoxRecommendationProviderTests" -v minimal`

Expected: PASS for all script-content and hook-only tests added so far.

- [ ] **Step 7: Commit the hook script rewrite**

```bash
git add BotMain/HsBoxRecommendationProvider.cs BotCore.Tests/HsBoxRecommendationProviderTests.cs
git commit -m "修复：重建构筑推荐 hook 脚本并支持回调重包"
```

### Task 4: Add CDP pre-document bootstrap registration in the C# reader

**Files:**
- Modify: `BotMain/HsBoxRecommendationProvider.cs:3353-3425`
- Test: `BotCore.Tests/HsBoxRecommendationProviderTests.cs`

- [ ] **Step 1: Write the failing test that checks the provider builds a `Page.addScriptToEvaluateOnNewDocument` request**

```csharp
[Fact]
public void BuildAddScriptOnNewDocumentRequest_UsesPageMethod()
{
    var request = InvokePrivateJObject("BuildAddScriptOnNewDocumentRequest", 1, "bootstrap");

    Assert.Equal("Page.addScriptToEvaluateOnNewDocument", request["method"]?.Value<string>());
    Assert.Equal("bootstrap", request["params"]?["source"]?.Value<string>());
}
```

- [ ] **Step 2: Run the targeted tests to verify the request builder is still missing**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HsBoxRecommendationProviderTests" -v minimal`

Expected: FAIL because the helper does not exist yet.

- [ ] **Step 3: Add helper builders for CDP commands and reuse them inside `TryEvaluateState`**

```csharp
private static JObject BuildAddScriptOnNewDocumentRequest(int id, string source)
{
    return new JObject
    {
        ["id"] = id,
        ["method"] = "Page.addScriptToEvaluateOnNewDocument",
        ["params"] = new JObject
        {
            ["source"] = source
        }
    };
}
```

```csharp
private static JObject BuildEvaluateRequest(int id, string expression)
{
    return new JObject
    {
        ["id"] = id,
        ["method"] = "Runtime.evaluate",
        ["params"] = new JObject
        {
            ["expression"] = expression,
            ["returnByValue"] = true,
            ["awaitPromise"] = true
        }
    };
}
```

- [ ] **Step 4: Change `TryEvaluateState` to send three commands in order**

```csharp
SendCommand(socket, BuildAddScriptOnNewDocumentRequest(1, BuildConstructedHookBootstrapScript()), cts.Token);
ReceiveResponseById(socket, 1, cts.Token);

SendCommand(socket, BuildEvaluateRequest(2, BuildConstructedHookBootstrapScript()), cts.Token);
ReceiveResponseById(socket, 2, cts.Token);

SendCommand(socket, BuildEvaluateRequest(3, BuildConstructedStateScript()), cts.Token);
var value = ReceiveStringResultById(socket, 3, cts.Token);
```

- [ ] **Step 5: Keep the current-page immediate install even after pre-document registration**

```csharp
// Do not remove the second Runtime.evaluate call.
// The addScript registration only protects future navigations and reloads.
```

- [ ] **Step 6: Run the targeted tests and verify the request-builder coverage passes**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HsBoxRecommendationProviderTests" -v minimal`

Expected: PASS for the CDP request helper tests.

- [ ] **Step 7: Commit the C# CDP bootstrap registration change**

```bash
git add BotMain/HsBoxRecommendationProvider.cs BotCore.Tests/HsBoxRecommendationProviderTests.cs
git commit -m "修复：构筑模式 CDP 预注入 hook 脚本"
```

### Task 5: Mirror the same hook lifecycle in the standalone Python debugger

**Files:**
- Modify: `tools/hsbox_standard.py`
- Test: `tools/hsbox_standard.py`

- [ ] **Step 1: Add a helper that sends arbitrary CDP commands by id**

```python
def send_cdp(ws, request_id, method, params):
    ws.send(json.dumps({
        "id": request_id,
        "method": method,
        "params": params,
    }))
```

- [ ] **Step 2: Add a matching helper that waits for a specific response id**

```python
def recv_cdp_result(ws, request_id):
    while True:
        msg = ws.recv()
        if not msg:
            continue
        response = json.loads(msg)
        if response.get("id") != request_id:
            continue
        return response
```

- [ ] **Step 3: Replace the single `Runtime.evaluate` call with the same three-stage sequence used in C#**

```python
send_cdp(ws, 1, "Page.addScriptToEvaluateOnNewDocument", {"source": BOOTSTRAP_SCRIPT})
recv_cdp_result(ws, 1)

send_cdp(ws, 2, "Runtime.evaluate", {
    "expression": BOOTSTRAP_SCRIPT,
    "returnByValue": True,
    "awaitPromise": True,
})
recv_cdp_result(ws, 2)

send_cdp(ws, 3, "Runtime.evaluate", {
    "expression": STATE_SCRIPT,
    "returnByValue": True,
    "awaitPromise": True,
})
result = recv_cdp_result(ws, 3)
```

- [ ] **Step 4: Align the printed state reason wording with the callback-only semantics**

```python
print(f"  原因:         {state.get('reason', '?')}")
# Expected values now include:
# ready_callback / waiting_for_box_payload / callback_missing
```

- [ ] **Step 5: Run a Python syntax check**

Run: `python -m py_compile tools/hsbox_standard.py`

Expected: no output

- [ ] **Step 6: Commit the debugger alignment**

```bash
git add -- tools/hsbox_standard.py
git commit -m "修复：同步构筑模式 hook 调试脚本"
```

### Task 6: Full verification and live acceptance

**Files:**
- Modify: `BotMain/HsBoxRecommendationProvider.cs`
- Modify: `BotCore.Tests/HsBoxRecommendationProviderTests.cs`
- Modify: `tools/hsbox_standard.py`

- [ ] **Step 1: Run the focused unit tests for the provider**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HsBoxRecommendationProviderTests" -v minimal`

Expected: PASS

- [ ] **Step 2: Run the full BotCore test project**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj -v minimal`

Expected: PASS

- [ ] **Step 3: Run the Python syntax check one more time**

Run: `python -m py_compile tools/hsbox_standard.py`

Expected: no output

- [ ] **Step 4: Manually verify the hook in a live constructed match**

Run: `python tools/hsbox_standard.py`

Expected progression:
- before callback: `reason=waiting_for_box_payload`
- after callback arrives: `count > 0`, `raw != null`, `data != null`, `sourceCallback=onUpdateLadderActionRecommend`

- [ ] **Step 5: Manually verify callback replacement resilience**

Run: `python tools/hsbox_standard.py`

Expected:
- after switching modules or after page-side re-render, callback capture still resumes
- the script does not get stuck forever at `count=0` once new payloads continue arriving

- [ ] **Step 6: Commit the final verified state**

```bash
git add BotMain/HsBoxRecommendationProvider.cs BotCore.Tests/HsBoxRecommendationProviderTests.cs
git add -- tools/hsbox_standard.py
git commit -m "完成构筑模式 HSBox hook 链路修复"
```

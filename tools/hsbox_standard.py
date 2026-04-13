#!/usr/bin/env python3
"""
HSBox 标准模式数据抓取器
通过 Chrome DevTools Protocol 连接炉石盒子内嵌浏览器，
读取标准模式 (client-jipaiqi) 页面的结构化推荐数据。

使用方法: python hsbox_standard.py
前提: 炉石传说已启动且盒子处于对战中。
"""

import json
import sys
import time
import urllib.request
import websocket  # pip install websocket-client

CDP_ENDPOINT = "http://127.0.0.1:9222/json/list"
PAGE_URL_KEYWORD = "/client-jipaiqi/"
PREFERRED_URL_KEYWORD = "/client-jipaiqi/ladder-opp"

# ── 注入浏览器的 JS 脚本（与 C# 端保持一致） ──
BOOTSTRAP_SCRIPT = r"""(() => {
  function normalizePayload(raw) {
    return (raw || '')
      .replaceAll('opp-target-hero', 'oppTargetHero')
      .replaceAll('opp-target', 'oppTarget')
      .replaceAll('target-hero', 'targetHero');
  }

  function ensureState() {
    if (!window.__hbHsBoxHooks) window.__hbHsBoxHooks = {};
    if (!window.__hbHsBoxSetterState) window.__hbHsBoxSetterState = {};
    if (typeof window.__hbHsBoxCount !== 'number') window.__hbHsBoxCount = Number(window.__hbHsBoxCount || 0);
    if (typeof window.__hbHsBoxUpdatedAt !== 'number') window.__hbHsBoxUpdatedAt = Number(window.__hbHsBoxUpdatedAt || 0);
    if (typeof window.__hbHsBoxHasConstructedCallback !== 'boolean') window.__hbHsBoxHasConstructedCallback = false;
  }

  function recordPayload(name, raw) {
    ensureState();
    raw = raw || '';
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

  function buildWrapped(name, original) {
    ensureState();
    const slot = window.__hbHsBoxHooks[name] || (window.__hbHsBoxHooks[name] = {});
    slot.original = original;
    slot.lastSeen = original;
    slot.wrapped = function(payload) {
      payload = payload || '';
      recordPayload(name, payload);
      return slot.original.apply(this, arguments);
    };
    slot.wrapped.__hbHsBoxManaged = true;
    slot.wrapped.__hbHsBoxOriginal = original;
    return slot.wrapped;
  }

  function unwrapManagedCandidate(slot, candidate) {
    if (typeof candidate !== 'function') return candidate;
    if (candidate === slot.wrapped && typeof slot.original === 'function') return slot.original;
    if (candidate.__hbHsBoxManaged === true && typeof candidate.__hbHsBoxOriginal === 'function') {
      return candidate.__hbHsBoxOriginal;
    }
    return candidate;
  }

  function installWrapper(name, candidate, assignGlobal) {
    ensureState();
    if (typeof candidate !== 'function') return false;
    const slot = window.__hbHsBoxHooks[name] || (window.__hbHsBoxHooks[name] = {});
    window.__hbHsBoxHasConstructedCallback = true;
    candidate = unwrapManagedCandidate(slot, candidate);

    if (slot.original === candidate && typeof slot.wrapped === 'function') {
      slot.lastSeen = candidate;
      if (assignGlobal && window[name] !== slot.wrapped) {
        window[name] = slot.wrapped;
      }
      return true;
    }

    const wrapped = buildWrapped(name, candidate);
    if (assignGlobal) {
      window[name] = wrapped;
    }
    return true;
  }

  function ensureLadderSetter() {
    ensureState();
    const name = 'onUpdateLadderActionRecommend';
    const existingDescriptor = Object.getOwnPropertyDescriptor(window, name);
    if (existingDescriptor && existingDescriptor.configurable === false) {
      const current = window[name];
      if (typeof current === 'function') {
        installWrapper(name, current, true);
      }
      return;
    }

    if (window.__hbHsBoxLadderSetterInstalled) {
      const ladderState = window.__hbHsBoxSetterState[name];
      const current = ladderState ? ladderState.current : window[name];
      if (typeof current === 'function') {
        installWrapper(name, current, false);
        if (ladderState) {
          ladderState.current = window.__hbHsBoxHooks[name].wrapped;
        }
        window.__hbHsBoxHasConstructedCallback = true;
      }
      return;
    }

    const setterState = {
      current: window[name],
      internalWrite: false
    };
    window.__hbHsBoxSetterState[name] = setterState;

    Object.defineProperty(window, name, {
      configurable: true,
      enumerable: true,
      get() {
        return setterState.current;
      },
      set(value) {
        setterState.current = value;
        if (setterState.internalWrite) return;
        if (typeof value !== 'function') return;

        installWrapper(name, value, false);
        const slot = window.__hbHsBoxHooks[name];
        if (slot && typeof slot.wrapped === 'function') {
          setterState.internalWrite = true;
          setterState.current = slot.wrapped;
          setterState.internalWrite = false;
        }
      }
    });

    window.__hbHsBoxLadderSetterInstalled = true;

    if (typeof setterState.current === 'function') {
      installWrapper(name, setterState.current, false);
      const slot = window.__hbHsBoxHooks[name];
      if (slot && typeof slot.wrapped === 'function') {
        setterState.internalWrite = true;
        setterState.current = slot.wrapped;
        setterState.internalWrite = false;
        window.__hbHsBoxHasConstructedCallback = true;
      }
    }
  }

  function installPatternHooks() {
    ensureState();
    const pattern = /^onUpdate\w*Recommend$/;
    for (const key of Object.keys(window)) {
      if (!pattern.test(key)) continue;
      if (key === 'onUpdateLadderActionRecommend') continue;
      const candidate = window[key];
      if (typeof candidate !== 'function') continue;
      installWrapper(key, candidate, true);
    }
  }

  function tryRecoverFromReactState() {
    ensureState();
    if (Number(window.__hbHsBoxCount || 0) > 0) return false;
    try {
      var fiberKey = null;
      var allEls = document.querySelectorAll('*');
      for (var i = 0; i < allEls.length; i++) {
        var keys = Object.keys(allEls[i]);
        for (var j = 0; j < keys.length; j++) {
          if (keys[j].indexOf('__reactFiber') === 0) {
            fiberKey = keys[j];
            break;
          }
        }
        if (fiberKey) break;
      }
      if (!fiberKey) return false;

      var rootEl = null;
      for (var i2 = 0; i2 < allEls.length; i2++) {
        if (allEls[i2][fiberKey]) { rootEl = allEls[i2]; break; }
      }
      if (!rootEl) return false;

      var found = null;
      var visited = 0;
      function walkFiber(node, depth) {
        if (!node || visited > 300 || depth > 30 || found) return;
        visited++;
        var state = node.memoizedState;
        var hookIdx = 0;
        while (state && hookIdx < 15) {
          var val = state.memoizedState;
          if (val && typeof val === 'object' && !(val instanceof HTMLElement) && !Array.isArray(val)) {
            var vk = Object.keys(val);
            var hasData = false;
            var hasStatus = false;
            for (var m = 0; m < vk.length; m++) {
              if (vk[m] === 'data') hasData = true;
              if (vk[m] === 'status') hasStatus = true;
            }
            if (hasData && hasStatus && Array.isArray(val.data) && val.data.length > 0) {
              var first = val.data[0];
              if (first && typeof first === 'object' && typeof first.actionName === 'string') {
                found = val;
                return;
              }
            }
          }
          state = state.next;
          hookIdx++;
        }
        if (node.child) walkFiber(node.child, depth + 1);
        if (node.sibling) walkFiber(node.sibling, depth);
      }
      walkFiber(rootEl[fiberKey], 0);

      if (found) {
        window.__hbHsBoxCount = 1;
        window.__hbHsBoxUpdatedAt = Date.now();
        window.__hbHsBoxLastData = found;
        window.__hbHsBoxLastSource = 'react_state_recovery';
        try {
          var seen = [];
          window.__hbHsBoxLastRaw = JSON.stringify(found, function(k, v) {
            if (v && typeof v === 'object') {
              if (v instanceof HTMLElement) return undefined;
              if (seen.indexOf(v) >= 0) return undefined;
              seen.push(v);
            }
            return v;
          });
        } catch(e) {
          window.__hbHsBoxLastRaw = '';
        }
        return true;
      }
    } catch (e) {}
    return false;
  }

  try {
    ensureLadderSetter();
    installPatternHooks();
    window.__hbHsBoxReactRecover = tryRecoverFromReactState;
    tryRecoverFromReactState();
    window.__hbHsBoxBootstrapInstalled = true;
    window.__hbHsBoxBootstrapError = '';
    return JSON.stringify({ ok: true, installed: true, error: '' });
  } catch (error) {
    const message = String(error && error.message ? error.message : error);
    window.__hbHsBoxBootstrapInstalled = false;
    window.__hbHsBoxBootstrapError = message;
    return JSON.stringify({ ok: false, installed: false, error: message });
  }
})()"""

STATE_SCRIPT = r"""(() => {
  const response = {
    ok: false,
    hooked: false,
    count: Number(window.__hbHsBoxCount || 0),
    updatedAt: Number(window.__hbHsBoxUpdatedAt || 0),
    raw: window.__hbHsBoxLastRaw ?? null,
    data: window.__hbHsBoxLastData ?? null,
    href: location.href,
    bodyText: document.body ? document.body.innerText.slice(0, 1500) : '',
    reason: '',
    sourceCallback: window.__hbHsBoxLastSource ?? '',
    title: document.title ?? ''
  };
  try {
    if (typeof window.__hbHsBoxBootstrapInstalled !== 'boolean'
        || !window.__hbHsBoxBootstrapInstalled) {
      response.reason = window.__hbHsBoxBootstrapError
        ? 'hook_install_failed:' + window.__hbHsBoxBootstrapError
        : 'hook_install_failed';
      return JSON.stringify(response);
    }
    response.ok = true;
    response.hooked = true;

    if (Number(window.__hbHsBoxCount || 0) === 0 && typeof window.__hbHsBoxReactRecover === 'function') {
      window.__hbHsBoxReactRecover();
    }

    response.count = Number(window.__hbHsBoxCount || 0);
    response.updatedAt = Number(window.__hbHsBoxUpdatedAt || 0);
    response.raw = window.__hbHsBoxLastRaw ?? null;
    response.data = window.__hbHsBoxLastData ?? null;
    response.sourceCallback = window.__hbHsBoxLastSource ?? '';
    response.reason = response.count > 0
      ? 'ready_callback'
      : (window.__hbHsBoxHasConstructedCallback ? 'waiting_for_box_payload' : 'callback_missing');
    return JSON.stringify(response);
  } catch (error) {
    response.reason = String(error && error.message ? error.message : error);
    return JSON.stringify(response);
  }
})()"""


def get_debugger_url():
    """从 CDP 端点获取标准模式页面的 WebSocket 调试 URL"""
    try:
        req = urllib.request.Request(CDP_ENDPOINT)
        with urllib.request.urlopen(req, timeout=3) as resp:
            targets = json.loads(resp.read().decode("utf-8"))
    except Exception as e:
        print(f"❌ 无法连接 CDP 端点 ({CDP_ENDPOINT}): {e}")
        print("   请确认炉石传说已启动且盒子内嵌浏览器可用。")
        return None

    # 优先查找 ladder-opp 页面
    pages = [t for t in targets if PAGE_URL_KEYWORD in (t.get("url") or "")]
    if not pages:
        print(f"❌ 未找到标准模式页面 (关键词: {PAGE_URL_KEYWORD})")
        print("   当前可用页面:")
        for t in targets:
            print(f"     - {t.get('url', '(无url)')}")
        return None

    preferred = [p for p in pages if PREFERRED_URL_KEYWORD in (p.get("url") or "")]
    target = preferred[0] if preferred else pages[0]

    ws_url = target.get("webSocketDebuggerUrl")
    if not ws_url:
        print("❌ 目标页面缺少 webSocketDebuggerUrl")
        return None

    print(f"✅ 找到标准模式页面: {target.get('url', '?')}")
    print(f"   WebSocket: {ws_url}")
    return ws_url


def send_cdp(ws, request_id, method, params):
    ws.send(json.dumps({
        "id": request_id,
        "method": method,
        "params": params,
    }))


def recv_cdp_result(ws, request_id):
    while True:
        msg = ws.recv()
        if not msg:
            continue
        response = json.loads(msg)
        if response.get("id") != request_id:
            continue
        return response


def evaluate_on_page(ws_url):
    """通过 CDP WebSocket 在页面上安装 hook 并读取状态"""
    ws = websocket.create_connection(ws_url, timeout=5)
    try:
        send_cdp(ws, 1, "Page.addScriptToEvaluateOnNewDocument", {
            "source": BOOTSTRAP_SCRIPT,
        })
        response = recv_cdp_result(ws, 1)
        if response.get("error"):
            print(f"⚠️ 预注入失败: {response['error'].get('message', '未知错误')}")
            return None

        send_cdp(ws, 2, "Runtime.evaluate", {
            "expression": BOOTSTRAP_SCRIPT,
            "returnByValue": True,
            "awaitPromise": True,
        })
        response = recv_cdp_result(ws, 2)
        if response.get("error"):
            print(f"⚠️ 当前页 hook 安装失败: {response['error'].get('message', '未知错误')}")
            return None

        send_cdp(ws, 3, "Runtime.evaluate", {
            "expression": STATE_SCRIPT,
            "returnByValue": True,
            "awaitPromise": True,
        })
        response = recv_cdp_result(ws, 3)
        result = response.get("result", {}).get("result", {})
        if result.get("type") == "string":
            return result.get("value")
        exc = response.get("result", {}).get("exceptionDetails")
        if exc:
            print(f"⚠️ JS 异常: {exc.get('text', '未知')}")
        return None
    finally:
        ws.close()


def format_action_step(step, index):
    """格式化单个推荐动作步骤"""
    lines = []
    action = step.get("actionName", "?")
    lines.append(f"  [{index}] 动作: {action}")

    # 卡牌信息
    for card_key in ("card", "cards"):
        card_data = step.get(card_key)
        if card_data:
            if isinstance(card_data, list):
                for ci, c in enumerate(card_data):
                    card_id = c.get("cardId", "?")
                    zone_pos = c.get("zonePosition", c.get("zone_position", "?"))
                    zone_name = c.get("zoneName", c.get("zone_name", ""))
                    lines.append(f"       卡牌[{ci}]: {card_id} (位置={zone_pos}, 区域={zone_name})")
            elif isinstance(card_data, dict):
                card_id = card_data.get("cardId", "?")
                zone_pos = card_data.get("zonePosition", card_data.get("zone_position", "?"))
                zone_name = card_data.get("zoneName", card_data.get("zone_name", ""))
                lines.append(f"       卡牌: {card_id} (位置={zone_pos}, 区域={zone_name})")

    # 目标信息
    target = step.get("target")
    if target:
        t_card = target.get("cardId", "?")
        t_pos = target.get("zonePosition", target.get("zone_position", "?"))
        lines.append(f"       目标: {t_card} (位置={t_pos})")

    opp_target = step.get("oppTarget")
    if opp_target:
        t_card = opp_target.get("cardId", "?")
        t_pos = opp_target.get("zonePosition", opp_target.get("zone_position", "?"))
        lines.append(f"       对手目标: {t_card} (位置={t_pos})")

    target_hero = step.get("targetHero")
    if target_hero:
        lines.append(f"       目标英雄: {target_hero.get('cardId', '?')}")

    sub_option = step.get("subOption")
    if sub_option:
        lines.append(f"       子选项: {sub_option.get('cardId', '?')}")

    position = step.get("position")
    if position:
        lines.append(f"       放置位置: {position}")

    return "\n".join(lines)


def display_state(state_json):
    """以结构化格式输出状态数据"""
    state = json.loads(state_json)
    sep = "═" * 60

    print(f"\n{sep}")
    print("  🎴 HSBox 标准模式 - 结构化数据")
    print(sep)

    # ── 基本信息 ──
    print(f"\n📌 基本信息:")
    print(f"  状态:         {'✅ 正常' if state.get('ok') else '❌ 异常'}")
    print(f"  Hook 状态:    {'已安装' if state.get('hooked') else '未安装'}")
    print(f"  回调次数:     {state.get('count', 0)}")
    updated_at = state.get("updatedAt", 0)
    if updated_at > 0:
        ts = time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(updated_at / 1000))
        print(f"  最后更新:     {ts} ({updated_at}ms)")
    else:
        print(f"  最后更新:     无")
    print(f"  原因:         {state.get('reason', '?')}")
    print(f"  来源回调:     {state.get('sourceCallback', '?')}")
    print(f"  页面 URL:     {state.get('href', '?')}")
    print(f"  页面标题:     {state.get('title', '?')}")

    # ── 结构化推荐数据 ──
    data = state.get("data")
    print(f"\n📊 结构化推荐数据 (data):")
    if data is None:
        print("  (无数据 - 盒子尚未推送推荐)")
    elif isinstance(data, dict):
        # 检查是否是 envelope 格式 {status, data: [...], error, ...}
        envelope_data = data.get("data")
        status = data.get("status")
        error = data.get("error")
        choice_id = data.get("choiceId")
        option_id = data.get("optionId")
        turn_num = data.get("turnNum")

        if status is not None:
            print(f"  信封状态(status):  {status}")
        if error:
            print(f"  错误(error):       {error}")
        if choice_id is not None:
            print(f"  选择ID(choiceId):  {choice_id}")
        if option_id is not None:
            print(f"  选项ID(optionId):  {option_id}")
        if turn_num is not None:
            print(f"  回合数(turnNum):   {turn_num}")

        if isinstance(envelope_data, list) and len(envelope_data) > 0:
            print(f"\n  推荐步骤 ({len(envelope_data)} 条):")
            for i, step in enumerate(envelope_data):
                print(format_action_step(step, i + 1))
        elif envelope_data is not None:
            print(f"  data.data: {json.dumps(envelope_data, ensure_ascii=False, indent=4)}")
        else:
            # data 本身就是步骤列表或其他格式
            if "__parseError" in data:
                print(f"  ⚠️ JSON 解析错误: {data['__parseError']}")
                if "raw" in data:
                    raw_preview = str(data["raw"])[:300]
                    print(f"  原始数据(前300字符): {raw_preview}")
            else:
                print(f"  {json.dumps(data, ensure_ascii=False, indent=4)}")
    else:
        print(f"  {json.dumps(data, ensure_ascii=False, indent=4)}")

    # ── 原始数据 ──
    raw = state.get("raw")
    print(f"\n📝 原始回调数据 (raw):")
    if raw is None:
        print("  (无)")
    else:
        raw_str = str(raw)
        if len(raw_str) > 500:
            print(f"  (长度 {len(raw_str)} 字符, 显示前500字符)")
            print(f"  {raw_str[:500]}...")
        else:
            print(f"  {raw_str}")

    # ── 页面文本 ──
    body_text = state.get("bodyText", "")
    print(f"\n📄 页面文本 (bodyText):")
    if body_text:
        # 清理多余空行
        lines = [line.strip() for line in body_text.split("\n") if line.strip()]
        for line in lines[:20]:
            print(f"  {line}")
        if len(lines) > 20:
            print(f"  ... (共 {len(lines)} 行)")
    else:
        print("  (空)")

    print(f"\n{sep}")

    # ── 完整 JSON 输出 ──
    print("\n🔍 完整 JSON (折叠格式):")
    print(json.dumps(state, ensure_ascii=False, indent=2))
    print()


def main():
    print("=" * 60)
    print("  HSBox 标准模式数据抓取器")
    print("  CDP 端点: " + CDP_ENDPOINT)
    print("=" * 60)

    ws_url = get_debugger_url()
    if not ws_url:
        sys.exit(1)

    print("\n⏳ 正在执行 JS 脚本...")
    result = evaluate_on_page(ws_url)
    if not result:
        print("❌ JS 执行返回空结果")
        sys.exit(1)

    display_state(result)


if __name__ == "__main__":
    main()

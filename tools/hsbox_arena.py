#!/usr/bin/env python3
"""
HSBox 竞技场选牌数据抓取器
通过 Chrome DevTools Protocol 连接炉石盒子内嵌浏览器，
从 ai-recommend 页面的 React state 读取选牌推荐。

数据结构: {data: [{actionName:"choose", card:{cardId:"CATA_151", cardName:"..."}}], status:2}

使用方法: python hsbox_arena.py
         python hsbox_arena.py --watch   (持续监听模式)
前提: 炉石传说已启动且在竞技场选牌界面。
"""

import json
import sys
import time
import urllib.request
import websocket  # pip install websocket-client

CDP_ENDPOINT = "http://127.0.0.1:9222/json/list"
PAGE_URL_KEYWORD = "/client-jipaiqi/ai-recommend"

# ── 从 ai-recommend 页面的 React state 读取选牌推荐 ──
READ_STATE_SCRIPT = r"""(() => {
    var r = { ok: false, reason: '', recommendedCardId: null, recommendedCardName: null,
              actionName: null, fullData: null };

    var root = document.getElementById('root') || document.body.firstElementChild;
    if (!root) { r.reason = 'no_root'; return JSON.stringify(r); }
    var ck = Object.keys(root).find(function(k) { return k.startsWith('__reactContainer'); });
    if (!ck) { r.reason = 'no_react'; return JSON.stringify(r); }

    var fiber = root[ck];
    var visited = new Set();
    var queue = [fiber];

    while (queue.length > 0) {
        var node = queue.shift();
        if (!node || visited.has(node)) continue;
        visited.add(node);

        var ms = node.memoizedState;
        var idx = 0;
        while (ms && idx < 15) {
            var val = ms.memoizedState;

            // 匹配 {data: [{actionName:..., card:...}], status:...}
            if (val && typeof val === 'object' && Array.isArray(val.data) && val.data.length > 0
                && val.data[0] && val.data[0].actionName) {
                r.ok = true;
                r.reason = 'react_state';
                r.fullData = val;
                var step = val.data[0];
                r.actionName = step.actionName;
                if (step.card) {
                    r.recommendedCardId = step.card.cardId || null;
                    r.recommendedCardName = step.card.cardName || null;
                }
                return JSON.stringify(r);
            }

            // 也检查 ref.current
            if (val && val.current && typeof val.current === 'object'
                && Array.isArray(val.current.data) && val.current.data.length > 0
                && val.current.data[0] && val.current.data[0].actionName) {
                r.ok = true;
                r.reason = 'react_state_ref';
                r.fullData = val.current;
                var step2 = val.current.data[0];
                r.actionName = step2.actionName;
                if (step2.card) {
                    r.recommendedCardId = step2.card.cardId || null;
                    r.recommendedCardName = step2.card.cardName || null;
                }
                return JSON.stringify(r);
            }

            ms = ms.next; idx++;
        }

        if (node.child) queue.push(node.child);
        if (node.sibling) queue.push(node.sibling);
        if (queue.length > 500) break;
    }

    r.reason = 'no_data';
    return JSON.stringify(r);
})()"""


def find_ws_url():
    try:
        data = json.loads(urllib.request.urlopen(CDP_ENDPOINT, timeout=3).read())
    except Exception as e:
        print(f"[ERROR] 无法连接 CDP: {e}")
        return None

    for target in data:
        if PAGE_URL_KEYWORD in target.get("url", ""):
            ws = target.get("webSocketDebuggerUrl", "")
            if ws:
                print(f"[OK] 找到 ai-recommend 页面: {target['url']}")
                return ws

    print("[ERROR] 未找到 ai-recommend 页面。当前页面:")
    for t in data:
        print(f"  - {t.get('url', '???')}")
    return None


def cdp_evaluate(ws, expression, id=1):
    msg = json.dumps({"id": id, "method": "Runtime.evaluate",
                       "params": {"expression": expression, "returnByValue": True}})
    ws.send(msg)
    resp = json.loads(ws.recv())
    val = resp.get("result", {}).get("result", {}).get("value")
    if isinstance(val, str):
        try:
            return json.loads(val)
        except:
            return val
    return val


def dump_once(ws):
    print("\n" + "=" * 60)
    print("  竞技场选牌推荐 (ai-recommend 页面)")
    print("=" * 60)

    state = cdp_evaluate(ws, READ_STATE_SCRIPT, id=1)

    print(f"\n[状态] ok={state.get('ok')}, reason={state.get('reason')}")
    print(f"[推荐动作] {state.get('actionName')}")
    print(f"[推荐卡牌] {state.get('recommendedCardId')} ({state.get('recommendedCardName')})")

    full = state.get("fullData")
    if full:
        print(f"\n[完整推荐数据]")
        print(json.dumps(full, indent=2, ensure_ascii=False))
    else:
        print("\n[完整数据] 暂无")

    return state


def watch_mode(ws):
    print("\n[Watch] 持续监听，每2秒刷新，Ctrl+C 退出\n")
    last_card = None

    while True:
        try:
            state = cdp_evaluate(ws, READ_STATE_SCRIPT, id=10)
            card_id = state.get("recommendedCardId")

            if card_id != last_card:
                last_card = card_id
                ts = time.strftime("%H:%M:%S")
                action = state.get("actionName", "?")
                name = state.get("recommendedCardName", "?")
                print(f"[{ts}] action={action} -> {card_id} ({name})")

                full = state.get("fullData")
                if full and full.get("data"):
                    for i, step in enumerate(full["data"]):
                        a = step.get("actionName", "?")
                        c = step.get("card", {})
                        cid = c.get("cardId", "?")
                        cname = c.get("cardName", "?")
                        t = step.get("target", {})
                        tid = t.get("cardId", "") if t else ""
                        print(f"  [{i+1}] {a}: {cid} ({cname}){' -> ' + tid if tid else ''}")

            time.sleep(2)
        except KeyboardInterrupt:
            print("\n[停止]")
            break
        except Exception as e:
            print(f"[错误] {e}")
            time.sleep(3)


def main():
    ws_url = find_ws_url()
    if not ws_url:
        sys.exit(1)

    ws = websocket.create_connection(ws_url, timeout=5)
    print("[OK] 已连接")

    try:
        if "--watch" in sys.argv:
            watch_mode(ws)
        else:
            dump_once(ws)
    finally:
        ws.close()


if __name__ == "__main__":
    main()

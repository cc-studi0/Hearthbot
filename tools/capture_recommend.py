"""
抓包：监听盒子在推荐打法时的网络请求 + 捕获推荐数据格式
在对局中运行，等待30秒，记录所有 HTTP/WebSocket 流量和推荐数据
"""
import json, asyncio, sys, urllib.request

try:
    import websockets
except ImportError:
    print("pip install websockets")
    sys.exit(1)


async def find_active_page():
    pages = json.loads(urllib.request.urlopen('http://127.0.0.1:9222/json').read())
    for p in pages:
        ws_url = p.get('webSocketDebuggerUrl', '')
        if not ws_url:
            continue
        try:
            async with websockets.connect(ws_url, max_size=10*1024*1024) as ws:
                await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate',
                    'params': {'expression': 'typeof window.onShowModule !== "undefined"', 'returnByValue': True}}))
                r = json.loads(await ws.recv())
                if r.get('result', {}).get('result', {}).get('value') == True:
                    return p
        except:
            pass
    # fallback
    for p in pages:
        if 'ladder' in p.get('url', '') or 'analysis' in p.get('url', ''):
            return p
    return pages[0] if pages else None


async def main():
    page = await find_active_page()
    if not page:
        print("[!] 盒子未运行")
        return

    print(f"[+] 连接: {page['url'][:60]}")
    ws_url = page['webSocketDebuggerUrl']

    async with websockets.connect(ws_url, max_size=50*1024*1024) as ws:
        # 启用网络监听
        await ws.send(json.dumps({'id': 1, 'method': 'Network.enable', 'params': {}}))
        await ws.recv()

        # hook onUpdateLadderActionRecommend 捕获数据
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {
            'expression': '''
                (function(){
                    window.__capturedRecommend = [];
                    var orig = window.onUpdateLadderActionRecommend;
                    function wrap(fn) {
                        return function(data) {
                            window.__capturedRecommend.push({
                                time: new Date().toISOString(),
                                len: data ? data.length : 0,
                                data: data
                            });
                            if (fn) return fn(data);
                        };
                    }
                    if (typeof orig === 'function') {
                        window.onUpdateLadderActionRecommend = wrap(orig);
                    }
                    // 拦截未来赋值
                    var cur = window.onUpdateLadderActionRecommend;
                    Object.defineProperty(window, 'onUpdateLadderActionRecommend', {
                        get: function(){ return cur; },
                        set: function(fn){ cur = wrap(fn); },
                        configurable: true
                    });
                    return 'capture ready';
                })()
            ''',
            'returnByValue': True
        }}))
        await ws.recv()

        duration = 45
        print(f"[*] 监听 {duration} 秒... (请在游戏中操作几个回合)")
        print()

        http_requests = []
        ws_messages = []
        start = asyncio.get_event_loop().time()

        while asyncio.get_event_loop().time() - start < duration:
            try:
                msg = await asyncio.wait_for(ws.recv(), timeout=1)
                data = json.loads(msg)
                method = data.get('method', '')

                if method == 'Network.requestWillBeSent':
                    req = data.get('params', {}).get('request', {})
                    url = req.get('url', '')
                    meth = req.get('method', '')
                    if not url.startswith('data:'):
                        http_requests.append({'method': meth, 'url': url})
                        print(f"  HTTP {meth}: {url[:120]}")

                elif method == 'Network.responseReceived':
                    resp = data.get('params', {}).get('response', {})
                    url = resp.get('url', '')
                    status = resp.get('status', 0)
                    if 'recommend' in url.lower() or 'action' in url.lower() or 'medal' in url.lower():
                        print(f"  响应 {status}: {url[:120]}")

                elif method == 'Network.webSocketFrameReceived':
                    payload = data.get('params', {}).get('response', {}).get('payloadData', '')
                    if payload:
                        ws_messages.append(payload)
                        # 只打印看起来有意义的
                        if any(k in payload.lower() for k in ['recommend', 'action', 'medal', 'rank', 'league']):
                            print(f"  WS收到: {payload[:200]}")

            except asyncio.TimeoutError:
                pass

        # 获取捕获的推荐数据
        await ws.send(json.dumps({'id': 3, 'method': 'Runtime.evaluate', 'params': {
            'expression': 'JSON.stringify(window.__capturedRecommend || [])',
            'returnByValue': True
        }}))
        r = json.loads(await ws.recv())
        captured = json.loads(r.get('result', {}).get('result', {}).get('value', '[]'))

        print(f"\n{'='*60}")
        print(f"捕获到 {len(captured)} 条推荐数据:")
        for i, c in enumerate(captured):
            print(f"\n  #{i+1} 时间={c['time']} 长度={c['len']}")
            if c.get('data'):
                # 尝试解析 JSON
                try:
                    parsed = json.loads(c['data'])
                    print(f"  状态: status={parsed.get('status')}, error={parsed.get('error','')}")
                    if parsed.get('data'):
                        print(f"  动作数: {len(parsed['data'])}")
                        for j, action in enumerate(parsed['data'][:3]):
                            print(f"    动作{j}: {action.get('actionName','')} card={action.get('card',{}).get('CardID','')}")
                except:
                    print(f"  原始: {c['data'][:300]}")

        print(f"\nHTTP请求总计: {len(http_requests)}")
        unique_urls = set(r['url'] for r in http_requests)
        for url in sorted(unique_urls):
            print(f"  {url[:150]}")

        print(f"\nWebSocket消息总计: {len(ws_messages)}")

        # 保存完整数据到文件
        output = {
            'captured_recommend': captured,
            'http_requests': http_requests,
            'ws_message_count': len(ws_messages),
        }
        with open('C:/Users/qq324/AppData/Local/HearthstoneBot/capture_result.json', 'w', encoding='utf-8') as f:
            json.dump(output, f, ensure_ascii=False, indent=2)
        print(f"\n完整数据已保存到 capture_result.json")


if __name__ == '__main__':
    asyncio.run(main())

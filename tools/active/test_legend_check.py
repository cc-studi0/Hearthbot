"""
验证脚本：传说段位时盒子是否仍推送出牌建议
1. 强制显示推荐面板（绕过前端隐藏）
2. 拦截 onShowModule 看 C++ 是否发了隐藏指令
3. 拦截 onUpdateLadderActionRecommend 看是否有数据推送
"""
import asyncio
import json
import urllib.request
import websockets

CDP_HOST = "127.0.0.1"
CDP_PORT = 9222


async def get_all_ws_urls():
    """获取所有可调试页面"""
    url = f"http://{CDP_HOST}:{CDP_PORT}/json"
    try:
        with urllib.request.urlopen(url, timeout=5) as resp:
            pages = json.loads(resp.read())
    except Exception as e:
        print(f"[!] 无法连接 CDP: {e}")
        return []

    results = []
    for page in pages:
        ws = page.get("webSocketDebuggerUrl", "")
        page_url = page.get("url", "")
        title = page.get("title", "")
        print(f"  页面: {title} | {page_url[:80]}")
        if ws and ("jipaiqi" in page_url or "ladder" in page_url or "ceframe" in page_url):
            results.append((title, page_url, ws))
    return results


async def inject_and_monitor(title, page_url, ws_url):
    """在指定页面注入钩子并监听"""
    tag = page_url.split("/")[-1].split("?")[0] or "unknown"
    print(f"\n[{tag}] 连接: {ws_url[:60]}...")

    try:
        async with websockets.connect(ws_url, max_size=10 * 1024 * 1024) as ws:
            msg_id = 0

            # 注入全面监听钩子
            msg_id += 1
            hook_js = r"""
            (function() {
                var tag = document.title || location.pathname;
                var log = function(label, data) {
                    console.log('__LEGEND_CHECK__:' + JSON.stringify({
                        tag: tag,
                        label: label,
                        time: new Date().toISOString(),
                        data: typeof data === 'string' ? data.substring(0, 2000) : JSON.stringify(data).substring(0, 2000)
                    }));
                };

                // 1. 拦截 onShowModule - 看C++是否发隐藏推荐面板指令
                var origOnShowModule = window.onShowModule;
                window.onShowModule = function(module, flag) {
                    log('onShowModule', {module: module, flag: flag});
                    if (origOnShowModule) origOnShowModule(module, flag);
                };

                // 2. 拦截 onSwitchModule
                var origOnSwitchModule = window.onSwitchModule;
                window.onSwitchModule = function(module) {
                    log('onSwitchModule', {module: module});
                    if (origOnSwitchModule) origOnSwitchModule(module);
                };

                // 3. 拦截天梯出牌建议
                var origLadder = window.onUpdateLadderActionRecommend;
                window.onUpdateLadderActionRecommend = function(data) {
                    log('onUpdateLadderActionRecommend', data);
                    if (origLadder) origLadder(data);
                };

                // 4. 拦截竞技场出牌建议
                var origArena = window.onUpdateArenaRecommend;
                window.onUpdateArenaRecommend = function(data) {
                    log('onUpdateArenaRecommend', data);
                    if (origArena) origArena(data);
                };

                // 5. 拦截 fRecommend (套牌推荐)
                var origFRecommend = window.fRecommend;
                window.fRecommend = function(hero, guideList, cardStage) {
                    log('fRecommend', {hero: hero, guideList: guideList, cardStage: cardStage});
                    if (origFRecommend) origFRecommend(hero, guideList, cardStage);
                };

                // 6. 拦截 fChangeCards (卡牌变化)
                var origFChangeCards = window.fChangeCards;
                window.fChangeCards = function() {
                    log('fChangeCards', Array.from(arguments));
                    if (origFChangeCards) origFChangeCards.apply(this, arguments);
                };

                // 7. 拦截 highLightAction
                var origHighLight = window.highLightAction;
                window.highLightAction = function(id) {
                    log('highLightAction', {id: id});
                    if (origHighLight) origHighLight(id);
                };

                // 8. 拦截 foldRecommend
                var origFold = window.foldRecommend;
                window.foldRecommend = function() {
                    log('foldRecommend', Array.from(arguments));
                    if (origFold) origFold.apply(this, arguments);
                };

                return 'all hooks installed on: ' + tag;
            })();
            """
            await ws.send(json.dumps({
                "id": msg_id, "method": "Runtime.evaluate",
                "params": {"expression": hook_js}
            }))
            result = json.loads(await ws.recv())
            val = result.get("result", {}).get("result", {}).get("value", "?")
            print(f"[{tag}] 钩子注入: {val}")

            # 启用 console 监听
            msg_id += 1
            await ws.send(json.dumps({"id": msg_id, "method": "Runtime.enable"}))
            await ws.recv()

            # 持续监听
            while True:
                try:
                    msg = json.loads(await ws.recv())
                except websockets.ConnectionClosed:
                    print(f"[{tag}] 连接断开")
                    break

                if msg.get("method") == "Runtime.consoleAPICalled":
                    args = msg.get("params", {}).get("args", [])
                    for arg in args:
                        val = arg.get("value", "")
                        if isinstance(val, str) and val.startswith("__LEGEND_CHECK__:"):
                            payload = json.loads(val[len("__LEGEND_CHECK__:"):])
                            label = payload["label"]
                            time = payload["time"]
                            data = payload["data"]

                            # 高亮关键事件
                            if label == "onUpdateLadderActionRecommend":
                                print(f"\n{'='*60}")
                                print(f"[!!!] 收到出牌建议推送! time={time}")
                                print(f"  数据: {data[:500]}")
                                print(f"{'='*60}")
                            elif label == "onShowModule":
                                d = json.loads(data) if isinstance(data, str) else data
                                print(f"\n[*] onShowModule: module={d.get('module')} flag={d.get('flag')}  time={time}")
                            elif label == "onUpdateArenaRecommend":
                                print(f"\n[!!!] 收到竞技场建议! time={time}")
                                print(f"  数据: {data[:500]}")
                            else:
                                print(f"[{tag}] {label}: {str(data)[:200]}  time={time}")

    except Exception as e:
        print(f"[{tag}] 错误: {e}")


async def main():
    print("=" * 50)
    print("传说段位出牌建议验证工具")
    print("=" * 50)
    print(f"\n[*] 扫描 CDP {CDP_HOST}:{CDP_PORT} 页面...\n")

    pages = await get_all_ws_urls()
    if not pages:
        print("[!] 没有可用页面")
        return

    print(f"\n[+] 找到 {len(pages)} 个目标页面，全部注入监听...")
    print("[*] 现在去炉石里打一局，看是否有数据推送 (Ctrl+C 退出)\n")

    # 并发监听所有页面
    tasks = [inject_and_monitor(t, u, w) for t, u, w in pages]
    await asyncio.gather(*tasks)


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\n[*] 已退出")

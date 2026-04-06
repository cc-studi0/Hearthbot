"""
炉石盒子 出牌建议 实时获取脚本
通过 CDP (Chrome DevTools Protocol) 端口 9222 监听盒子推送的出牌建议数据

数据来源: window.onUpdateLadderActionRecommend(jsonString)
由盒子 C++ 原生程序在每个回合推送给内嵌 CEF 页面

actionName 类型:
  end_turn       - 结束回合
  choice         - 选择卡牌（抉择等）
  play_minion    - 打出随从
  play_special   - 打出法术
  play_weapon    - 打出武器
  play_hero      - 打出英雄牌
  play_location  - 打出地标
  forge          - 锻造
  trade          - 交易
  hero_attack    - 英雄攻击
  minion_attack  - 随从攻击
  hero_skill     - 英雄技能
  titan_power    - 泰坦能力
  location_power - 地标能力

card 对象字段:
  cardId         - 卡牌ID (如 EDR_856)
  cardName       - 卡牌名称
  ZONE_POSITION  - 手牌/场上位置编号
"""
import asyncio
import json
import sys
import websockets

# CDP 连接配置
CDP_HOST = "127.0.0.1"
CDP_PORT = 9222

ACTION_CN = {
    "end_turn": "结束回合",
    "choice": "选择",
    "play_minion": "打出随从",
    "play_special": "打出法术",
    "play_weapon": "打出武器",
    "play_hero": "打出英雄牌",
    "play_location": "打出地标",
    "forge": "锻造",
    "trade": "交易",
    "hero_attack": "英雄攻击",
    "minion_attack": "随从攻击",
    "hero_skill": "英雄技能",
    "titan_power": "泰坦能力",
    "location_power": "地标能力",
}


def format_card(card):
    """格式化卡牌信息"""
    if not card:
        return ""
    if isinstance(card, list):
        return " + ".join(format_card(c) for c in card)
    name = card.get("cardName") or card.get("cardId", "?")
    pos = card.get("ZONE_POSITION", "")
    pos_str = f"[{pos}号位]" if pos else ""
    return f"{pos_str}{name}"


def format_target(action):
    """格式化目标信息"""
    parts = []
    target = action.get("target")
    opp_target = action.get("oppTarget")
    target_hero = action.get("targetHero")
    opp_target_hero = action.get("oppTargetHero")

    cur = target or opp_target or target_hero or opp_target_hero
    if not cur:
        return ""

    side = "对方" if (opp_target or opp_target_hero) else "己方"
    if target_hero or opp_target_hero:
        return f" → 目标: {side}英雄"
    else:
        pos = cur.get("ZONE_POSITION") or cur.get("position", "")
        name = cur.get("cardName") or cur.get("cardId", "")
        return f" → 目标: {side}{pos}号位 {name}"


def display_actions(data):
    """显示出牌建议"""
    if isinstance(data, str):
        # 模拟前端的处理
        data = data.replace("opp-target-hero", "oppTargetHero")
        data = data.replace("opp-target", "oppTarget")
        data = data.replace("target-hero", "targetHero")
        data = json.loads(data)

    status = data.get("status")
    error = data.get("error")

    if status != 2:
        print(f"  [状态异常] status={status}, error={error}")
        return

    actions = data.get("data", [])
    if not actions:
        print("  [无建议]")
        return

    print(f"  ┌─ AI 出牌建议 ({len(actions)} 步) ─────────────")
    for i, a in enumerate(actions, 1):
        action_name = a.get("actionName", "unknown")
        action_cn = ACTION_CN.get(action_name, action_name)
        card_str = format_card(a.get("card"))
        target_str = format_target(a)
        position = a.get("position")
        pos_str = f" → 放置{position}号位" if position else ""
        sub = a.get("subOption")
        sub_str = f" → 选择: {format_card(sub)}" if sub and sub.get("cardId") else ""

        print(f"  │ {i}. [{action_cn}] {card_str}{target_str}{pos_str}{sub_str}")
    print(f"  └────────────────────────────────")


async def get_ws_url():
    """从 CDP 获取可调试页面的 WebSocket URL"""
    import urllib.request
    url = f"http://{CDP_HOST}:{CDP_PORT}/json"
    try:
        with urllib.request.urlopen(url, timeout=5) as resp:
            pages = json.loads(resp.read())
    except Exception as e:
        print(f"[!] 无法连接 CDP 端口 {CDP_PORT}: {e}")
        print("    确保炉石盒子正在运行")
        return None

    # 找 ai-recommend 或 jipaiqi 页面
    target = None
    for page in pages:
        page_url = page.get("url", "")
        title = page.get("title", "")
        ws = page.get("webSocketDebuggerUrl", "")
        print(f"  页面: {title} | {page_url[:80]}")
        if "jipaiqi" in page_url or "ai-recommend" in page_url or "ladder" in page_url:
            target = page
        if not target and ws:
            target = page  # fallback to first debuggable page

    if not target:
        print("[!] 未找到可调试页面")
        return None

    ws_url = target.get("webSocketDebuggerUrl")
    print(f"\n[+] 目标页面: {target.get('title', '?')}")
    print(f"    URL: {target.get('url', '?')[:100]}")
    return ws_url


async def monitor_play_advice():
    """通过 CDP 监听出牌建议"""
    ws_url = await get_ws_url()
    if not ws_url:
        return

    print(f"\n[*] 连接 WebSocket: {ws_url}")
    msg_id = 0

    async with websockets.connect(ws_url, max_size=10 * 1024 * 1024) as ws:
        print("[+] 已连接\n")

        # 方法1: 注入 JS 钩子拦截 onUpdateLadderActionRecommend
        msg_id += 1
        hook_js = """
        (function() {
            // 保存原始函数
            const origLadder = window.onUpdateLadderActionRecommend;
            const origArena = window.onUpdateArenaRecommend;

            // 劫持天梯出牌建议
            window.onUpdateLadderActionRecommend = function(data) {
                console.log('__PLAY_ADVICE_LADDER__:' + data);
                if (origLadder) origLadder(data);
            };

            // 劫持竞技场出牌建议
            window.onUpdateArenaRecommend = function(data) {
                console.log('__PLAY_ADVICE_ARENA__:' + data);
                if (origArena) origArena(data);
            };

            return 'hooks installed';
        })();
        """
        await ws.send(json.dumps({
            "id": msg_id,
            "method": "Runtime.evaluate",
            "params": {"expression": hook_js}
        }))
        result = json.loads(await ws.recv())
        print(f"[+] JS 钩子注入: {result.get('result', {}).get('result', {}).get('value', 'unknown')}")

        # 启用 console 监听
        msg_id += 1
        await ws.send(json.dumps({
            "id": msg_id,
            "method": "Runtime.enable"
        }))
        await ws.recv()

        print("[*] 正在监听出牌建议... (Ctrl+C 退出)\n")

        while True:
            try:
                msg = json.loads(await ws.recv())
            except websockets.ConnectionClosed:
                print("[!] WebSocket 连接断开")
                break

            # 监听 console.log 事件
            if msg.get("method") == "Runtime.consoleAPICalled":
                args = msg.get("params", {}).get("args", [])
                for arg in args:
                    val = arg.get("value", "")
                    if isinstance(val, str):
                        if val.startswith("__PLAY_ADVICE_LADDER__:"):
                            data_str = val[len("__PLAY_ADVICE_LADDER__:"):]
                            print(f"\n[天梯] 收到出牌建议:")
                            try:
                                display_actions(data_str)
                            except Exception as e:
                                print(f"  解析失败: {e}")
                                print(f"  原始数据: {data_str[:500]}")

                        elif val.startswith("__PLAY_ADVICE_ARENA__:"):
                            data_str = val[len("__PLAY_ADVICE_ARENA__:"):]
                            print(f"\n[竞技场] 收到出牌建议:")
                            try:
                                display_actions(data_str)
                            except Exception as e:
                                print(f"  解析失败: {e}")
                                print(f"  原始数据: {data_str[:500]}")


async def main():
    print("=" * 50)
    print("炉石盒子 出牌建议 实时监听工具")
    print("=" * 50)
    print(f"\n[*] 连接 CDP {CDP_HOST}:{CDP_PORT}...")

    try:
        await monitor_play_advice()
    except KeyboardInterrupt:
        print("\n[*] 已退出")
    except Exception as e:
        print(f"\n[!] 错误: {e}")
        import traceback
        traceback.print_exc()


if __name__ == "__main__":
    asyncio.run(main())

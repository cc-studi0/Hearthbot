#!/usr/bin/env python3
"""
HSBox 酒馆战棋数据抓取器
通过 Chrome DevTools Protocol 连接炉石盒子内嵌浏览器，
读取酒馆战棋 (client-wargame/action) 页面的结构化推荐数据。

使用方法: python hsbox_battlegrounds.py
前提: 炉石传说已启动且正在酒馆战棋对局中。
"""

import json
import sys
import time
import urllib.request
import websocket  # pip install websocket-client

CDP_ENDPOINT = "http://127.0.0.1:9222/json/list"
PAGE_URL_KEYWORD = "/client-wargame/action"

# ── 注入浏览器的 JS 脚本（与 C# 端 BuildBgHookScript 保持一致） ──
BG_HOOK_SCRIPT = r"""(() => {
  function normalizeText(text) {
    return (text || '').replace(/\s+/g, ' ').trim();
  }
  function collectRecommendText() {
    const actionRe = /(选择|选取|选|拿|保留|重掷|重投|购买|买入|打出|使用|出售|卖出|升级|冻结|结束回合|英雄技能)/;
    const selectors = [
      '[class*=recommend]',
      '[id*=recommend]',
      '[class*=playstyle]',
      '[class*=strategy]',
      '[class*=guide]',
      '[class*=action]',
      '[class*=choice]',
      '[class*=hero]',
      '[class*=option]'
    ];
    const candidates = [];
    const seen = new Set();
    const pushText = (text) => {
      const normalized = normalizeText(text);
      if (!normalized || normalized.length < 4 || normalized.length > 240) return;
      if (seen.has(normalized)) return;
      seen.add(normalized);
      candidates.push(normalized);
    };
    for (const selector of selectors) {
      for (const node of document.querySelectorAll(selector)) {
        pushText(node.innerText || node.textContent || '');
      }
    }
    for (const node of document.querySelectorAll('body *')) {
      const text = normalizeText(node.innerText || node.textContent || '');
      if (!text || !actionRe.test(text)) continue;
      pushText(text);
    }
    const heroSpecific = candidates.find(text => /(英雄|酒馆英雄|候选英雄)/.test(text) && actionRe.test(text));
    if (heroSpecific) return heroSpecific;
    const actionCandidate = candidates.find(text => actionRe.test(text));
    return actionCandidate || '';
  }
  const response = {
    ok: false,
    count: Number(window.__hbBgCount || 0),
    updatedAt: Number(window.__hbBgUpdatedAt || 0),
    data: window.__hbBgLastData ?? null,
    stationData: window.__hbBgLastStations ?? null,
    stationCount: Number(window.__hbBgStationCount || 0),
    sourceCallback: window.__hbBgLastSource ?? '',
    recommendText: collectRecommendText(),
    bodyText: document.body ? document.body.innerText.slice(0, 1500) : '',
    href: location.href,
    title: document.title ?? ''
  };
  try {
    if (!window.__hbBgHooked) window.__hbBgHooked = {};
    const stationOrig = window.onUpdateBattleStations;
    const pattern = /^onUpdateBattle\w*Recommend$/;
    let foundRecommend = false;
    for (const key of Object.keys(window)) {
      if (!pattern.test(key)) continue;
      if (typeof window[key] !== 'function') continue;
      foundRecommend = true;
      if (window.__hbBgHooked[key]) continue;
      const origKey = '__hbBgOrig_' + key;
      window[origKey] = window[key];
      window[key] = function(e) {
        e = e || '';
        window.__hbBgCount = Number(window.__hbBgCount || 0) + 1;
        window.__hbBgUpdatedAt = Date.now();
        window.__hbBgLastSource = key;
        try {
          window.__hbBgLastData = JSON.parse(e);
        } catch (err) {
          window.__hbBgLastData = { __parseError: String(err), raw: e };
        }
        return window[origKey].apply(this, arguments);
      };
      window.__hbBgHooked[key] = true;
    }
    if (!foundRecommend && typeof stationOrig !== 'function' && !window.__hbBgStationsHooked) {
      response.reason = 'callback_missing';
      return JSON.stringify(response);
    }
    if (typeof stationOrig === 'function' && !window.__hbBgStationsHooked) {
      window.__hbBgStationsOriginal = stationOrig;
      window.onUpdateBattleStations = function(e) {
        e = e || '';
        window.__hbBgStationCount = Number(window.__hbBgStationCount || 0) + 1;
        try {
          window.__hbBgLastStations = JSON.parse(e);
        } catch (err) {
          window.__hbBgLastStations = { __parseError: String(err), raw: e };
        }
        return window.__hbBgStationsOriginal.apply(this, arguments);
      };
      window.__hbBgStationsHooked = true;
    }
    response.ok = true;
    response.count = Number(window.__hbBgCount || 0);
    response.updatedAt = Number(window.__hbBgUpdatedAt || 0);
    response.data = window.__hbBgLastData ?? null;
    response.stationData = window.__hbBgLastStations ?? null;
    response.stationCount = Number(window.__hbBgStationCount || 0);
    response.sourceCallback = window.__hbBgLastSource ?? '';
    response.recommendText = collectRecommendText();
    response.bodyText = document.body ? document.body.innerText.slice(0, 1500) : '';
    response.href = location.href;
    response.title = document.title ?? '';
    response.reason = response.count > 0 || response.stationCount > 0
      ? 'ready'
      : (response.bodyText ? 'body_only' : 'waiting');
    return JSON.stringify(response);
  } catch (error) {
    response.reason = String(error && error.message ? error.message : error);
    return JSON.stringify(response);
  }
})()"""


def get_debugger_url():
    """从 CDP 端点获取酒馆战棋页面的 WebSocket 调试 URL"""
    try:
        req = urllib.request.Request(CDP_ENDPOINT)
        with urllib.request.urlopen(req, timeout=3) as resp:
            targets = json.loads(resp.read().decode("utf-8"))
    except Exception as e:
        print(f"❌ 无法连接 CDP 端点 ({CDP_ENDPOINT}): {e}")
        print("   请确认炉石传说已启动且盒子内嵌浏览器可用。")
        return None

    pages = [t for t in targets if PAGE_URL_KEYWORD in (t.get("url") or "")]
    if not pages:
        print(f"❌ 未找到酒馆战棋页面 (关键词: {PAGE_URL_KEYWORD})")
        print("   当前可用页面:")
        for t in targets:
            print(f"     - {t.get('url', '(无url)')}")
        return None

    target = pages[0]
    ws_url = target.get("webSocketDebuggerUrl")
    if not ws_url:
        print("❌ 目标页面缺少 webSocketDebuggerUrl")
        return None

    print(f"✅ 找到酒馆战棋页面: {target.get('url', '?')}")
    print(f"   WebSocket: {ws_url}")
    return ws_url


def evaluate_on_page(ws_url, expression):
    """通过 CDP WebSocket 在页面上执行 JS 表达式"""
    ws = websocket.create_connection(ws_url, timeout=5)
    try:
        request = json.dumps({
            "id": 1,
            "method": "Runtime.evaluate",
            "params": {
                "expression": expression,
                "returnByValue": True,
                "awaitPromise": True,
            },
        })
        ws.send(request)

        while True:
            msg = ws.recv()
            if not msg:
                continue
            response = json.loads(msg)
            if response.get("id") != 1:
                continue
            result = response.get("result", {}).get("result", {})
            if result.get("type") == "string":
                return result.get("value")
            exc = response.get("result", {}).get("exceptionDetails")
            if exc:
                print(f"⚠️ JS 异常: {exc.get('text', '未知')}")
            return None
    finally:
        ws.close()


# ── 动作名中文映射 ──
ACTION_NAME_MAP = {
    "buy": "🛒 购买",
    "buy_special": "🛒 特殊购买",
    "buy_minion": "🛒 购买随从",
    "sell": "💰 出售",
    "sell_minion": "💰 出售随从",
    "play": "🎯 打出",
    "play_minion": "🎯 打出随从",
    "play_special": "🎯 打出(特殊)",
    "special": "⚡ 特殊",
    "hero_skill": "🦸 英雄技能",
    "upgrade": "⬆️ 升级酒馆",
    "tavern_up": "⬆️ 升级酒馆",
    "refresh": "🔄 刷新商店",
    "reroll": "🔄 刷新商店",
    "reroll_choices": "🔄 刷新选择",
    "freeze": "🧊 冻结",
    "unfreeze": "🧊 解冻",
    "freeze_choices": "🧊 冻结选择",
    "end_turn": "⏭️ 结束回合",
    "choice": "🤔 选择",
    "choose": "🤔 选择",
    "pick": "🤔 选择",
    "select": "🤔 选择",
    "choose_hero": "🦸 选择英雄",
    "change_minion_index": "↔️ 移动随从",
    "common_action": "⚙️ 通用动作",
    "titan_power": "⚡ 泰坦之力",
    "launch_starship": "🚀 发射星舰",
    "discard": "🗑️ 弃置",
}


def format_bg_action_step(step, index):
    """格式化单个战棋推荐动作步骤"""
    lines = []
    action = step.get("actionName", "?")
    action_display = ACTION_NAME_MAP.get(action.lower(), action)
    lines.append(f"  [{index}] {action_display}  (原始: {action})")

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
        t_zone = target.get("zoneName", target.get("zone_name", ""))
        lines.append(f"       目标: {t_card} (位置={t_pos}, 区域={t_zone})")

    opp_target = step.get("oppTarget")
    if opp_target:
        t_card = opp_target.get("cardId", "?")
        t_pos = opp_target.get("zonePosition", opp_target.get("zone_position", "?"))
        lines.append(f"       对手目标: {t_card} (位置={t_pos})")

    target_hero = step.get("targetHero")
    if target_hero:
        lines.append(f"       目标英雄: {target_hero.get('cardId', '?')}")

    opp_target_hero = step.get("oppTargetHero")
    if opp_target_hero:
        lines.append(f"       对手英雄: {opp_target_hero.get('cardId', '?')}")

    sub_option = step.get("subOption")
    if sub_option:
        lines.append(f"       子选项: {sub_option.get('cardId', '?')}")

    position = step.get("position")
    if position:
        lines.append(f"       放置位置: {position}")

    return "\n".join(lines)


def display_state(state_json):
    """以结构化格式输出战棋状态数据"""
    state = json.loads(state_json)
    sep = "═" * 60

    print(f"\n{sep}")
    print("  🍺 HSBox 酒馆战棋 - 结构化数据")
    print(sep)

    # ── 基本信息 ──
    print(f"\n📌 基本信息:")
    print(f"  状态:         {'✅ 正常' if state.get('ok') else '❌ 异常'}")
    print(f"  推荐回调次数: {state.get('count', 0)}")
    print(f"  驿站回调次数: {state.get('stationCount', 0)}")
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
                print(format_bg_action_step(step, i + 1))
        elif envelope_data is not None:
            print(f"  data.data: {json.dumps(envelope_data, ensure_ascii=False, indent=4)}")
        else:
            if "__parseError" in data:
                print(f"  ⚠️ JSON 解析错误: {data['__parseError']}")
                if "raw" in data:
                    raw_preview = str(data["raw"])[:300]
                    print(f"  原始数据(前300字符): {raw_preview}")
            else:
                print(f"  {json.dumps(data, ensure_ascii=False, indent=4)}")
    else:
        print(f"  {json.dumps(data, ensure_ascii=False, indent=4)}")

    # ── 驿站数据 ──
    station_data = state.get("stationData")
    print(f"\n🏠 驿站数据 (stationData):")
    if station_data is None:
        print("  (无)")
    elif isinstance(station_data, dict):
        if "__parseError" in station_data:
            print(f"  ⚠️ JSON 解析错误: {station_data['__parseError']}")
        else:
            station_json = json.dumps(station_data, ensure_ascii=False, indent=4)
            lines = station_json.split("\n")
            if len(lines) > 30:
                print("  " + "\n  ".join(lines[:30]))
                print(f"  ... (共 {len(lines)} 行)")
            else:
                print("  " + "\n  ".join(lines))
    else:
        print(f"  {json.dumps(station_data, ensure_ascii=False, indent=4)}")

    # ── 推荐文本 ──
    recommend_text = state.get("recommendText", "")
    print(f"\n💡 推荐文本 (recommendText):")
    if recommend_text:
        print(f"  {recommend_text}")
    else:
        print("  (空)")

    # ── 页面文本 ──
    body_text = state.get("bodyText", "")
    print(f"\n📄 页面文本 (bodyText):")
    if body_text:
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
    print("  HSBox 酒馆战棋数据抓取器")
    print("  CDP 端点: " + CDP_ENDPOINT)
    print("=" * 60)

    ws_url = get_debugger_url()
    if not ws_url:
        sys.exit(1)

    print("\n⏳ 正在执行 JS 脚本...")
    result = evaluate_on_page(ws_url, BG_HOOK_SCRIPT)
    if not result:
        print("❌ JS 执行返回空结果")
        sys.exit(1)

    display_state(result)


if __name__ == "__main__":
    main()

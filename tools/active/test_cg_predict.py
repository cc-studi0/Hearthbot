"""
炉石盒子 cg_predict API 测试脚本
用途：传入卡牌ID，获取推荐卡组/打法数据
"""
import requests
import json
import sys
import re

API_BASE = "https://hs-api.lushi.163.com"
CARDS_JS_URL = f"{API_BASE}/static/inc/hscards.min.js"

# 职业ID映射
JOB_NAMES = {
    1: "战士", 2: "圣骑士", 3: "猎人", 4: "盗贼", 5: "牧师",
    6: "萨满", 7: "法师", 8: "术士", 9: "德鲁伊", 10: "恶魔猎手",
    11: "死亡骑士", 12: "中立"
}


def load_card_db():
    """加载卡牌数据库（用于ID转中文名）"""
    print("[*] 加载卡牌数据库...")
    try:
        resp = requests.get(CARDS_JS_URL, timeout=10)
        resp.raise_for_status()
        # 格式: var HSCARDS = {...}
        json_str = resp.text.replace("var HSCARDS = ", "", 1)
        if json_str.endswith(";"):
            json_str = json_str[:-1]
        db = json.loads(json_str)
        print(f"[+] 加载了 {len(db)} 张卡牌")
        return db
    except Exception as e:
        print(f"[!] 卡牌数据库加载失败: {e}，将只显示卡牌ID")
        return {}


def cg_predict(card_ids: str):
    """调用 cg_predict 接口"""
    url = f"{API_BASE}/cg/cg_predict"
    params = {"cglist": card_ids}
    print(f"\n[*] 请求: {url}?cglist={card_ids}")
    resp = requests.get(url, params=params, timeout=10)
    resp.raise_for_status()
    return resp.json()


def display_result(data, card_db):
    """格式化显示结果"""
    if data.get("ret") != 0:
        print(f"[!] 请求失败: {data.get('msg')}")
        return

    results = data.get("result", [])
    if not results:
        print("[!] 没有匹配结果")
        return

    print(f"\n{'='*60}")
    print(f"匹配到 {len(results)} 个推荐卡组 (耗时 {data.get('took', '?')}ms)")
    print(f"{'='*60}")

    for i, deck in enumerate(results, 1):
        job = deck.get("job", 0)
        job_name = JOB_NAMES.get(job, f"未知({job})")
        mode = "狂野" if deck.get("iswild") else "标准"
        hot = deck.get("hot", 0)
        md5 = deck.get("md5", "")
        date = deck.get("date", "")

        print(f"\n--- 卡组 {i}: {job_name} [{mode}] 热度:{hot} 日期:{date} ---")
        print(f"    MD5: {md5}")

        # 解析卡牌列表
        new_cg = deck.get("new_cg", "")
        if new_cg:
            cards = new_cg.split(",")
            print(f"    卡牌 ({len(cards)} 种):")
            for card_entry in cards:
                parts = card_entry.split(":")
                card_id = parts[0]
                count = parts[1] if len(parts) > 1 else "1"
                card_info = card_db.get(card_id, {})
                name = card_info.get("name_cn", card_id)
                cost = card_info.get("Cost", "?")
                print(f"      [{cost}费] {name} x{count}  ({card_id})")

    # 输出原始JSON供调试
    print(f"\n{'='*60}")
    print("[DEBUG] 原始JSON响应:")
    print(json.dumps(data, indent=2, ensure_ascii=False))


def main():
    card_db = load_card_db()

    if len(sys.argv) > 1:
        # 命令行传入卡牌ID
        card_ids = sys.argv[1]
    else:
        # 交互模式
        print("\n炉石盒子 cg_predict 测试工具")
        print("输入卡牌ID（逗号分隔），例如: EDR_856,CORE_EX1_002")
        print("输入 q 退出\n")
        while True:
            card_ids = input("卡牌ID> ").strip()
            if card_ids.lower() == "q":
                break
            if not card_ids:
                # 默认测试用例
                card_ids = "EDR_856"
                print(f"[*] 使用默认测试: {card_ids}")
            try:
                data = cg_predict(card_ids)
                display_result(data, card_db)
            except Exception as e:
                print(f"[!] 错误: {e}")
            print()
        return

    data = cg_predict(card_ids)
    display_result(data, card_db)


if __name__ == "__main__":
    main()

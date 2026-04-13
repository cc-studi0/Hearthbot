#!/usr/bin/env python3
"""
炉石传说 UI 元素检测器
通过 BepInEx Payload 管道命令，检测当前游戏页面上的所有 UI 元素。

使用方法: python hs_ui_inspector.py
前提: 炉石传说已启动且 Payload 已注入。

也可以直接通过 CDP 检测盒子 CEF 页面元素（加 --cef 参数）。
"""

import json
import sys
import time
import struct
import urllib.request

# ── 方式1: 通过 CDP 检测 CEF 页面 ──

def inspect_cef():
    """检测所有 CEF 页面的 DOM 元素"""
    import websocket

    try:
        data = json.loads(urllib.request.urlopen("http://127.0.0.1:9222/json/list", timeout=3).read())
    except Exception as e:
        print(f"[ERROR] 无法连接 CDP: {e}")
        return

    print(f"\n找到 {len(data)} 个 CEF 页面:\n")
    for i, target in enumerate(data):
        url = target.get("url", "?")
        title = target.get("title", "?")
        print(f"  [{i}] {url}")
        print(f"      title: {title}")

    print()
    for target in data:
        url = target.get("url", "")
        if "about:blank" in url:
            continue
        ws_url = target.get("webSocketDebuggerUrl")
        if not ws_url:
            continue

        print(f"\n{'='*60}")
        print(f"  {url}")
        print(f"{'='*60}")

        try:
            ws = websocket.create_connection(ws_url, timeout=5)
            js = r"""(() => {
                var r = { buttons: [], inputs: [], links: [], texts: [], images: [] };

                // 按钮
                document.querySelectorAll('button, [role=button], .btn, [class*=button], [class*=Button]').forEach(el => {
                    r.buttons.push({
                        tag: el.tagName,
                        text: (el.innerText || '').trim().substring(0, 50),
                        class: (el.className || '').substring(0, 80),
                        id: el.id || '',
                        visible: el.offsetParent !== null,
                        rect: el.getBoundingClientRect ? {
                            x: Math.round(el.getBoundingClientRect().x),
                            y: Math.round(el.getBoundingClientRect().y),
                            w: Math.round(el.getBoundingClientRect().width),
                            h: Math.round(el.getBoundingClientRect().height)
                        } : null
                    });
                });

                // 文本内容
                document.querySelectorAll('h1, h2, h3, h4, p, span, div').forEach(el => {
                    var text = (el.innerText || '').trim();
                    if (text.length > 3 && text.length < 200 && el.children.length === 0) {
                        r.texts.push({
                            tag: el.tagName,
                            text: text.substring(0, 100),
                            class: (el.className || '').substring(0, 50)
                        });
                    }
                });

                // 只保留前 30 个文本
                r.texts = r.texts.slice(0, 30);

                return JSON.stringify(r);
            })()"""

            msg = json.dumps({"id": 1, "method": "Runtime.evaluate",
                              "params": {"expression": js, "returnByValue": True}})
            ws.send(msg)
            resp = json.loads(ws.recv())
            val = resp.get("result", {}).get("result", {}).get("value", "{}")
            elements = json.loads(val) if isinstance(val, str) else val

            buttons = elements.get("buttons", [])
            texts = elements.get("texts", [])

            if buttons:
                print(f"\n  [按钮] ({len(buttons)} 个)")
                for b in buttons:
                    vis = "✓" if b.get("visible") else "✗"
                    rect = b.get("rect", {})
                    pos = f"({rect.get('x',0)},{rect.get('y',0)} {rect.get('w',0)}x{rect.get('h',0)})" if rect else ""
                    print(f"    {vis} [{b['tag']}] \"{b['text']}\" {pos}")
                    if b.get("class"):
                        print(f"        class={b['class'][:60]}")

            if texts:
                print(f"\n  [文本] ({len(texts)} 个)")
                for t in texts:
                    print(f"    [{t['tag']}] \"{t['text']}\"")

            if not buttons and not texts:
                print("  (无可见元素)")

            ws.close()
        except Exception as e:
            print(f"  [ERROR] {e}")


# ── 方式2: 通过 Payload 管道检测游戏内 UI ──

PIPE_NAME = r"\\.\pipe\HearthbotPipe"

def pipe_send_recv(command, timeout_ms=5000):
    """通过命名管道发送命令并接收响应"""
    import ctypes
    import ctypes.wintypes

    GENERIC_READ = 0x80000000
    GENERIC_WRITE = 0x40000000
    OPEN_EXISTING = 3
    PIPE_READMODE_MESSAGE = 0x00000002
    INVALID_HANDLE_VALUE = ctypes.wintypes.HANDLE(-1).value

    kernel32 = ctypes.windll.kernel32

    handle = kernel32.CreateFileW(
        PIPE_NAME, GENERIC_READ | GENERIC_WRITE, 0, None, OPEN_EXISTING, 0, None)

    if handle == INVALID_HANDLE_VALUE:
        return None

    try:
        # 写入命令
        data = command.encode("utf-8")
        written = ctypes.wintypes.DWORD()
        kernel32.WriteFile(handle, data, len(data), ctypes.byref(written), None)

        # 读取响应
        buf = ctypes.create_string_buffer(65536)
        read = ctypes.wintypes.DWORD()
        ok = kernel32.ReadFile(handle, buf, 65536, ctypes.byref(read), None)
        if ok and read.value > 0:
            return buf.raw[:read.value].decode("utf-8", errors="replace")
        return None
    finally:
        kernel32.CloseHandle(handle)


def inspect_game():
    """通过 Payload 检测游戏内 UI 元素"""

    print("\n" + "=" * 60)
    print("  炉石传说游戏内 UI 检测")
    print("=" * 60)

    # 1. 当前场景
    print("\n[场景]")
    scene = pipe_send_recv("GET_SCENE")
    print(f"  当前场景: {scene}")

    # 2. 大厅按钮
    print("\n[大厅按钮]")
    buttons = pipe_send_recv("GET_HUB_BUTTONS")
    if buttons:
        print(f"  {buttons[:500]}")

    # 3. 阻塞弹窗
    print("\n[阻塞弹窗]")
    dialog = pipe_send_recv("GET_BLOCKING_DIALOG")
    print(f"  {dialog}")

    # 4. 是否在匹配
    print("\n[匹配状态]")
    finding = pipe_send_recv("IS_FINDING")
    print(f"  正在匹配: {finding}")

    # 5. 竞技场状态
    print("\n[竞技场]")
    arena_status = pipe_send_recv("ARENA_GET_STATUS")
    print(f"  竞技场状态: {arena_status}")

    ticket_info = pipe_send_recv("ARENA_GET_TICKET_INFO")
    print(f"  票务信息: {ticket_info}")

    # 6. 竞技场选项
    print("\n[竞技场选项]")
    hero_choices = pipe_send_recv("ARENA_GET_HERO_CHOICES")
    print(f"  英雄选项: {hero_choices}")

    draft_choices = pipe_send_recv("ARENA_GET_DRAFT_CHOICES")
    print(f"  卡牌选项: {draft_choices}")

    # 7. DraftManager dump
    print("\n[DraftManager 详细信息]")
    dump = pipe_send_recv("ARENA_DUMP_DRAFT_MANAGER")
    if dump and dump.startswith("DRAFT_DUMP:"):
        parts = dump[11:].split("|")
        if len(parts) >= 1:
            fields = parts[0].split(";")
            print(f"  字段 ({len(fields)} 个):")
            for f in fields:
                if f.startswith("F:") and "=null" not in f and "=0" not in f and "=False" not in f:
                    print(f"    {f}")
        if len(parts) >= 2:
            props = parts[1].split(";")
            print(f"\n  属性 ({len(props)} 个):")
            for p in props:
                if p.startswith("P:") and "=null" not in p and "=0" not in p and "=False" not in p:
                    print(f"    {p}")
        if len(parts) >= 3:
            methods = parts[2].split(";")
            # 只显示竞技场相关的方法
            arena_methods = [m for m in methods if any(k in m.lower() for k in
                ["draft", "arena", "choice", "slot", "ticket", "find", "reward", "retire"])]
            print(f"\n  竞技场相关方法 ({len(arena_methods)} 个):")
            for m in arena_methods:
                print(f"    {m}")
    else:
        print(f"  {dump}")

    # 8. Seed 探测
    print("\n[对局状态]")
    seed = pipe_send_recv("GET_SEED")
    if seed:
        if len(seed) > 100:
            print(f"  SEED: {seed[:100]}...")
        else:
            print(f"  SEED: {seed}")
    else:
        print("  (无响应)")


def main():
    if "--cef" in sys.argv:
        try:
            import websocket
        except ImportError:
            print("需要安装 websocket-client: pip install websocket-client")
            sys.exit(1)
        inspect_cef()
    elif "--game" in sys.argv:
        inspect_game()
    else:
        print("用法:")
        print("  python hs_ui_inspector.py --game    检测游戏内 UI (通过 Payload 管道)")
        print("  python hs_ui_inspector.py --cef     检测盒子 CEF 页面元素")
        print("  python hs_ui_inspector.py --all     两者都检测")

        if "--all" in sys.argv:
            inspect_game()
            try:
                import websocket
                inspect_cef()
            except ImportError:
                print("\n[跳过 CEF] 需要 pip install websocket-client")


if __name__ == "__main__":
    main()

"""
直接从盒子进程内存读取 AI 推荐数据
不依赖 JS 层，不受段位限制影响

用法: python read_recommend.py
  在对局中运行，实时输出 AI 推荐的 JSON
"""
import ctypes
import ctypes.wintypes as wt
import struct
import subprocess
import sys
import json
import time

kernel32 = ctypes.WinDLL('kernel32')
advapi32 = ctypes.WinDLL('advapi32')


def enable_debug():
    class LUID(ctypes.Structure):
        _fields_ = [("Lo", wt.DWORD), ("Hi", wt.LONG)]
    class LA(ctypes.Structure):
        _fields_ = [("Luid", LUID), ("Attr", wt.DWORD)]
    class TP(ctypes.Structure):
        _fields_ = [("Count", wt.DWORD), ("Privs", LA * 1)]
    tok = wt.HANDLE()
    advapi32.OpenProcessToken(kernel32.GetCurrentProcess(), 0x28, ctypes.byref(tok))
    luid = LUID()
    advapi32.LookupPrivilegeValueW(None, "SeDebugPrivilege", ctypes.byref(luid))
    tp = TP(); tp.Count = 1; tp.Privs[0].Luid = luid; tp.Privs[0].Attr = 2
    advapi32.AdjustTokenPrivileges(tok, False, ctypes.byref(tp), 0, None, None)
    kernel32.CloseHandle(tok)


class MBI(ctypes.Structure):
    _fields_ = [
        ('BaseAddress', ctypes.c_void_p), ('AllocationBase', ctypes.c_void_p),
        ('AllocationProtect', wt.DWORD), ('RegionSize', ctypes.c_size_t),
        ('State', wt.DWORD), ('Protect', wt.DWORD), ('Type', wt.DWORD),
    ]


def read_mem(h, addr, size):
    buf = ctypes.create_string_buffer(size)
    br = ctypes.c_size_t()
    ok = kernel32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, size, ctypes.byref(br))
    return buf.raw[:br.value] if ok and br.value > 0 else None


def scan_for_recommend(h):
    """扫描盒子堆内存，找到最新的 AI 推荐 JSON"""
    results = []
    # 搜索紧凑 JSON 格式: {"choiceId":
    pattern = b'{"choiceId":'

    addr = 0x10000
    while addr < 0x7FFF0000:
        mbi = MBI()
        ret = kernel32.VirtualQueryEx(h, ctypes.c_void_p(addr), ctypes.byref(mbi), ctypes.sizeof(mbi))
        if ret == 0 or mbi.BaseAddress is None:
            addr += 0x1000
            continue
        base = mbi.BaseAddress or addr
        size = mbi.RegionSize or 0x1000

        if mbi.State == 0x1000 and 0 < size < 0x4000000:
            data = read_mem(h, base, size)
            if data:
                pos = 0
                while pos < len(data) - len(pattern):
                    pos = data.find(pattern, pos)
                    if pos < 0:
                        break

                    # 读到 \0 或 } 结束
                    end = pos
                    brace_depth = 0
                    while end < len(data) and end - pos < 4000:
                        c = data[end]
                        if c == 0:
                            break
                        if c == ord('{'):
                            brace_depth += 1
                        elif c == ord('}'):
                            brace_depth -= 1
                            if brace_depth == 0:
                                end += 1
                                break
                        end += 1

                    raw = data[pos:end]
                    try:
                        text = raw.decode('utf-8')
                        obj = json.loads(text)
                        # 验证是有效的推荐数据
                        if 'data' in obj and 'status' in obj and 'turnNum' in obj:
                            results.append({
                                'addr': base + pos,
                                'json': obj,
                                'raw_len': len(raw),
                            })
                    except:
                        pass

                    pos += max(len(raw), len(pattern))

        addr = base + max(size, 0x1000)

    return results


def main():
    enable_debug()

    r = subprocess.run(['powershell', '-Command', '(Get-Process HSAng -EA SilentlyContinue).Id'],
                       capture_output=True, text=True)
    try:
        pid = int(r.stdout.strip())
    except:
        print("[!] HSAng 未运行")
        return

    h = kernel32.OpenProcess(0x0410, False, pid)
    if not h:
        print("[!] 无法打开进程")
        return

    print(f"[+] HSAng PID={pid}")
    print(f"[*] 实时监控推荐数据 (Ctrl+C 退出)\n")

    last_seen = {}  # key = (optionId, turnNum) -> 去重

    try:
        while True:
            results = scan_for_recommend(h)

            # 按 turnNum 和 optionId 去重，只显示新的
            for r in results:
                obj = r['json']
                key = (obj.get('optionId', -1), obj.get('turnNum', -1), obj.get('choiceId', -1))

                if key not in last_seen:
                    last_seen[key] = True
                    turn = obj.get('turnNum', '?')
                    opt = obj.get('optionId', '?')
                    choice = obj.get('choiceId', -1)
                    actions = obj.get('data', [])

                    print(f"回合{turn} 选项{opt}" + (f" 选择{choice}" if choice >= 0 else ""))
                    for a in actions:
                        if not isinstance(a, dict):
                            continue
                        name = a.get('actionName', '?')
                        card = a.get('card', {})
                        if not isinstance(card, dict):
                            card = {}
                        card_name = card.get('cardName', '')
                        card_pos = card.get('ZONE_POSITION', '')
                        target = a.get('target') or a.get('opp-target-hero') or {}
                        if not isinstance(target, dict):
                            target = {}
                        target_name = target.get('cardName', '')
                        pos = a.get('position', '')

                        desc = f"  → {name}"
                        if card_name:
                            desc += f" [{card_name}]"
                        if card_pos:
                            desc += f" (位置{card_pos})"
                        if pos:
                            desc += f" 放{pos}号位"
                        if target_name:
                            desc += f" → {target_name}"
                        print(desc)
                    print()

            time.sleep(1)
    except KeyboardInterrupt:
        pass

    kernel32.CloseHandle(h)
    print("\n[*] 已退出")


if __name__ == '__main__':
    main()

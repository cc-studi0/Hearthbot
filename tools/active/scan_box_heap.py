"""扫描 HSAng.exe 堆内存，搜索 AI 推荐的 JSON 数据"""
import ctypes, ctypes.wintypes as wt, struct, subprocess, sys, json

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
    _fields_ = [('BaseAddress', ctypes.c_void_p), ('AllocationBase', ctypes.c_void_p),
                ('AllocationProtect', wt.DWORD), ('RegionSize', ctypes.c_size_t),
                ('State', wt.DWORD), ('Protect', wt.DWORD), ('Type', wt.DWORD)]

def read_mem(h, addr, size):
    buf = ctypes.create_string_buffer(size)
    br = ctypes.c_size_t()
    ok = kernel32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, size, ctypes.byref(br))
    return buf.raw[:br.value] if ok and br.value > 0 else None

enable_debug()
r = subprocess.run(['powershell', '-Command', '(Get-Process HSAng -EA SilentlyContinue).Id'],
                   capture_output=True, text=True)
pid = int(r.stdout.strip())
h = kernel32.OpenProcess(0x0410, False, pid)
print(f"HSAng PID={pid}")

patterns = [
    b'"actionName"',
    b'"choiceId"',
    b'"turnNum"',
    b'"optionId"',
    b'play_minion',
    b'play_special',
    b'end_turn',
    b'minion_attack',
    b'hero_attack',
]

print(f"搜索 {len(patterns)} 个模式...")
found = []

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
            for pat in patterns:
                pos = 0
                while pos < len(data) - len(pat):
                    pos = data.find(pat, pos)
                    if pos < 0:
                        break
                    abs_addr = base + pos

                    # 向前找 { 开头
                    json_start = pos
                    for back in range(1, 500):
                        if pos - back < 0:
                            break
                        if data[pos - back] == ord('{'):
                            json_start = pos - back
                            break

                    # 读到 \0 或长度限制
                    end = json_start
                    while end < len(data) and data[end] != 0 and end - json_start < 4000:
                        end += 1

                    content = data[json_start:end].decode('utf-8', errors='replace')
                    if len(content) > 30:
                        found.append({
                            'addr': f"0x{abs_addr:08X}",
                            'pattern': pat.decode(),
                            'len': len(content),
                            'preview': content[:500]
                        })

                    pos += len(pat)

    addr = base + max(size, 0x1000)

kernel32.CloseHandle(h)

# 去重
seen = set()
unique = []
for f in found:
    key = f['preview'][:100]
    if key not in seen:
        seen.add(key)
        unique.append(f)

print(f"\n找到 {len(unique)} 个唯一匹配:\n")
for f in unique:
    print(f"地址={f['addr']} 模式={f['pattern']} 长度={f['len']}")
    print(f"  {f['preview'][:300]}")
    print()

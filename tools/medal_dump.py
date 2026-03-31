"""快速 dump: 搜索 65278 并打印周围内存"""
import ctypes, ctypes.wintypes as wt, struct, subprocess

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
r = subprocess.run(['powershell', '-Command', '(Get-Process Hearthstone -EA SilentlyContinue).Id'], capture_output=True, text=True)
pid = int(r.stdout.strip())
h = kernel32.OpenProcess(0x0438, False, pid)
print(f"Hearthstone PID={pid}")

# 搜索 65278
TARGET = 65278
pattern = struct.pack('<i', TARGET)
results = []
addr = 0x10000
while addr < 0x7FFF0000:
    mbi = MBI()
    ret = kernel32.VirtualQueryEx(h, ctypes.c_void_p(addr), ctypes.byref(mbi), ctypes.sizeof(mbi))
    if ret == 0 or mbi.BaseAddress is None:
        addr += 0x1000; continue
    base = mbi.BaseAddress or addr
    size = mbi.RegionSize or 0x1000
    if mbi.State == 0x1000 and 0 < size < 0x10000000:
        data = read_mem(h, base, size)
        if data:
            off = 0
            while True:
                off = data.find(pattern, off)
                if off < 0: break
                results.append(base + off)
                off += 1
    addr = base + max(size, 0x1000)

print(f"找到 {len(results)} 个 {TARGET}")
print()

# dump 每个匹配的上下文
for i, a in enumerate(results[:30]):
    ctx = read_mem(h, a - 64, 192)
    if not ctx: continue

    print(f"── 匹配 #{i+1} @ 0x{a:08X} ──")
    for row in range(0, len(ctx), 16):
        hex_vals = []
        int_vals = []
        ascii_str = ""
        for col in range(0, 16, 4):
            pos = row + col
            if pos + 4 <= len(ctx):
                v = struct.unpack_from('<i', ctx, pos)[0]
                raw = ctx[pos:pos+4]
                hex_vals.append(f"{v:>11}")
                for b in raw:
                    ascii_str += chr(b) if 0x20 <= b <= 0x7E else "."

        abs_addr = a - 64 + row
        marker = "  ◀◀◀ 65278" if (a - 64 + row <= a < a - 64 + row + 16) else ""
        print(f"  0x{abs_addr:08X}: {''.join(hex_vals)}  {ascii_str}{marker}")
    print()

kernel32.CloseHandle(h)

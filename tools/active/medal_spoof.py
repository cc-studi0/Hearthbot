"""
Medal Spoofer v5 - 精确定位并修改炉石盒子中的段位数据

用已知段位值精确搜索:
  标准: 钻石5 (leagueId=1, starLevel=5)
  狂野: 传说65278 (leagueId=0, starLevel=11, legendRank=65278)
"""
import ctypes
import ctypes.wintypes as wt
import struct
import subprocess
import sys

kernel32 = ctypes.WinDLL('kernel32')
psapi = ctypes.WinDLL('psapi')
advapi32 = ctypes.WinDLL('advapi32')

MEM_COMMIT = 0x1000


def enable_debug_privilege():
    class LUID(ctypes.Structure):
        _fields_ = [("LowPart", wt.DWORD), ("HighPart", wt.LONG)]
    class LUID_AND_ATTRIBUTES(ctypes.Structure):
        _fields_ = [("Luid", LUID), ("Attributes", wt.DWORD)]
    class TOKEN_PRIVILEGES(ctypes.Structure):
        _fields_ = [("PrivilegeCount", wt.DWORD), ("Privileges", LUID_AND_ATTRIBUTES * 1)]
    hToken = wt.HANDLE()
    advapi32.OpenProcessToken(kernel32.GetCurrentProcess(), 0x0028, ctypes.byref(hToken))
    luid = LUID()
    advapi32.LookupPrivilegeValueW(None, "SeDebugPrivilege", ctypes.byref(luid))
    tp = TOKEN_PRIVILEGES()
    tp.PrivilegeCount = 1
    tp.Privileges[0].Luid = luid
    tp.Privileges[0].Attributes = 2
    advapi32.AdjustTokenPrivileges(hToken, False, ctypes.byref(tp), 0, None, None)
    kernel32.CloseHandle(hToken)


def get_pid(name):
    r = subprocess.run(['powershell', '-Command', f'(Get-Process {name} -EA SilentlyContinue).Id'],
                       capture_output=True, text=True)
    try:
        return int(r.stdout.strip())
    except:
        return None


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


def read_i32(h, addr):
    d = read_mem(h, addr, 4)
    return struct.unpack('<i', d)[0] if d and len(d) == 4 else None


def write_i32(h, addr, value):
    data = struct.pack('<i', value)
    bw = ctypes.c_size_t()
    buf = ctypes.create_string_buffer(data)
    ok = kernel32.WriteProcessMemory(h, ctypes.c_void_p(addr), buf, 4, ctypes.byref(bw))
    if not ok:
        old = wt.DWORD()
        kernel32.VirtualProtectEx(h, ctypes.c_void_p(addr), 4, 0x40, ctypes.byref(old))
        ok = kernel32.WriteProcessMemory(h, ctypes.c_void_p(addr), buf, 4, ctypes.byref(bw))
        kernel32.VirtualProtectEx(h, ctypes.c_void_p(addr), 4, old.value, ctypes.byref(old))
    return ok


def scan_i32(h, value):
    """扫描所有 int32 = value 的地址 (4字节对齐)"""
    pattern = struct.pack('<i', value)
    results = []
    addr = 0x10000
    while addr < 0x7FFF0000:
        mbi = MBI()
        ret = kernel32.VirtualQueryEx(h, ctypes.c_void_p(addr), ctypes.byref(mbi), ctypes.sizeof(mbi))
        if ret == 0 or mbi.BaseAddress is None:
            addr += 0x1000
            continue
        base = mbi.BaseAddress if mbi.BaseAddress else addr
        size = mbi.RegionSize if mbi.RegionSize else 0x1000
        if mbi.State == MEM_COMMIT and 0 < size < 0x10000000:
            data = read_mem(h, base, size)
            if data:
                for off in range(0, len(data) - 3, 4):
                    if data[off:off + 4] == pattern:
                        results.append(base + off)
        addr = base + max(size, 0x1000)
    return results


def dump_context(h, addr, before=32, after=48):
    """打印地址周围的内存"""
    base = addr - before
    data = read_mem(h, base, before + after)
    if not data:
        return
    for row in range(0, len(data), 16):
        vals = []
        for col in range(0, 16, 4):
            pos = row + col
            if pos + 4 <= len(data):
                v = struct.unpack_from('<i', data, pos)[0]
                abs_addr = base + pos
                marker = ""
                if abs_addr == addr:
                    marker = " ◀◀◀"
                vals.append(f"{v:>11}{marker}")
        print(f"    0x{base + row:08X}: {''.join(vals)}")


def main():
    print("=" * 60)
    print("  炉石盒子段位伪装工具 v5 - 精确定位")
    print("=" * 60)

    enable_debug_privilege()

    pid = get_pid('HSAng')
    if not pid:
        print("[!] HSAng 未运行")
        return
    print(f"[+] HSAng PID: {pid}")

    h = kernel32.OpenProcess(0x0438, False, pid)  # VM_READ|WRITE|OPERATION|QUERY
    if not h:
        print(f"[!] OpenProcess 失败: {ctypes.GetLastError()}")
        return

    # ═══════════════════════════════════════════════
    # 第一步: 搜索狂野传说排名 65278 (极其唯一的值)
    # ═══════════════════════════════════════════════
    WILD_LEGEND_RANK = 65278

    print(f"\n[1] 搜索狂野传说排名 {WILD_LEGEND_RANK}...")
    addrs = scan_i32(h, WILD_LEGEND_RANK)
    print(f"    找到 {len(addrs)} 个匹配")

    wild_medal_locations = []

    for a in addrs:
        # 在 legendRank 周围搜索 leagueId=0 和 starLevel=11
        ctx = read_mem(h, a - 64, 128)
        if not ctx:
            continue

        for off in range(0, 124, 4):
            v = struct.unpack_from('<i', ctx, off)[0]
            if v != 11:
                continue
            star_abs = (a - 64) + off
            # starLevel=11 找到了, 看附近有没有 leagueId=0
            for off2 in range(0, 124, 4):
                v2 = struct.unpack_from('<i', ctx, off2)[0]
                if v2 == 0:
                    league_abs = (a - 64) + off2
                    # 三个值应该相距不远
                    spread = max(a, star_abs, league_abs) - min(a, star_abs, league_abs)
                    if spread <= 32:
                        wild_medal_locations.append({
                            'legend_addr': a,
                            'star_addr': star_abs,
                            'league_addr': league_abs,
                            'legend_val': WILD_LEGEND_RANK,
                            'star_val': 11,
                            'league_val': 0,
                        })

    # 去重
    seen = set()
    unique_wild = []
    for loc in wild_medal_locations:
        key = (loc['legend_addr'], loc['star_addr'], loc['league_addr'])
        if key not in seen:
            seen.add(key)
            unique_wild.append(loc)

    print(f"    筛选出 {len(unique_wild)} 个狂野段位数据候选")

    for i, loc in enumerate(unique_wild):
        print(f"\n  狂野候选 #{i + 1}:")
        print(f"    legendRank @ 0x{loc['legend_addr']:08X} = {loc['legend_val']}")
        print(f"    starLevel  @ 0x{loc['star_addr']:08X} = {loc['star_val']}")
        print(f"    leagueId   @ 0x{loc['league_addr']:08X} = {loc['league_val']} (传说)")
        print(f"    上下文:")
        dump_context(h, loc['legend_addr'])

    # ═══════════════════════════════════════════════
    # 第二步: 搜索标准钻石5 (leagueId=1 + starLevel=5)
    # 用 65278 的地址附近来缩小范围
    # ═══════════════════════════════════════════════
    print(f"\n[2] 搜索标准模式钻石5数据...")

    # 如果找到了狂野数据，标准数据应该在附近
    std_candidates = []
    if unique_wild:
        for wloc in unique_wild:
            base_region = wloc['legend_addr'] & 0xFFFFF000  # 同一页
            # 在 ±4KB 范围搜索 leagueId=1 + starLevel=5 的组合
            ctx = read_mem(h, base_region - 4096, 12288)
            if not ctx:
                continue
            for off in range(0, len(ctx) - 8, 4):
                v1 = struct.unpack_from('<i', ctx, off)[0]
                v2 = struct.unpack_from('<i', ctx, off + 4)[0]
                # leagueId=1 紧跟 starLevel=5, 或反过来
                if (v1 == 1 and v2 == 5) or (v1 == 5 and v2 == 1):
                    abs_addr = base_region - 4096 + off
                    std_candidates.append(abs_addr)
                # 也可能间隔几个字段
                if off + 16 <= len(ctx):
                    v3 = struct.unpack_from('<i', ctx, off + 8)[0]
                    v4 = struct.unpack_from('<i', ctx, off + 12)[0]
                    if v1 == 1 and v3 == 5:
                        std_candidates.append(base_region - 4096 + off)
                    if v1 == 5 and v3 == 1:
                        std_candidates.append(base_region - 4096 + off)

    if std_candidates:
        print(f"    在狂野数据附近找到 {len(std_candidates)} 个标准模式候选")
        for i, a in enumerate(std_candidates[:10]):
            print(f"\n  标准候选 #{i + 1} @ 0x{a:08X}:")
            dump_context(h, a)

    # ═══════════════════════════════════════════════
    # 第三步: 交互式修改
    # ═══════════════════════════════════════════════
    if unique_wild:
        print(f"\n{'=' * 60}")
        print(f"找到 {len(unique_wild)} 个狂野段位候选")
        print(f"修改 leagueId: 0(传说) → 1(钻石) 可以骗过盒子")
        print(f"\n输入狂野候选编号 (1-{len(unique_wild)}) 修改, a=全部修改, q=退出:")
        try:
            choice = input("> ").strip().lower()
            targets = []
            if choice == 'a':
                targets = list(range(len(unique_wild)))
            elif choice.isdigit():
                idx = int(choice) - 1
                if 0 <= idx < len(unique_wild):
                    targets = [idx]

            for idx in targets:
                loc = unique_wild[idx]
                addr = loc['league_addr']
                print(f"\n[*] 修改 0x{addr:08X}: leagueId 0→1 (传说→钻石)")
                old = read_i32(h, addr)
                if write_i32(h, addr, 1):
                    new = read_i32(h, addr)
                    print(f"    修改前: {old}, 修改后: {new}")
                    if new == 1:
                        print(f"    ✓ 成功!")
                    else:
                        print(f"    ✗ 验证失败")
                else:
                    print(f"    ✗ 写入失败")

            if targets:
                print(f"\n[+] 修改完成，盒子应该会把你识别为钻石段位")
                print(f"[*] 注意: 进入下一局对战或盒子刷新数据后可能被覆盖")
                print(f"[*] 如果被覆盖，重新运行本工具即可")

        except (EOFError, KeyboardInterrupt):
            pass

    kernel32.CloseHandle(h)
    print("\n[*] 完成")


if __name__ == '__main__':
    main()

import re

with open(r'h:\桌面\炉石脚本\smartbot\smartbot\Temp\SB.dll', 'rb') as f:
    data = f.read()

# Search for Unicode strings containing "mono" or "Mono"
for m in re.finditer(b'[mM][oO][nN][oO]', data):
    start = max(0, m.start() - 20)
    end = min(len(data), m.end() + 40)
    ctx = data[start:end]
    printable = ''.join(chr(b) if 32 <= b < 127 else '.' for b in ctx)
    print(f'  0x{m.start():06X}: {printable}')

print('\n--- VirtualProtect context ---')
for m in re.finditer(b'VirtualProtect', data):
    start = max(0, m.start() - 60)
    end = min(len(data), m.end() + 60)
    ctx = data[start:end]
    printable = ''.join(chr(b) if 32 <= b < 127 else '.' for b in ctx)
    print(f'  0x{m.start():06X}: {printable}')

print('\n--- Process/memory API strings ---')
for pattern in [b'OpenProcess', b'WriteProcessMemory', b'ReadProcessMemory',
                b'CreateRemoteThread', b'NtWriteVirtualMemory',
                b'mono_image', b'mono_domain', b'mono_assembly',
                b'mono_runtime', b'mono_thread', b'mono_jit']:
    for m in re.finditer(pattern, data):
        start = max(0, m.start() - 10)
        end = min(len(data), m.end() + 30)
        ctx = data[start:end]
        printable = ''.join(chr(b) if 32 <= b < 127 else '.' for b in ctx)
        print(f'  0x{m.start():06X} [{pattern.decode()}]: {printable}')

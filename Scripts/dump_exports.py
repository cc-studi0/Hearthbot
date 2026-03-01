import struct

with open(r'h:\桌面\炉石脚本\Libs\Loader.dll', 'rb') as f:
    data = f.read()

pe = struct.unpack_from('<I', data, 0x3C)[0]
opt = pe + 24
export_rva = struct.unpack_from('<I', data, opt + 96)[0]
export_size = struct.unpack_from('<I', data, opt + 100)[0]
print(f'Export RVA: 0x{export_rva:X}, size: {export_size}')

num_sec = struct.unpack_from('<H', data, pe + 6)[0]
opt_size = struct.unpack_from('<H', data, pe + 20)[0]
sec_off = pe + 24 + opt_size

def rva2off(rva):
    for i in range(num_sec):
        s = sec_off + i * 40
        va = struct.unpack_from('<I', data, s + 12)[0]
        vs = struct.unpack_from('<I', data, s + 8)[0]
        rs = struct.unpack_from('<I', data, s + 16)[0]
        rp = struct.unpack_from('<I', data, s + 20)[0]
        if va <= rva < va + max(vs, rs):
            return rva - va + rp
    return rva

eo = rva2off(export_rva)
num_funcs = struct.unpack_from('<I', data, eo + 20)[0]
num_names = struct.unpack_from('<I', data, eo + 24)[0]
names_rva = struct.unpack_from('<I', data, eo + 32)[0]
print(f'Functions: {num_funcs}, Names: {num_names}')

no = rva2off(names_rva)
for i in range(num_names):
    name_rva = struct.unpack_from('<I', data, no + i * 4)[0]
    name_off = rva2off(name_rva)
    name = b''
    while data[name_off] != 0:
        name += bytes([data[name_off]])
        name_off += 1
    print(f'  [{i}] {name.decode()}')

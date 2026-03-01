import struct, sys

def dump_pe(path, label):
    with open(path, 'rb') as f:
        data = f.read()
    print(f"=== {label} ({len(data)} bytes) ===")
    pe_off = struct.unpack_from('<I', data, 0x3C)[0]
    print(f"PE offset: 0x{pe_off:X}")

    machine = struct.unpack_from('<H', data, pe_off+4)[0]
    num_sec = struct.unpack_from('<H', data, pe_off+6)[0]
    opt_hdr_size = struct.unpack_from('<H', data, pe_off+20)[0]
    chars = struct.unpack_from('<H', data, pe_off+22)[0]
    print(f"Machine: 0x{machine:04X}, Sections: {num_sec}, Chars: 0x{chars:04X}")

    opt_off = pe_off + 24
    magic = struct.unpack_from('<H', data, opt_off)[0]
    print(f"PE magic: 0x{magic:04X}")

    cli_rva = struct.unpack_from('<I', data, opt_off + 208)[0]
    cli_size = struct.unpack_from('<I', data, opt_off + 212)[0]
    print(f"CLI RVA: 0x{cli_rva:X}, size: {cli_size}")

    sec_off = opt_off + opt_hdr_size
    def rva2off(rva):
        for i in range(num_sec):
            s = sec_off + i*40
            vs = struct.unpack_from('<I', data, s+8)[0]
            va = struct.unpack_from('<I', data, s+12)[0]
            rs = struct.unpack_from('<I', data, s+16)[0]
            rp = struct.unpack_from('<I', data, s+20)[0]
            if va <= rva < va + max(vs, rs):
                return rva - va + rp
        return rva

    cli_off = rva2off(cli_rva)
    cb = struct.unpack_from('<I', data, cli_off)[0]
    major = struct.unpack_from('<H', data, cli_off+4)[0]
    minor = struct.unpack_from('<H', data, cli_off+6)[0]
    meta_rva = struct.unpack_from('<I', data, cli_off+8)[0]
    meta_size = struct.unpack_from('<I', data, cli_off+12)[0]
    flags = struct.unpack_from('<I', data, cli_off+16)[0]
    entry_tok = struct.unpack_from('<I', data, cli_off+20)[0]
    print(f"CLI cb:{cb} ver:{major}.{minor} flags:0x{flags:08X} entry:0x{entry_tok:08X}")
    print(f"Meta RVA:0x{meta_rva:X} size:{meta_size}")

    meta_off = rva2off(meta_rva)
    meta_sig = struct.unpack_from('<I', data, meta_off)[0]
    ver_len = struct.unpack_from('<I', data, meta_off+12)[0]
    ver_str = data[meta_off+16:meta_off+16+ver_len].rstrip(b'\x00').decode()
    print(f"Meta sig:0x{meta_sig:08X} version:'{ver_str}'")

    # Streams
    flags_off = meta_off + 16 + ver_len
    num_streams = struct.unpack_from('<H', data, flags_off + 2)[0]
    print(f"Streams: {num_streams}")
    pos = flags_off + 4
    for _ in range(num_streams):
        s_off = struct.unpack_from('<I', data, pos)[0]
        s_size = struct.unpack_from('<I', data, pos+4)[0]
        pos += 8
        name = b''
        while data[pos] != 0:
            name += bytes([data[pos]])
            pos += 1
        pos += 1
        while pos % 4 != 0:
            pos += 1
        print(f"  Stream '{name.decode()}': off=0x{s_off:X} size={s_size}")
    print()

import os
base = os.path.dirname(os.path.abspath(__file__))
dump_pe(os.path.join(base, 'HearthstonePayload', 'bin', 'Release', 'net472', 'HearthstonePayload.dll'), 'HearthstonePayload')
dump_pe(os.path.join(base, 'smartbot', 'smartbot', 'Temp', 'SBNet.dll'), 'SBNet')

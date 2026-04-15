// 扫描 HSAng.exe 内存中的 FT_STANDARD，替换为 FT_WILD
// 仅操作盒子进程，绝不碰游戏进程

var REFRESH_INTERVAL_MS = 30000;

function patchOnce() {
    var patched = 0;
    var found = 0;
    var ranges = Process.enumerateRanges("rw-");

    for (var ri = 0; ri < ranges.length; ri++) {
        var range = ranges[ri];
        if (range.size > 100 * 1024 * 1024) continue;

        try {
            // ASCII: FT_STANDARD
            var matches = Memory.scanSync(range.base, range.size,
                "46 54 5f 53 54 41 4e 44 41 52 44");
            for (var mi = 0; mi < matches.length; mi++) {
                found++;
                var addr = matches[mi].address;
                try {
                    Memory.protect(addr, 11, "rwx");
                    addr.writeUtf8String("FT_WILD");
                    addr.add(7).writeU8(0);
                    addr.add(8).writeU8(0);
                    addr.add(9).writeU8(0);
                    addr.add(10).writeU8(0);
                    patched++;
                } catch(e) {}
            }

            // UTF-16LE: FT_STANDARD
            var matches16 = Memory.scanSync(range.base, range.size,
                "46 00 54 00 5f 00 53 00 54 00 41 00 4e 00 44 00 41 00 52 00 44 00");
            for (var mi2 = 0; mi2 < matches16.length; mi2++) {
                found++;
                var addr2 = matches16[mi2].address;
                try {
                    Memory.protect(addr2, 22, "rwx");
                    var wild16 = [
                        0x46,0x00, 0x54,0x00, 0x5f,0x00, 0x57,0x00,
                        0x49,0x00, 0x4c,0x00, 0x44,0x00, 0x00,0x00,
                        0x00,0x00, 0x00,0x00, 0x00,0x00
                    ];
                    addr2.writeByteArray(wild16);
                    patched++;
                } catch(e) {}
            }
        } catch(e) {}
    }

    return { found: found, patched: patched };
}

// 首次补丁
var result = patchOnce();
console.log("[patch] found=" + result.found + " patched=" + result.patched);

// 定时刷新，防止盒子内部重新写入
setInterval(function() {
    var r = patchOnce();
    if (r.patched > 0) {
        console.log("[refresh] found=" + r.found + " patched=" + r.patched);
    }
}, REFRESH_INTERVAL_MS);

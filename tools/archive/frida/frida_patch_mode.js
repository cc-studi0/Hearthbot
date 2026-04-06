// Patch HSAng.exe memory: FT_STANDARD -> FT_WILD
// Only patches within HSAng.exe's writable memory, not the game process

console.log("[*] Scanning for FT_STANDARD in HSAng.exe memory...");

var ranges = Process.enumerateRanges("rw-");
var patched = 0;
var found = 0;

// FT_STANDARD in ASCII: 46 54 5f 53 54 41 4e 44 41 52 44
// FT_WILD in ASCII:     46 54 5f 57 49 4c 44

// Also check UTF-16LE versions
// FT_STANDARD UTF16: 46 00 54 00 5f 00 53 00 54 00 41 00 4e 00 44 00 41 00 52 00 44 00
// FT_WILD UTF16:     46 00 54 00 5f 00 57 00 49 00 4c 00 44 00

for (var ri = 0; ri < ranges.length; ri++) {
    var range = ranges[ri];
    if (range.size > 100 * 1024 * 1024) continue;

    try {
        // ASCII version
        var matches = Memory.scanSync(range.base, range.size,
            "46 54 5f 53 54 41 4e 44 41 52 44");
        for (var mi = 0; mi < matches.length; mi++) {
            found++;
            var addr = matches[mi].address;
            // Read surrounding context to verify
            var before = "";
            try {
                var bytes = addr.sub(20).readByteArray(60);
                var arr = new Uint8Array(bytes);
                for (var i = 0; i < arr.length; i++) {
                    if (arr[i] >= 32 && arr[i] < 127) before += String.fromCharCode(arr[i]);
                    else before += ".";
                }
            } catch(e) {}
            console.log("[FOUND ASCII] " + addr + " context: " + before);

            // Patch: write "FT_WILD" + null padding to same length
            // FT_STANDARD = 11 chars, FT_WILD = 7 chars, pad with nulls
            try {
                Memory.protect(addr, 11, "rwx");
                addr.writeUtf8String("FT_WILD");
                // Zero out remaining bytes (ANDARD -> \0\0\0\0)
                addr.add(7).writeU8(0);
                addr.add(8).writeU8(0);
                addr.add(9).writeU8(0);
                addr.add(10).writeU8(0);
                console.log("[PATCHED] " + addr + " FT_STANDARD -> FT_WILD");
                patched++;
            } catch(e) {
                console.log("[SKIP] " + addr + " cannot write: " + e);
            }
        }

        // UTF-16LE version
        var matches16 = Memory.scanSync(range.base, range.size,
            "46 00 54 00 5f 00 53 00 54 00 41 00 4e 00 44 00 41 00 52 00 44 00");
        for (var mi2 = 0; mi2 < matches16.length; mi2++) {
            found++;
            var addr2 = matches16[mi2].address;
            console.log("[FOUND UTF16] " + addr2);

            // Patch UTF-16: write "FT_WILD" in UTF-16LE + null padding
            try {
                Memory.protect(addr2, 22, "rwx");
                var wild16 = [0x46,0x00, 0x54,0x00, 0x5f,0x00, 0x57,0x00, 0x49,0x00, 0x4c,0x00, 0x44,0x00, 0x00,0x00, 0x00,0x00, 0x00,0x00, 0x00,0x00];
                addr2.writeByteArray(wild16);
                console.log("[PATCHED] " + addr2 + " FT_STANDARD -> FT_WILD (UTF16)");
                patched++;
            } catch(e) {
                console.log("[SKIP] " + addr2 + " cannot write: " + e);
            }
        }
    } catch(e) {}
}

console.log("\n[*] Done. Found " + found + ", patched " + patched);
console.log("[*] If patched > 0, the box should now think this is a Wild game.");
console.log("[*] Try ending your turn to trigger recommendation recalculation.");

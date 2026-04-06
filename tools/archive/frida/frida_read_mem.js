// Read recommendation JS code from memory
var addrs = [ptr("0x472cb0ab"), ptr("0x49d28113")];

addrs.forEach(function(addr) {
    try {
        // Try reading as raw bytes and convert manually (skip null bytes)
        var startAddr = addr.sub(300);
        var bytes = startAddr.readByteArray(3000);
        var arr = new Uint8Array(bytes);
        var str = "";
        for (var i = 0; i < arr.length; i++) {
            if (arr[i] >= 32 && arr[i] < 127) {
                str += String.fromCharCode(arr[i]);
            } else if (arr[i] === 10 || arr[i] === 13) {
                str += "\n";
            } else if (str.length > 0 && arr[i] === 0) {
                // null terminator or padding, skip
            } else if (str.length > 10) {
                break;
            }
        }
        console.log("\n=== Full JS at " + addr + " ===");
        console.log(str);
    } catch(e) {
        console.log("Error reading " + addr + ": " + e);
    }
});

// Find onShowModule("ladderRecommend") in ASCII
var ranges = Process.enumerateRanges("r--");
var found = 0;
for (var ri = 0; ri < ranges.length && found < 10; ri++) {
    var range = ranges[ri];
    if (range.size > 50 * 1024 * 1024) continue;
    try {
        // "onShowModule" in ASCII
        var matches = Memory.scanSync(range.base, range.size,
            "6f 6e 53 68 6f 77 4d 6f 64 75 6c 65");
        for (var mi = 0; mi < matches.length; mi++) {
            try {
                var ctx = matches[mi].address.sub(50).readUtf8String(300);
                if (ctx.indexOf("Recommend") >= 0 || ctx.indexOf("recommend") >= 0) {
                    console.log("\n[!] onShowModule+Recommend at: " + matches[mi].address);
                    console.log("    " + ctx);
                    found++;
                }
            } catch(e) {}
        }
    } catch(e) {}
}

// Search for "star_level" which is how the game reports rank
found = 0;
for (var ri = 0; ri < ranges.length && found < 5; ri++) {
    var range = ranges[ri];
    if (range.size > 50 * 1024 * 1024) continue;
    try {
        // "star_level" in ASCII
        var matches = Memory.scanSync(range.base, range.size,
            "73 74 61 72 5f 6c 65 76 65 6c");
        for (var mi = 0; mi < matches.length; mi++) {
            try {
                var ctx = matches[mi].address.sub(30).readUtf8String(200);
                console.log("\n[RANK] star_level at: " + matches[mi].address);
                console.log("    " + ctx);
                found++;
            } catch(e) {}
        }
    } catch(e) {}
}

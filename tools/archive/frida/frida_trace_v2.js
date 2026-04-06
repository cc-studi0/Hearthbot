console.log("[*] Recommendation tracer v2");

var hsang = Process.getModuleByName("HSAng.exe");
console.log("[*] HSAng.exe base=" + hsang.base + " size=" + hsang.size);

var knownAddrs = {};
var traceCount = 0;

function scanAndTrace() {
    var ranges = Process.enumerateRanges("r--");
    var newFinds = [];

    for (var ri = 0; ri < ranges.length; ri++) {
        var range = ranges[ri];
        if (range.size > 80 * 1024 * 1024) continue;
        try {
            var matches = Memory.scanSync(range.base, range.size,
                "6f 6e 55 70 64 61 74 65 4c 61 64 64 65 72 41 63 74 69 6f 6e 52 65 63 6f 6d 6d 65 6e 64");
            for (var mi = 0; mi < matches.length; mi++) {
                var key = matches[mi].address.toString();
                if (knownAddrs[key]) continue;
                knownAddrs[key] = true;

                var bytes = matches[mi].address.sub(50).readByteArray(500);
                var arr = new Uint8Array(bytes);
                var str = "";
                for (var i = 0; i < arr.length; i++) {
                    if (arr[i] >= 32 && arr[i] < 127) str += String.fromCharCode(arr[i]);
                    else if (arr[i] === 10 || arr[i] === 13) str += "\n";
                }
                if (str.indexOf("actionName") >= 0 || str.indexOf("choiceId") >= 0) {
                    traceCount++;
                    var mod = Process.findModuleByAddress(matches[mi].address);
                    var modInfo = mod ? (mod.name + "+0x" + matches[mi].address.sub(mod.base).toString(16)) : "HEAP";
                    var ri2 = Process.findRangeByAddress(matches[mi].address);
                    var rangeInfo = ri2 ? (ri2.base + " prot=" + ri2.protection) : "?";
                    newFinds.push({
                        addr: matches[mi].address,
                        mod: modInfo,
                        range: rangeInfo,
                        snippet: str.substring(0, 300)
                    });
                }
            }
        } catch(e) {}
    }
    return newFinds;
}

// Baseline
console.log("[*] Baseline scan...");
scanAndTrace();
console.log("[*] Baseline: " + Object.keys(knownAddrs).length + " known. Waiting for NEW ones...\n");

var scanNum = 0;
setInterval(function() {
    scanNum++;
    var finds = scanAndTrace();
    if (finds.length > 0) {
        for (var i = 0; i < finds.length; i++) {
            var f = finds[i];
            console.log("\n[!!! NEW RECOMMEND #" + traceCount + " at scan #" + scanNum + " !!!]");
            console.log("  addr=" + f.addr + " module=" + f.mod);
            console.log("  range=" + f.range);
            console.log("  " + f.snippet);
        }
    } else if (scanNum % 5 === 0) {
        console.log("[scan #" + scanNum + "] waiting...");
    }
}, 2000);

console.log("[*] Play turns! Watching for new recommendation data...");

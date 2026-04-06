// Deep scan for recommendation execution JS and show/hide module calls
var ranges = Process.enumerateRanges("r--");
var foundRecommend = 0;
var foundShow = 0;

for (var ri = 0; ri < ranges.length; ri++) {
    var range = ranges[ri];
    if (range.size > 80 * 1024 * 1024) continue;

    try {
        // Find "onUpdateLadderActionRecommend" ASCII
        if (foundRecommend < 8) {
            var matches = Memory.scanSync(range.base, range.size,
                "6f 6e 55 70 64 61 74 65 4c 61 64 64 65 72 41 63 74 69 6f 6e 52 65 63 6f 6d 6d 65 6e 64");
            for (var mi = 0; mi < matches.length && foundRecommend < 8; mi++) {
                var addr = matches[mi].address;
                var bytes = addr.sub(200).readByteArray(2000);
                var arr = new Uint8Array(bytes);
                var str = "";
                for (var bi = 0; bi < arr.length; bi++) {
                    if (arr[bi] >= 32 && arr[bi] < 127) {
                        str += String.fromCharCode(arr[bi]);
                    } else if (arr[bi] === 10 || arr[bi] === 13) {
                        str += "\n";
                    }
                }
                if (str.indexOf("onUpdateLadder") >= 0) {
                    var jsIdx = str.indexOf("if (");
                    if (jsIdx < 0) jsIdx = str.indexOf("if(");
                    if (jsIdx < 0) jsIdx = str.indexOf("window.");
                    if (jsIdx < 0) jsIdx = Math.max(0, str.indexOf("onUpdateLadder") - 100);
                    console.log("\n[RECOMMEND #" + foundRecommend + "] at " + addr);
                    console.log(str.substring(jsIdx, jsIdx + 1500));
                    foundRecommend++;
                }
            }
        }
    } catch(e) {}

    try {
        // Find "ladderRecommend" ASCII
        if (foundShow < 5) {
            var matches2 = Memory.scanSync(range.base, range.size,
                "6c 61 64 64 65 72 52 65 63 6f 6d 6d 65 6e 64");
            for (var mi2 = 0; mi2 < matches2.length && foundShow < 5; mi2++) {
                var addr2 = matches2[mi2].address;
                var bytes2 = addr2.sub(100).readByteArray(500);
                var arr2 = new Uint8Array(bytes2);
                var str2 = "";
                for (var bi2 = 0; bi2 < arr2.length; bi2++) {
                    if (arr2[bi2] >= 32 && arr2[bi2] < 127) str2 += String.fromCharCode(arr2[bi2]);
                    else if (arr2[bi2] === 10 || arr2[bi2] === 13) str2 += "\n";
                }
                if (str2.indexOf("onShowModule") >= 0 || str2.indexOf("ShowModule") >= 0) {
                    console.log("\n[SHOW_MODULE #" + foundShow + "] at " + addr2);
                    console.log(str2);
                    foundShow++;
                }
            }
        }
    } catch(e) {}
}

console.log("\n[*] Scan done. Found " + foundRecommend + " recommend, " + foundShow + " showModule");

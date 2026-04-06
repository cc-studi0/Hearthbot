// 监控标准模式传说段位下，C++ 是否仍然生成推荐数据
// 持续扫描内存中新出现的 onUpdateLadderActionRecommend 调用

console.log("[*] 开始监控标准模式推荐数据...");
console.log("[*] 每5秒扫描一次内存，检查是否有新的推荐数据生成");

var lastFoundAddrs = {};
var scanCount = 0;

function scanForRecommendations() {
    scanCount++;
    var ranges = Process.enumerateRanges("r--");
    var newFinds = 0;

    for (var ri = 0; ri < ranges.length; ri++) {
        var range = ranges[ri];
        if (range.size > 80 * 1024 * 1024) continue;

        try {
            // "onUpdateLadderActionRecommend" ASCII
            var matches = Memory.scanSync(range.base, range.size,
                "6f 6e 55 70 64 61 74 65 4c 61 64 64 65 72 41 63 74 69 6f 6e 52 65 63 6f 6d 6d 65 6e 64");

            for (var mi = 0; mi < matches.length; mi++) {
                var addrStr = matches[mi].address.toString();
                if (lastFoundAddrs[addrStr]) continue;

                // New address - read content
                var addr = matches[mi].address;
                var bytes = addr.sub(100).readByteArray(1500);
                var arr = new Uint8Array(bytes);
                var str = "";
                for (var bi = 0; bi < arr.length; bi++) {
                    if (arr[bi] >= 32 && arr[bi] < 127) str += String.fromCharCode(arr[bi]);
                    else if (arr[bi] === 10 || arr[bi] === 13) str += "\n";
                }

                if (str.indexOf("onUpdateLadder") >= 0) {
                    var jsIdx = str.indexOf("if (");
                    if (jsIdx < 0) jsIdx = str.indexOf("if(");
                    if (jsIdx < 0) jsIdx = Math.max(0, str.indexOf("onUpdateLadder") - 50);
                    var snippet = str.substring(jsIdx, jsIdx + 800);

                    // Check if this contains actual game data
                    var hasData = snippet.indexOf("actionName") >= 0;
                    var hasGameType = snippet.indexOf("GAME_TYPE") >= 0;
                    var turnMatch = snippet.match(/"turnNum":\s*(\d+)/);
                    var optionMatch = snippet.match(/"optionId":\s*(\d+)/);

                    console.log("\n[SCAN #" + scanCount + "] NEW recommend at " + addr);
                    console.log("  hasActionData=" + hasData + " turnNum=" + (turnMatch ? turnMatch[1] : "?"));
                    console.log("  " + snippet.substring(0, 500));

                    lastFoundAddrs[addrStr] = true;
                    newFinds++;
                }
            }
        } catch(e) {}
    }

    // Also scan for "onShowModule" + "ladderRecommend" to see if the panel is toggled
    for (var ri = 0; ri < ranges.length; ri++) {
        var range = ranges[ri];
        if (range.size > 80 * 1024 * 1024) continue;
        try {
            var matches2 = Memory.scanSync(range.base, range.size,
                "6c 61 64 64 65 72 52 65 63 6f 6d 6d 65 6e 64");
            for (var mi2 = 0; mi2 < matches2.length; mi2++) {
                var addrStr2 = matches2[mi2].address.toString();
                if (lastFoundAddrs["show_" + addrStr2]) continue;
                var bytes2 = matches2[mi2].address.sub(80).readByteArray(300);
                var arr2 = new Uint8Array(bytes2);
                var str2 = "";
                for (var bi2 = 0; bi2 < arr2.length; bi2++) {
                    if (arr2[bi2] >= 32 && arr2[bi2] < 127) str2 += String.fromCharCode(arr2[bi2]);
                    else if (arr2[bi2] === 10 || arr2[bi2] === 13) str2 += "\n";
                }
                if (str2.indexOf("onShowModule") >= 0 && (str2.indexOf("true") >= 0 || str2.indexOf("false") >= 0)) {
                    console.log("\n[SCAN #" + scanCount + "] SHOW_MODULE at " + matches2[mi2].address);
                    console.log("  " + str2);
                    lastFoundAddrs["show_" + addrStr2] = true;
                }
            }
        } catch(e) {}
    }

    // Also check for GT_RANKED vs FT_STANDARD to confirm game mode
    if (scanCount <= 2) {
        for (var ri = 0; ri < ranges.length; ri++) {
            var range = ranges[ri];
            if (range.size > 80 * 1024 * 1024) continue;
            try {
                // "FT_STANDARD" or "FT_WILD"
                var matches3 = Memory.scanSync(range.base, range.size,
                    "46 54 5f 53 54 41 4e 44 41 52 44"); // FT_STANDARD
                if (matches3.length > 0) {
                    console.log("[MODE] Found FT_STANDARD in memory (" + matches3.length + " instances)");
                }
                var matches4 = Memory.scanSync(range.base, range.size,
                    "46 54 5f 57 49 4c 44"); // FT_WILD
                if (matches4.length > 0) {
                    console.log("[MODE] Found FT_WILD in memory (" + matches4.length + " instances)");
                }
            } catch(e) {}
            // Only check first few ranges
            if (ri > 100) break;
        }
    }

    if (newFinds === 0) {
        console.log("[SCAN #" + scanCount + "] 无新推荐数据");
    }
}

// Initial baseline scan
console.log("[*] 执行基线扫描，记录已有数据...");
scanForRecommendations();
console.log("[*] 基线完成。后续只报告新出现的数据。");
console.log("[*] 请切换到标准模式开始对局...\n");

// Schedule periodic scans
setInterval(scanForRecommendations, 5000);

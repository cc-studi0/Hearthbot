// Trace the call stack when recommendation JS is executed
// We hook cef_frame_execute_javascript or the internal function that constructs
// the "onUpdateLadderActionRecommend" JS string

console.log("[*] Setting up recommendation call-stack tracer...");

var libcef = Process.getModuleByName("libcef.dll");
var hsang = Process.getModuleByName("HSAng.exe");

console.log("[*] HSAng.exe base=" + hsang.base + " size=" + hsang.size);
console.log("[*] libcef.dll base=" + libcef.base + " size=" + libcef.size);

// Strategy: We know the C++ code constructs a JS string containing
// "onUpdateLadderActionRecommend" and passes it to CEF for execution.
// Hook memory write/allocation to detect when this string is being built.

// Approach 1: Hook cef_v8context_eval or frame->ExecuteJavaScript
// These are virtual functions, hard to find directly.
//
// Approach 2: Hook the string construction. The C++ code must call
// some string formatting function. We can use MemoryAccessMonitor or
// scan for the string in real-time after each turn.

// Approach 3 (best): Place a hardware breakpoint on a known memory location
// that contains "onUpdateLadderActionRecommend" and catch the write.

// Let's use Approach 3 variant: continuously monitor for NEW instances
// of the recommendation string and backtrace the thread that created them.

var knownAddrs = {};
var traceCount = 0;

function scanAndTrace() {
    var ranges = Process.enumerateRanges("r--");
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

                // Check if this is actual recommendation JS (not just our hook script)
                var bytes = matches[mi].address.sub(50).readByteArray(300);
                var arr = new Uint8Array(bytes);
                var str = "";
                for (var i = 0; i < arr.length; i++) {
                    if (arr[i] >= 32 && arr[i] < 127) str += String.fromCharCode(arr[i]);
                    else if (arr[i] === 10 || arr[i] === 13) str += "\n";
                }
                if (str.indexOf("actionName") >= 0 || str.indexOf("choiceId") >= 0) {
                    traceCount++;
                    console.log("\n[NEW RECOMMEND #" + traceCount + "] at " + matches[mi].address);
                    console.log("  snippet: " + str.substring(0, 200));

                    // Check which module owns this memory
                    var mod = Process.findModuleByAddress(matches[mi].address);
                    if (mod) {
                        console.log("  module: " + mod.name + " offset=0x" + matches[mi].address.sub(mod.base).toString(16));
                    } else {
                        console.log("  module: HEAP (dynamic allocation)");
                    }

                    // Find the range info
                    var ri2 = Process.findRangeByAddress(matches[mi].address);
                    if (ri2) {
                        console.log("  range: " + ri2.base + " size=" + ri2.size + " prot=" + ri2.protection);
                    }
                }
            }
        } catch(e) {}
    }
}

// Do initial scan to establish baseline
console.log("[*] Baseline scan...");
scanAndTrace();
console.log("[*] Baseline done. " + Object.keys(knownAddrs).length + " existing instances recorded.\n");

// Now hook malloc/HeapAlloc to catch NEW allocations containing the string
// This is more targeted than periodic scanning

var pHeapAlloc = Module.findExportByName("ntdll.dll", "RtlAllocateHeap");
var pHeapFree = Module.findExportByName("ntdll.dll", "RtlFreeHeap");

// Track recent large allocations
var recentAllocs = [];
var MAX_TRACKED = 200;

if (pHeapAlloc) {
    Interceptor.attach(pHeapAlloc, {
        onLeave: function(retval) {
            if (retval.isNull()) return;
            // We'll check allocations in the scan
        }
    });
}

// Periodic scan every 2 seconds
var scanNum = 0;
setInterval(function() {
    scanNum++;
    var beforeCount = traceCount;
    scanAndTrace();
    if (traceCount > beforeCount) {
        console.log("[SCAN #" + scanNum + "] Found " + (traceCount - beforeCount) + " NEW recommendations!");
    } else if (scanNum % 5 === 0) {
        console.log("[SCAN #" + scanNum + "] no new data");
    }
}, 2000);

console.log("[*] Monitoring active. Play turns in WILD mode!");
console.log("[*] When new recommendation appears, we'll identify the source.\n");

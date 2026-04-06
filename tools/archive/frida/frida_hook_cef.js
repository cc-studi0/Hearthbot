// Frida hook script for HSAng.exe
// 目标：拦截网络请求，找到推荐API + 拦截CEF JS执行

var libcef = Process.getModuleByName("libcef.dll");
console.log("[*] libcef.dll base: " + libcef.base + " size: " + libcef.size);

// === Hook WINHTTP ===
try {
    var pSendReq = Module.findExportByName("WINHTTP.dll", "WinHttpSendRequest");
    var pOpenReq = Module.findExportByName("WINHTTP.dll", "WinHttpOpenRequest");
    var pConnect = Module.findExportByName("WINHTTP.dll", "WinHttpConnect");
    var pReadData = Module.findExportByName("WINHTTP.dll", "WinHttpReadData");

    if (pConnect) {
        Interceptor.attach(pConnect, {
            onEnter: function(args) {
                try {
                    var host = args[1].readUtf16String();
                    var port = args[2].toInt32();
                    console.log("[HTTP] Connect: " + host + ":" + port);
                } catch(e) {}
            }
        });
        console.log("[+] Hooked WinHttpConnect");
    }

    if (pOpenReq) {
        Interceptor.attach(pOpenReq, {
            onEnter: function(args) {
                try {
                    var verb = args[1].readUtf16String();
                    var path = args[2].readUtf16String();
                    console.log("[HTTP] " + verb + " " + path);
                } catch(e) {}
            }
        });
        console.log("[+] Hooked WinHttpOpenRequest");
    }

    if (pSendReq) {
        Interceptor.attach(pSendReq, {
            onEnter: function(args) {
                this.hdr = "";
                this.body = "";
                try {
                    if (!args[1].isNull()) this.hdr = args[1].readUtf16String();
                } catch(e) {}
                var optLen = args[4].toInt32();
                if (optLen > 0 && !args[3].isNull()) {
                    try { this.body = args[3].readUtf8String(Math.min(optLen, 2048)); } catch(e) {}
                }
            },
            onLeave: function(retval) {
                if (this.hdr) console.log("[HTTP HDR] " + this.hdr.substring(0, 500));
                if (this.body) console.log("[HTTP BODY] " + this.body.substring(0, 500));
            }
        });
        console.log("[+] Hooked WinHttpSendRequest");
    }

    if (pReadData) {
        Interceptor.attach(pReadData, {
            onEnter: function(args) {
                this.buf = args[1];
                this.bytesReadPtr = args[3];
            },
            onLeave: function(retval) {
                if (retval.toInt32() !== 0) return;
                try {
                    if (this.bytesReadPtr.isNull()) return;
                    var nRead = this.bytesReadPtr.readU32();
                    if (nRead > 0 && nRead < 8192) {
                        var data = this.buf.readUtf8String(nRead);
                        // Filter for interesting responses
                        if (data && (data.indexOf("recommend") >= 0 || data.indexOf("action") >= 0 ||
                            data.indexOf("legend") >= 0 || data.indexOf("predict") >= 0 ||
                            data.indexOf("mulligan") >= 0 || data.indexOf("play_") >= 0 ||
                            data.indexOf("star_level") >= 0 || data.indexOf("actionName") >= 0)) {
                            console.log("\n[HTTP RESP interesting] " + data.substring(0, 2000));
                        }
                    }
                } catch(e) {}
            }
        });
        console.log("[+] Hooked WinHttpReadData");
    }
} catch(e) {
    console.log("[!] WINHTTP hook error: " + e);
}

// === Hook CEF ExecuteJavaScript via scanning for the vtable ===
// In libcef.dll, CefFrame::ExecuteJavaScript is called via CefBrowserHost
// Let's scan for the function pattern instead

// Scan libcef for "onUpdateLadderActionRecommend" string at runtime
// This string must exist somewhere when recommendations are being prepared
var pattern = "onUpdateLadderActionRecommend";
var patternBytes = [];
for (var i = 0; i < pattern.length; i++) {
    patternBytes.push(pattern.charCodeAt(i).toString(16).padStart(2, '0'));
}

// Also scan for "onShowModule"
console.log("[*] Scanning for recommendation strings in process memory...");

var ranges = Process.enumerateRanges('r--');
var found = 0;
for (var ri = 0; ri < ranges.length && found < 5; ri++) {
    var range = ranges[ri];
    if (range.size > 50 * 1024 * 1024) continue; // skip huge ranges
    try {
        var matches = Memory.scanSync(range.base, range.size,
            "6f 6e 55 70 64 61 74 65 4c 61 64 64 65 72 41 63 74 69 6f 6e 52 65 63 6f 6d 6d 65 6e 64");
        // "onUpdateLadderActionRecommend" in ASCII
        for (var mi = 0; mi < matches.length; mi++) {
            console.log("[!] Found 'onUpdateLadderActionRecommend' at: " + matches[mi].address);
            // Read surrounding context
            try {
                var ctx = matches[mi].address.sub(32).readUtf8String(200);
                console.log("    Context: " + ctx.substring(0, 200));
            } catch(e) {}
            found++;
        }
    } catch(e) {}
}

// Also scan for UTF-16 version (CEF uses wide strings)
var found2 = 0;
for (var ri = 0; ri < ranges.length && found2 < 5; ri++) {
    var range = ranges[ri];
    if (range.size > 50 * 1024 * 1024) continue;
    try {
        // "onSh" in UTF-16LE = 6f 00 6e 00 53 00 68 00 6f 00 77 00 4d 00
        var matches = Memory.scanSync(range.base, range.size,
            "6f 00 6e 00 53 00 68 00 6f 00 77 00 4d 00 6f 00 64 00 75 00 6c 00 65 00");
        for (var mi = 0; mi < matches.length; mi++) {
            console.log("[!] Found 'onShowModule' (UTF16) at: " + matches[mi].address);
            try {
                var ctx = matches[mi].address.readUtf16String(100);
                console.log("    Value: " + ctx);
            } catch(e) {}
            found2++;
        }
    } catch(e) {}
}

// Search for "ladderRecommend" in UTF-16
var found3 = 0;
for (var ri = 0; ri < ranges.length && found3 < 5; ri++) {
    var range = ranges[ri];
    if (range.size > 50 * 1024 * 1024) continue;
    try {
        // "ladderRecommend" UTF-16LE
        var matches = Memory.scanSync(range.base, range.size,
            "6c 00 61 00 64 00 64 00 65 00 72 00 52 00 65 00 63 00 6f 00 6d 00 6d 00 65 00 6e 00 64 00");
        for (var mi = 0; mi < matches.length; mi++) {
            console.log("[!] Found 'ladderRecommend' (UTF16) at: " + matches[mi].address);
            try {
                var ctx = matches[mi].address.sub(20).readUtf16String(150);
                console.log("    Context: " + ctx);
            } catch(e) {}
            found3++;
        }
    } catch(e) {}
}

console.log("\n[*] All hooks installed. Monitoring traffic...");

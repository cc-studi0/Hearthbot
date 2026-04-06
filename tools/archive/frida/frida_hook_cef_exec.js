console.log("[*] Hook CEF ExecuteJavaScript via cef_string pattern");

// In libcef.dll, CefFrame::ExecuteJavaScript takes CefString parameters
// CefString is UTF-16 internally. The function eventually calls into V8.
// We can find it by searching for the exported cef_frame_execute_javascript
// or by hooking the internal CefString creation with "onUpdate" content.

var libcef = Process.getModuleByName("libcef.dll");
console.log("[*] libcef at " + libcef.base + " size=" + libcef.size);

// Search libcef exports for anything related to execute/javascript/frame
var cefExports = libcef.enumerateExports();
var relevant = [];
cefExports.forEach(function(e) {
    var n = e.name.toLowerCase();
    if (n.indexOf("execute") >= 0 || n.indexOf("javascript") >= 0 || n.indexOf("eval") >= 0 ||
        n.indexOf("frame") >= 0 || n.indexOf("string") >= 0) {
        relevant.push(e);
    }
});
console.log("[*] Relevant CEF exports (" + relevant.length + "):");
relevant.forEach(function(e) { console.log("  " + e.name + " @ " + e.address); });

// Hook cef_string_utf16_set and cef_string_utf8_to_utf16
// These are called when building CefString from C++ std::string
var hitCount = 0;

cefExports.forEach(function(e) {
    if (e.name === "cef_string_utf8_to_utf16" || e.name === "cef_string_userfree_utf16_alloc") {
        Interceptor.attach(e.address, {
            onEnter: function(args) {
                if (e.name === "cef_string_utf8_to_utf16") {
                    // args[0]=src (utf8), args[1]=src_len
                    var len = args[1].toInt32();
                    if (len < 30 || len > 500000) return;
                    try {
                        var s = args[0].readCString(80);
                        if (s && s.indexOf("onUpdateLadder") >= 0) {
                            hitCount++;
                            if (hitCount <= 10) {
                                console.log("\n[!!! CEF STRING HIT #" + hitCount + " !!!] " + e.name + " len=" + len);
                                console.log("  " + s);
                                console.log("  BACKTRACE:");
                                var bt = Thread.backtrace(this.context, Backtracer.ACCURATE);
                                for (var i = 0; i < bt.length && i < 25; i++) {
                                    var mod = Process.findModuleByAddress(bt[i]);
                                    if (mod) {
                                        console.log("    [" + i + "] " + mod.name + "+0x" + bt[i].sub(mod.base).toString(16));
                                    } else {
                                        console.log("    [" + i + "] " + bt[i]);
                                    }
                                }
                            }
                        }
                    } catch(ex) {}
                }
            }
        });
        console.log("[+] Hooked " + e.name);
    }
});

// Also hook cef_string_utf16_set for UTF-16 path
cefExports.forEach(function(e) {
    if (e.name === "cef_string_utf16_set") {
        Interceptor.attach(e.address, {
            onEnter: function(args) {
                // args[0]=src (utf16), args[1]=src_len (chars)
                var len = args[1].toInt32();
                if (len < 20 || len > 500000) return;
                try {
                    var s = args[0].readUtf16String(80);
                    if (s && s.indexOf("onUpdateLadder") >= 0) {
                        hitCount++;
                        if (hitCount <= 10) {
                            console.log("\n[!!! CEF UTF16 HIT #" + hitCount + " !!!] len=" + len);
                            console.log("  " + s);
                            console.log("  BACKTRACE:");
                            var bt = Thread.backtrace(this.context, Backtracer.ACCURATE);
                            for (var i = 0; i < bt.length && i < 25; i++) {
                                var mod = Process.findModuleByAddress(bt[i]);
                                if (mod) {
                                    console.log("    [" + i + "] " + mod.name + "+0x" + bt[i].sub(mod.base).toString(16));
                                } else {
                                    console.log("    [" + i + "] " + bt[i]);
                                }
                            }
                        }
                    }
                } catch(ex) {}
            }
        });
        console.log("[+] Hooked cef_string_utf16_set");
    }
});

console.log("\n[*] CEF hooks ready. Play turns!\n");

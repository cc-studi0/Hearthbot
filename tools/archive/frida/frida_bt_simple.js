console.log("[*] Simple backtrace tracer");

// List available MSVCR100 exports
var msvcr = Process.getModuleByName("MSVCR100.dll");
console.log("[*] MSVCR100 at " + msvcr.base);
try { var msvcp = Process.getModuleByName("MSVCP100.dll"); console.log("[*] MSVCP100 at " + msvcp.base); } catch(e) { console.log("[*] MSVCP100 not found"); }

var memcpyAddr = Module.findExportByName("MSVCR100.dll", "memcpy");
var memmoveAddr = Module.findExportByName("MSVCR100.dll", "memmove");
var memcpy_sAddr = Module.findExportByName("MSVCR100.dll", "memcpy_s");

console.log("[*] memcpy=" + memcpyAddr + " memmove=" + memmoveAddr + " memcpy_s=" + memcpy_sAddr);

var hitCount = 0;

function hookCopy(name, addr) {
    if (!addr) return;
    Interceptor.attach(addr, {
        onEnter: function(args) {
            // memcpy(dest, src, len) or memmove(dest, src, len)
            var src = (name === "memcpy_s") ? args[2] : args[1];
            var len = (name === "memcpy_s") ? args[4] : args[2];
            var n = len.toInt32();
            if (n < 30 || n > 200000) return;
            try {
                var s = src.readCString(60);
                if (!s) return;
                if (s.indexOf("onUpdateLadder") >= 0 ||
                    (s.indexOf("if (window") >= 0 && s.indexOf("Recommend") >= 0)) {
                    hitCount++;
                    if (hitCount <= 15) {
                        console.log("\n[HIT #" + hitCount + "] " + name + " len=" + n);
                        console.log("  str: " + s);
                        console.log("  BACKTRACE:");
                        var bt = Thread.backtrace(this.context, Backtracer.ACCURATE);
                        for (var i = 0; i < Math.min(bt.length, 20); i++) {
                            var mod = Process.findModuleByAddress(bt[i]);
                            if (mod) {
                                var off = "0x" + bt[i].sub(mod.base).toString(16);
                                console.log("    [" + i + "] " + mod.name + "+" + off + " (" + bt[i] + ")");
                            } else {
                                console.log("    [" + i + "] " + bt[i]);
                            }
                        }
                    }
                }
            } catch(e) {}
        }
    });
    console.log("[+] Hooked " + name);
}

hookCopy("memcpy", memcpyAddr);
hookCopy("memmove", memmoveAddr);
hookCopy("memcpy_s", memcpy_sAddr);

console.log("\n[*] Ready - play turns in WILD!\n");

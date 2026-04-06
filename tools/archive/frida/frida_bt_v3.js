console.log("[*] Backtrace tracer v3");

var hitCount = 0;

var addr = Module.findExportByName("ntdll.dll", "memcpy");
console.log("[*] ntdll memcpy = " + addr);

if (addr) {
    Interceptor.attach(addr, {
        onEnter: function(args) {
            var n = args[2].toInt32();
            if (n < 30 || n > 200000) return;
            try {
                var s = args[1].readCString(60);
                if (!s) return;
                if (s.indexOf("onUpdateLadder") < 0) return;
                hitCount++;
                if (hitCount > 15) return;
                console.log("\n[HIT #" + hitCount + "] memcpy len=" + n);
                console.log("  str: " + s);
                var bt = Thread.backtrace(this.context, Backtracer.ACCURATE);
                for (var i = 0; i < bt.length && i < 20; i++) {
                    var mod = Process.findModuleByAddress(bt[i]);
                    if (mod) {
                        console.log("  [" + i + "] " + mod.name + "+0x" + bt[i].sub(mod.base).toString(16));
                    } else {
                        console.log("  [" + i + "] " + bt[i]);
                    }
                }
            } catch(e) {}
        }
    });
    console.log("[+] memcpy hooked via ntdll");
}

// Also try kernel32 lstrcpyA / lstrcatA
var lstrcpy = Module.findExportByName("kernel32.dll", "lstrcpyA");
if (lstrcpy) {
    Interceptor.attach(lstrcpy, {
        onEnter: function(args) {
            try {
                var s = args[1].readCString(60);
                if (s && s.indexOf("onUpdateLadder") >= 0) {
                    console.log("\n[lstrcpyA HIT] " + s);
                    var bt = Thread.backtrace(this.context, Backtracer.ACCURATE);
                    bt.forEach(function(a, i) {
                        var m = Process.findModuleByAddress(a);
                        console.log("  [" + i + "] " + (m ? m.name + "+0x" + a.sub(m.base).toString(16) : a));
                    });
                }
            } catch(e) {}
        }
    });
    console.log("[+] lstrcpyA hooked");
}

console.log("[*] Ready. Play turns!\n");

console.log("[*] Backtrace tracer v4");

function findExport(dllName, funcName) {
    var mod = Process.getModuleByName(dllName);
    var result = null;
    mod.enumerateExports().forEach(function(e) {
        if (e.name === funcName) result = e.address;
    });
    return result;
}

var memcpyAddr = findExport("ntdll.dll", "memcpy");
var memmoveAddr = findExport("ntdll.dll", "memcpy");  // ntdll memcpy is actually memmove
console.log("[*] ntdll memcpy=" + memcpyAddr);

// Also get msvcr100 memcpy
var msvcr_memcpy = findExport("MSVCR100.dll", "memcpy");
var msvcr_memmove = findExport("MSVCR100.dll", "memmove");
console.log("[*] MSVCR100 memcpy=" + msvcr_memcpy + " memmove=" + msvcr_memmove);

var hitCount = 0;

function hookAddr(name, addr) {
    if (!addr) { console.log("[-] " + name + " not found"); return; }
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
                console.log("\n[HIT #" + hitCount + "] " + name + " len=" + n);
                console.log("  " + s);
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
    console.log("[+] " + name + " hooked");
}

hookAddr("ntdll_memcpy", memcpyAddr);
hookAddr("msvcr_memcpy", msvcr_memcpy);
hookAddr("msvcr_memmove", msvcr_memmove);

console.log("\n[*] Play turns in WILD!\n");

// Hook memory write to the heap region where recommendations appear
// Use MemoryAccessMonitor or intercept string formatting functions

console.log("[*] Backtrace tracer - finding who constructs recommendation JS");

var hsang = Process.getModuleByName("HSAng.exe");
var hsangBase = hsang.base;
var hsangEnd = hsang.base.add(hsang.size);
console.log("[*] HSAng.exe " + hsangBase + " - " + hsangEnd);

// Strategy: Hook sprintf/snprintf/QString formatting that creates the JS string
// The C++ code likely uses QString or std::string to build the JS

// Hook MSVCR100 sprintf variants (HSAng links against MSVCR100)
var targets = [
    ["MSVCR100.dll", "sprintf"],
    ["MSVCR100.dll", "sprintf_s"],
    ["MSVCR100.dll", "_snprintf"],
    ["MSVCR100.dll", "_snprintf_s"],
    ["MSVCP100.dll", "?append@?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@QAEAAV12@PBD@Z"],
    ["MSVCP100.dll", "?assign@?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@QAEAAV12@PBD@Z"],
];

var hooked = 0;
targets.forEach(function(t) {
    var addr = Module.findExportByName(t[0], t[1]);
    if (addr) {
        Interceptor.attach(addr, {
            onEnter: function(args) {
                // For sprintf: args[0]=dest, args[1]=fmt
                // For string::append: args[1]=src
                this.checkArg = args[1];
            },
            onLeave: function(retval) {
                try {
                    if (this.checkArg && !this.checkArg.isNull()) {
                        var s = this.checkArg.readCString(100);
                        if (s && s.indexOf("onUpdateLadder") >= 0) {
                            console.log("\n[!!! FOUND !!!] " + t[1] + " called with recommendation string!");
                            console.log("[BACKTRACE]");
                            var bt = Thread.backtrace(this.context, Backtracer.ACCURATE);
                            for (var i = 0; i < bt.length; i++) {
                                var mod = Process.findModuleByAddress(bt[i]);
                                var info = mod ? (mod.name + "+0x" + bt[i].sub(mod.base).toString(16)) : bt[i].toString();
                                console.log("  [" + i + "] " + info);
                            }
                        }
                    }
                } catch(e) {}
            }
        });
        hooked++;
        console.log("[+] Hooked " + t[0] + "::" + t[1]);
    }
});

// Also try hooking memcpy/memmove - the JS string gets copied around
var pMemcpy = Module.findExportByName("MSVCR100.dll", "memcpy");
if (pMemcpy) {
    var memcpyCount = 0;
    Interceptor.attach(pMemcpy, {
        onEnter: function(args) {
            this.dest = args[0];
            this.src = args[1];
            this.len = args[2].toInt32();
        },
        onLeave: function(retval) {
            if (this.len < 30 || this.len > 100000) return;
            try {
                var s = this.src.readCString(50);
                if (s && (s.indexOf("onUpdateLadder") >= 0 || s.indexOf("onShowModule") >= 0)) {
                    memcpyCount++;
                    if (memcpyCount <= 10) {
                        console.log("\n[!!! MEMCPY #" + memcpyCount + " !!!] len=" + this.len + " str=" + s.substring(0, 80));
                        console.log("[BACKTRACE]");
                        var bt = Thread.backtrace(this.context, Backtracer.ACCURATE);
                        for (var i = 0; i < Math.min(bt.length, 15); i++) {
                            var mod = Process.findModuleByAddress(bt[i]);
                            var offset = mod ? bt[i].sub(mod.base) : ptr(0);
                            var info = mod ? (mod.name + "+0x" + offset.toString(16)) : bt[i].toString();
                            console.log("  [" + i + "] " + bt[i] + " " + info);
                        }
                    }
                }
            } catch(e) {}
        }
    });
    console.log("[+] Hooked memcpy (" + pMemcpy + ")");
}

// Hook memmove too
var pMemmove = Module.findExportByName("MSVCR100.dll", "memmove");
if (pMemmove) {
    Interceptor.attach(pMemmove, {
        onEnter: function(args) {
            this.src = args[1];
            this.len = args[2].toInt32();
        },
        onLeave: function(retval) {
            if (this.len < 30 || this.len > 100000) return;
            try {
                var s = this.src.readCString(50);
                if (s && s.indexOf("onUpdateLadder") >= 0) {
                    console.log("\n[!!! MEMMOVE !!!] len=" + this.len);
                    var bt = Thread.backtrace(this.context, Backtracer.ACCURATE);
                    for (var i = 0; i < Math.min(bt.length, 15); i++) {
                        var mod = Process.findModuleByAddress(bt[i]);
                        var info = mod ? (mod.name + "+0x" + bt[i].sub(mod.base).toString(16)) : bt[i].toString();
                        console.log("  [" + i + "] " + bt[i] + " " + info);
                    }
                }
            } catch(e) {}
        }
    });
    console.log("[+] Hooked memmove");
}

console.log("\n[*] " + hooked + " format functions hooked + memcpy/memmove");
console.log("[*] Play turns in WILD - we'll catch the call stack!\n");

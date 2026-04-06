console.log("[*] Check if recommendation functions get called in standard mode");

var base = Process.getModuleByName("HSAng.exe").base;

// Hook the key functions from the backtrace to see which ones get called
var hooks = [
    [0x5be790, "recommend_dispatcher(+0x5be790)"],
    [0x5be880, "recommend_func2(+0x5be880)"],
    [0x5bdfa8, "execute_js_wrapper(+0x5bdfa8)"],
    [0x5be028, "recommend_builder(+0x5be028)"],
];

hooks.forEach(function(h) {
    var addr = base.add(h[0]);
    try {
        Interceptor.attach(addr, {
            onEnter: function(args) {
                console.log("[CALLED] " + h[1] + " at " + new Date().toISOString());
            }
        });
        console.log("[+] Hooked " + h[1]);
    } catch(e) {
        console.log("[-] Failed " + h[1] + ": " + e);
    }
});

// Also hook at a wider level - the Qt slot dispatcher at +0x5bb11c area
// The function prologue at +0x5bb1c0 might be the top-level handler
var wideHooks = [
    [0x5bb1c0, "qt_handler_5bb1c0"],
    [0x5bb250, "qt_handler_5bb250"],
    [0x5baf30, "qt_handler_5baf30"],
    [0x5baf80, "qt_handler_5baf80"],
];

wideHooks.forEach(function(h) {
    var addr = base.add(h[0]);
    try {
        Interceptor.attach(addr, {
            onEnter: function(args) {
                console.log("[CALLED] " + h[1]);
            }
        });
        console.log("[+] Hooked " + h[1]);
    } catch(e) {
        console.log("[-] Failed " + h[1] + ": " + e);
    }
});

console.log("\n[*] Play a turn in STANDARD mode. If nothing shows, the gate is upstream.\n");

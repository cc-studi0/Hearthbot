// Persistent hook: intercept ReadProcessMemory and patch FT_STANDARD -> FT_WILD
// in the returned buffer every time the box reads from the Hearthstone process

var pReadProcessMemory = Module.findExportByName("kernel32.dll", "ReadProcessMemory");
var patchCount = 0;
var callCount = 0;

// FT_STANDARD in ASCII bytes
var stdBytes = [0x46, 0x54, 0x5f, 0x53, 0x54, 0x41, 0x4e, 0x44, 0x41, 0x52, 0x44]; // FT_STANDARD
var wildBytes = [0x46, 0x54, 0x5f, 0x57, 0x49, 0x4c, 0x44, 0x00, 0x00, 0x00, 0x00]; // FT_WILD\0\0\0\0

Interceptor.attach(pReadProcessMemory, {
    onEnter: function(args) {
        this.buf = args[2];        // lpBuffer
        this.size = args[3].toInt32(); // nSize
        this.pBytesRead = args[4]; // lpNumberOfBytesRead
    },
    onLeave: function(retval) {
        if (retval.toInt32() === 0) return; // failed
        if (this.size < 11) return; // too small to contain FT_STANDARD

        callCount++;
        var bytesRead = this.size;
        if (!this.pBytesRead.isNull()) {
            try { bytesRead = this.pBytesRead.readU32(); } catch(e) {}
        }
        if (bytesRead < 11) return;

        // Scan the returned buffer for FT_STANDARD and replace
        try {
            var scanSize = Math.min(bytesRead, 1024 * 1024); // cap at 1MB
            var matches = Memory.scanSync(this.buf, scanSize,
                "46 54 5f 53 54 41 4e 44 41 52 44");

            for (var i = 0; i < matches.length; i++) {
                var addr = matches[i].address;
                addr.writeByteArray(wildBytes);
                patchCount++;
                if (patchCount <= 20) {
                    console.log("[PATCH #" + patchCount + "] ReadProcessMemory buffer @ " + addr + " FT_STANDARD -> FT_WILD (read " + bytesRead + " bytes)");
                }
            }
        } catch(e) {}
    }
});

console.log("[*] ReadProcessMemory hook active. FT_STANDARD will be replaced on every read.");
console.log("[*] Play your turns - recommendation should appear now.");

// Stats every 15s
setInterval(function() {
    console.log("[STATS] RPM calls=" + callCount + " patches=" + patchCount);
    callCount = 0;
}, 15000);

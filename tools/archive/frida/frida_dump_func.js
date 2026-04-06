console.log("[*] Dumping recommendation call chain functions");

var hsang = Process.getModuleByName("HSAng.exe");
var base = hsang.base;
console.log("[*] HSAng base=" + base);

// Key addresses from backtrace (these are return addresses, so the call is slightly before)
var offsets = [0x5bdfc8, 0x5be7f0, 0x5bb11c];

offsets.forEach(function(off) {
    var addr = base.add(off);
    console.log("\n=== HSAng.exe+0x" + off.toString(16) + " (absolute: " + addr + ") ===");

    // Disassemble around this address to find the function and the conditional branch
    // Read bytes around the call site and dump as hex + try to identify patterns
    var startAddr = addr.sub(128); // Look 128 bytes before the return address
    var bytes = startAddr.readByteArray(512);
    var arr = new Uint8Array(bytes);

    // Print hex dump with potential call/jmp instructions highlighted
    var hexLines = [];
    for (var i = 0; i < arr.length; i += 16) {
        var hexParts = [];
        var asciiParts = [];
        for (var j = 0; j < 16 && (i + j) < arr.length; j++) {
            var b = arr[i + j];
            hexParts.push(("0" + b.toString(16)).slice(-2));
            asciiParts.push((b >= 32 && b < 127) ? String.fromCharCode(b) : ".");
        }
        var lineAddr = startAddr.add(i);
        var marker = (lineAddr.equals(addr)) ? " <-- RETURN ADDR" : "";
        hexLines.push(lineAddr + ": " + hexParts.join(" ") + "  " + asciiParts.join("") + marker);
    }
    console.log(hexLines.join("\n"));
});

// Also dump the function at HSAng.exe+0x5bb11c more extensively (the scheduler)
// Go further back to find the function prologue
var schedAddr = base.add(0x5bb11c);
console.log("\n=== Extended dump around scheduler (0x5bb11c) - 512 bytes before ===");
var farBack = schedAddr.sub(512);
var bytes2 = farBack.readByteArray(1024);
var arr2 = new Uint8Array(bytes2);

// Look for common function prologues: 55 8b ec (push ebp; mov ebp,esp) or
// sub esp, xx patterns
var prologues = [];
for (var i = 0; i < arr2.length - 3; i++) {
    // push ebp; mov ebp, esp
    if (arr2[i] === 0x55 && arr2[i+1] === 0x8b && arr2[i+2] === 0xec) {
        prologues.push(i);
    }
    // push ebx/esi/edi followed by sub esp
    if ((arr2[i] === 0x53 || arr2[i] === 0x56 || arr2[i] === 0x57) &&
        arr2[i+1] === 0x55 && arr2[i+2] === 0x8b && arr2[i+3] === 0xec) {
        prologues.push(i);
    }
}
console.log("Function prologues found at offsets: " + prologues.map(function(p) {
    return "0x" + farBack.add(p).sub(base).toString(16);
}).join(", "));

// Find conditional jumps (je/jne/jz/jnz) near the return addresses
// These are the branches that decide whether to call the recommendation
console.log("\n=== Conditional branches near 0x5be7f0 ===");
var funcArea = base.add(0x5be000);
var funcBytes = funcArea.readByteArray(4096);
var funcArr = new Uint8Array(funcBytes);

for (var i = 0; i < funcArr.length - 2; i++) {
    var b = funcArr[i];
    // Short conditional jumps: 0x74=je, 0x75=jne, 0x0f84=je near, 0x0f85=jne near
    if (b === 0x74 || b === 0x75 || b === 0x76 || b === 0x77 || b === 0x7c || b === 0x7d || b === 0x7e || b === 0x7f) {
        var jmpTarget = funcArea.add(i + 2 + (funcArr[i+1] > 127 ? funcArr[i+1] - 256 : funcArr[i+1]));
        var off = funcArea.add(i).sub(base);
        console.log("  " + (b === 0x74 ? "JE" : b === 0x75 ? "JNE" : "Jcc") +
            " at HSAng+0x" + off.toString(16) + " -> HSAng+0x" + jmpTarget.sub(base).toString(16));
    }
    // Near conditional jumps
    if (b === 0x0f && (funcArr[i+1] === 0x84 || funcArr[i+1] === 0x85)) {
        var rel32 = funcArr[i+2] | (funcArr[i+3] << 8) | (funcArr[i+4] << 16) | (funcArr[i+5] << 24);
        if (rel32 > 0x7fffffff) rel32 -= 0x100000000;
        var jmpTarget2 = funcArea.add(i + 6 + rel32);
        var off2 = funcArea.add(i).sub(base);
        console.log("  " + (funcArr[i+1] === 0x84 ? "JE(near)" : "JNE(near)") +
            " at HSAng+0x" + off2.toString(16) + " -> HSAng+0x" + jmpTarget2.sub(base).toString(16));
    }
}

console.log("\n[*] Dump complete. Use these offsets with x64dbg/Ghidra for detailed analysis.");

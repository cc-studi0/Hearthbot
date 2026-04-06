// Force recommendation by NOPing the mode check
// Key function: HSAng.exe+0x5be790 (the recommendation dispatcher)
// It calls HSAng.exe+0x5bdfc8 area which does ExecuteJavaScript

console.log("[*] Force recommendation patcher");

var hsang = Process.getModuleByName("HSAng.exe");
var base = hsang.base;
console.log("[*] HSAng base=" + base);

// The function at +0x5be790 has a check around +0x5be38b:
//   JE(near) at +0x5be38b -> +0x5be689 (big skip - likely the "skip recommendation" path)
// If we NOP this jump, it should always fall through to the recommendation code

// But first, let's look at what this function does at its start to understand the check
// Read the check bytes at 0x5be38b
var jeAddr = base.add(0x5be38b);
var jeBytes = jeAddr.readByteArray(6);
var arr = new Uint8Array(jeBytes);
var hex = "";
for (var i = 0; i < arr.length; i++) hex += ("0" + arr[i].toString(16)).slice(-2) + " ";
console.log("[*] JE(near) at +0x5be38b: " + hex);

// This should be 0F 84 xx xx xx xx (JE near)
// To NOP it: replace with 90 90 90 90 90 90

// But we also need to check the other big jump at +0x5be3a1:
//   JE(near) at +0x5be3a1 -> +0x5be5ac (another big skip)
var je2Addr = base.add(0x5be3a1);
var je2Bytes = je2Addr.readByteArray(6);
var arr2 = new Uint8Array(je2Bytes);
hex = "";
for (var i = 0; i < arr2.length; i++) hex += ("0" + arr2[i].toString(16)).slice(-2) + " ";
console.log("[*] JE(near) at +0x5be3a1: " + hex);

// Also check the big jump at +0x5bead5 -> +0x5bf0b9
var je3Addr = base.add(0x5bead5);
var je3Bytes = je3Addr.readByteArray(6);
var arr3 = new Uint8Array(je3Bytes);
hex = "";
for (var i = 0; i < arr3.length; i++) hex += ("0" + arr3[i].toString(16)).slice(-2) + " ";
console.log("[*] JE(near) at +0x5bead5: " + hex);

// Let's also check what comparison happens right before +0x5be38b
// Read 32 bytes before the JE
var preCheck = base.add(0x5be38b - 32);
var preBytes = preCheck.readByteArray(38);
var preArr = new Uint8Array(preBytes);
hex = "";
for (var i = 0; i < preArr.length; i++) {
    if (i > 0 && i % 16 === 0) hex += "\n  ";
    hex += ("0" + preArr[i].toString(16)).slice(-2) + " ";
}
console.log("[*] Bytes before JE at +0x5be38b:\n  " + hex);

// Let's approach this differently - hook the function at +0x5be880
// (the larger function that starts with prologue 55 8b ec)
// and trace its execution to find the exact branch point

// Actually, the most direct approach: patch the big JE jumps and see what happens
console.log("\n[*] Attempting to NOP the mode check jumps...");

// Patch JE(near) at +0x5be38b (6 bytes: 0F 84 xx xx xx xx -> 90*6)
try {
    Memory.protect(jeAddr, 6, "rwx");
    jeAddr.writeByteArray([0x90, 0x90, 0x90, 0x90, 0x90, 0x90]);
    console.log("[PATCHED] +0x5be38b JE -> NOP (always fall through)");
} catch(e) {
    console.log("[FAILED] +0x5be38b: " + e);
}

// Patch JE(near) at +0x5be3a1 (6 bytes)
try {
    Memory.protect(je2Addr, 6, "rwx");
    je2Addr.writeByteArray([0x90, 0x90, 0x90, 0x90, 0x90, 0x90]);
    console.log("[PATCHED] +0x5be3a1 JE -> NOP (always fall through)");
} catch(e) {
    console.log("[FAILED] +0x5be3a1: " + e);
}

console.log("\n[*] Patches applied. Try playing a turn in STANDARD mode now!");
console.log("[*] If the box crashes, we patched too aggressively and need to be more targeted.");

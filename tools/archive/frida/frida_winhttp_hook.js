// Hook WINHTTP to capture all HTTP traffic from HSAng.exe
// Goal: find the recommendation API endpoint during a wild mode game

var requestMap = {};
var connMap = {};
var reqId = 0;

// Hook WinHttpConnect - capture hostnames
var pConnect = Module.findExportByName("winhttp.dll", "WinHttpConnect");
if (pConnect) {
    Interceptor.attach(pConnect, {
        onEnter: function(args) {
            try {
                this.host = args[1].readUtf16String();
                this.port = args[2].toInt32();
            } catch(e) {}
        },
        onLeave: function(retval) {
            if (this.host && !retval.isNull()) {
                connMap[retval.toString()] = this.host + ":" + this.port;
                console.log("[CONNECT] " + this.host + ":" + this.port + " handle=" + retval);
            }
        }
    });
    console.log("[+] Hooked WinHttpConnect");
}

// Hook WinHttpOpenRequest - capture method + path
var pOpen = Module.findExportByName("winhttp.dll", "WinHttpOpenRequest");
if (pOpen) {
    Interceptor.attach(pOpen, {
        onEnter: function(args) {
            this.connHandle = args[0];
            try { this.verb = args[1].readUtf16String(); } catch(e) { this.verb = "?"; }
            try { this.path = args[2].readUtf16String(); } catch(e) { this.path = "?"; }
        },
        onLeave: function(retval) {
            if (!retval.isNull()) {
                var id = ++reqId;
                var host = connMap[this.connHandle.toString()] || "unknown";
                requestMap[retval.toString()] = {
                    id: id,
                    host: host,
                    verb: this.verb,
                    path: this.path,
                    response: ""
                };
                console.log("\n[REQ #" + id + "] " + this.verb + " " + host + this.path);
            }
        }
    });
    console.log("[+] Hooked WinHttpOpenRequest");
}

// Hook WinHttpSendRequest - capture request body
var pSend = Module.findExportByName("winhttp.dll", "WinHttpSendRequest");
if (pSend) {
    Interceptor.attach(pSend, {
        onEnter: function(args) {
            this.hReq = args[0];
            var headers = "";
            try { if (!args[1].isNull()) headers = args[1].readUtf16String(); } catch(e) {}
            var optLen = args[4].toInt32();
            var body = "";
            if (optLen > 0 && !args[3].isNull()) {
                try { body = args[3].readUtf8String(Math.min(optLen, 4096)); } catch(e) {
                    try { body = args[3].readCString(Math.min(optLen, 4096)); } catch(e2) {}
                }
            }
            var info = requestMap[this.hReq.toString()];
            var id = info ? info.id : "?";
            if (headers) console.log("[SEND #" + id + "] Headers: " + headers.substring(0, 300));
            if (body) console.log("[SEND #" + id + "] Body: " + body.substring(0, 1000));
        }
    });
    console.log("[+] Hooked WinHttpSendRequest");
}

// Hook WinHttpReadData - capture response
var pRead = Module.findExportByName("winhttp.dll", "WinHttpReadData");
if (pRead) {
    Interceptor.attach(pRead, {
        onEnter: function(args) {
            this.hReq = args[0];
            this.buf = args[1];
            this.bytesReadPtr = args[3];
        },
        onLeave: function(retval) {
            try {
                if (this.bytesReadPtr.isNull()) return;
                var nRead = this.bytesReadPtr.readU32();
                if (nRead <= 0 || nRead > 65536) return;
                var data = this.buf.readUtf8String(Math.min(nRead, 8192));
                if (!data) return;

                var info = requestMap[this.hReq.toString()];
                var id = info ? info.id : "?";

                // Always log for recommendation-related
                if (data.indexOf("actionName") >= 0 || data.indexOf("recommend") >= 0 ||
                    data.indexOf("predict") >= 0 || data.indexOf("mulligan") >= 0 ||
                    data.indexOf("play_") >= 0 || data.indexOf("turnNum") >= 0 ||
                    data.indexOf("optionId") >= 0 || data.indexOf("choiceId") >= 0) {
                    console.log("\n[RESP #" + id + " !!!RECOMMEND!!!] " + data.substring(0, 4000));
                } else if (data.length > 10) {
                    // Log first 150 chars of other responses for context
                    console.log("[RESP #" + id + "] " + data.substring(0, 150));
                }

                if (info) info.response += data;
            } catch(e) {}
        }
    });
    console.log("[+] Hooked WinHttpReadData");
}

console.log("\n[*] All WINHTTP hooks ready. Play a WILD game now!");
console.log("[*] Looking for recommendation API calls...\n");

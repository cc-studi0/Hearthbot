var requestMap = {};
var connMap = {};
var reqId = 0;
var mod = Process.getModuleByName("WINHTTP.dll");
console.log("[*] WINHTTP at " + mod.base);

var exports = {};
mod.enumerateExports().forEach(function(e) { exports[e.name] = e.address; });

if (exports.WinHttpConnect) {
    Interceptor.attach(exports.WinHttpConnect, {
        onEnter: function(args) {
            try { this.host = args[1].readUtf16String(); } catch(e) { this.host = ""; }
            try { this.port = args[2].toInt32(); } catch(e) { this.port = 0; }
        },
        onLeave: function(retval) {
            if (this.host && !retval.isNull()) {
                connMap[retval.toString()] = this.host;
                console.log("[CONN] " + this.host + ":" + this.port);
            }
        }
    });
    console.log("[+] WinHttpConnect hooked");
}

if (exports.WinHttpOpenRequest) {
    Interceptor.attach(exports.WinHttpOpenRequest, {
        onEnter: function(args) {
            this.ch = args[0];
            try { this.verb = args[1].readUtf16String(); } catch(e) { this.verb = "?"; }
            try { this.path = args[2].readUtf16String(); } catch(e) { this.path = "?"; }
        },
        onLeave: function(retval) {
            if (!retval.isNull()) {
                var id = ++reqId;
                var host = connMap[this.ch.toString()] || "?";
                requestMap[retval.toString()] = { id: id, host: host, verb: this.verb, path: this.path, resp: "" };
                console.log("[REQ #" + id + "] " + this.verb + " https://" + host + this.path);
            }
        }
    });
    console.log("[+] WinHttpOpenRequest hooked");
}

if (exports.WinHttpSendRequest) {
    Interceptor.attach(exports.WinHttpSendRequest, {
        onEnter: function(args) {
            this.h = args[0];
            var hdr = "";
            try { if (!args[1].isNull()) hdr = args[1].readUtf16String(); } catch(e) {}
            var bodyLen = args[4].toInt32();
            var body = "";
            if (bodyLen > 0 && !args[3].isNull()) {
                try { body = args[3].readUtf8String(Math.min(bodyLen, 4096)); } catch(e) {}
            }
            var info = requestMap[this.h.toString()];
            var id = info ? info.id : "?";
            if (hdr) console.log("[HDR #" + id + "] " + hdr.substring(0, 500));
            if (body) console.log("[BODY #" + id + "] " + body.substring(0, 2000));
        }
    });
    console.log("[+] WinHttpSendRequest hooked");
}

if (exports.WinHttpReadData) {
    Interceptor.attach(exports.WinHttpReadData, {
        onEnter: function(args) {
            this.h = args[0];
            this.buf = args[1];
            this.pRead = args[3];
        },
        onLeave: function(retval) {
            try {
                if (this.pRead.isNull()) return;
                var n = this.pRead.readU32();
                if (n <= 0 || n > 65536) return;
                var d = this.buf.readUtf8String(Math.min(n, 8192));
                if (!d) return;
                var info = requestMap[this.h.toString()];
                var id = info ? info.id : "?";
                var path = info ? info.path : "";
                if (d.indexOf("actionName") >= 0 || d.indexOf("recommend") >= 0 ||
                    d.indexOf("predict") >= 0 || d.indexOf("turnNum") >= 0) {
                    console.log("\n[!!!RECOMMEND RESP #" + id + " path=" + path + "!!!]\n" + d.substring(0, 4000));
                } else {
                    console.log("[RESP #" + id + "] (" + n + "b) " + d.substring(0, 200));
                }
            } catch(e) {}
        }
    });
    console.log("[+] WinHttpReadData hooked");
}

console.log("\n[*] Ready! Play your turns in WILD mode!\n");

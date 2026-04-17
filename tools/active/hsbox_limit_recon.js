'use strict';

// ============================================================================
// 盒子受限推荐侦察脚本 (阶段0 - 只观察，不改写)
// 用法：frida -n HSAng.exe -l tools/active/hsbox_limit_recon.js --runtime=v8
//
// 由 Scripts/recon_hsbox.ps1 调度启动。每次 attach 60 秒左右，对照组/实验组
// 分别跑一次，生成可 diff 的 raw log。
// ============================================================================

const T0 = Date.now();

function log(tag, payload) {
    const ts = ((Date.now() - T0) / 1000).toFixed(3);
    const line = JSON.stringify({ ts: ts, tag: tag, payload: payload });
    console.log(line);
}

function backtrace(ctx, depth) {
    if (!depth) depth = 8;
    try {
        return Thread.backtrace(ctx, Backtracer.ACCURATE)
            .slice(0, depth)
            .map(DebugSymbol.fromAddress)
            .map(function (s) {
                const mod = s.moduleName || '<?>';
                const nm = s.name || ('+0x' + s.address.toString(16));
                return mod + '!' + nm;
            });
    } catch (e) {
        return ['<bt-failed:' + e.message + '>'];
    }
}

function safeReadUtf16(ptr, maxLen) {
    if (maxLen === undefined) maxLen = 1024;
    try {
        if (ptr.isNull()) return null;
        return ptr.readUtf16String(maxLen);
    } catch (e) {
        return '<read-failed:' + e.message + '>';
    }
}

function safeReadAnsi(ptr, maxLen) {
    if (maxLen === undefined) maxLen = 1024;
    try {
        if (ptr.isNull()) return null;
        return ptr.readAnsiString(maxLen);
    } catch (e) {
        return '<read-failed:' + e.message + '>';
    }
}

// ---- CreateFileW：观察盒子读哪些文件（尤其 Hearthstone 日志） ----
(function hookCreateFileW() {
    const addr = Module.findExportByName('kernel32.dll', 'CreateFileW');
    if (!addr) {
        log('init-warn', { msg: 'CreateFileW export not found' });
        return;
    }
    Interceptor.attach(addr, {
        onEnter: function (args) {
            const path = safeReadUtf16(args[0], 520);
            if (!path) return;
            if (!/hearthstone|\\logs\\|\.log$|output_log|achievements|power/i.test(path)) return;
            this._path = path;
            this._ctx = this.context;
        },
        onLeave: function (retval) {
            if (!this._path) return;
            log('CreateFileW', {
                path: this._path,
                handle: retval.toString(),
                bt: backtrace(this._ctx)
            });
        }
    });
    log('init-hook', { name: 'CreateFileW', at: addr.toString() });
})();

// ---- ReadProcessMemory：观察盒子是否读 Hearthstone.exe 内存 ----
(function hookRPM() {
    const addr = Module.findExportByName('kernel32.dll', 'ReadProcessMemory');
    if (!addr) {
        log('init-warn', { msg: 'ReadProcessMemory export not found' });
        return;
    }
    const seen = {};
    Interceptor.attach(addr, {
        onEnter: function (args) {
            this._h = args[0];
            this._base = args[1];
            this._size = args[2].toInt32();
            this._ctx = this.context;
        },
        onLeave: function (retval) {
            if (retval.toInt32() === 0) return;
            if (this._size <= 0 || this._size > 0x10000) return;
            const key = this._base.toString() + ':' + this._size;
            seen[key] = (seen[key] || 0) + 1;
            if (seen[key] > 3) return;
            log('RPM', {
                handle: this._h.toString(),
                addr: this._base.toString(),
                size: this._size,
                nth: seen[key],
                bt: backtrace(this._ctx, 6)
            });
        }
    });
    log('init-hook', { name: 'ReadProcessMemory', at: addr.toString() });
})();

// ---- WinHttpSendRequest：观察盒子 C++ 层（非 CEF）直接发的 HTTP ----
(function hookWinHttp() {
    const addr = Module.findExportByName('winhttp.dll', 'WinHttpSendRequest');
    if (!addr) {
        log('init-warn', { msg: 'WinHttpSendRequest export not found' });
        return;
    }
    Interceptor.attach(addr, {
        onEnter: function (args) {
            const headers = safeReadUtf16(args[1], 512);
            log('WinHttpSendRequest', {
                hReq: args[0].toString(),
                headers: headers,
                bt: backtrace(this.context)
            });
        }
    });
    log('init-hook', { name: 'WinHttpSendRequest', at: addr.toString() });
})();

(function hookWinHttpConnect() {
    const addr = Module.findExportByName('winhttp.dll', 'WinHttpConnect');
    if (!addr) return;
    Interceptor.attach(addr, {
        onEnter: function (args) {
            const host = safeReadUtf16(args[1], 256);
            const port = args[2].toInt32();
            log('WinHttpConnect', { host: host, port: port });
        }
    });
    log('init-hook', { name: 'WinHttpConnect', at: addr.toString() });
})();

// ---- CefFrame::ExecuteJavaScript：C++ 推给 CEF V8 执行的所有 JS ----
(function hookExecJS() {
    const libcef = Process.findModuleByName('libcef.dll');
    if (!libcef) {
        log('init-warn', { msg: 'libcef.dll not loaded' });
        return;
    }
    const exp = libcef.enumerateExports().find(function (e) {
        return /execute[_]?java[sS]cript/i.test(e.name);
    });
    if (!exp) {
        log('init-warn', { msg: 'ExecuteJavaScript export not found in libcef.dll' });
        return;
    }
    Interceptor.attach(exp.address, {
        onEnter: function (args) {
            // cef_string_t 在 Windows 是 cef_string_utf16_t { char16* str; size_t length; cef_string_userfree_utf16_t dtor; }
            // C API 签名:  void fn(cef_frame_t* self, const cef_string_t* script, const cef_string_t* script_url, int start_line)
            try {
                const strPtr = args[1].readPointer();
                const jsLen = args[1].add(Process.pointerSize).readULong();
                const maxRead = Math.min(jsLen, 400);
                const js = strPtr.readUtf16String(maxRead);
                log('ExecuteJS', {
                    js: js,
                    len: jsLen,
                    bt: backtrace(this.context, 10)
                });
            } catch (e) {
                log('ExecuteJS', { err: 'parse-failed:' + e.message });
            }
        }
    });
    log('init-hook', { name: 'ExecuteJavaScript', at: exp.address.toString(), sym: exp.name });
})();

log('init', { pid: Process.id, arch: Process.arch, ready: true });

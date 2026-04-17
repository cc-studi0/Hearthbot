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
    // 通过 Frida send() 发出，由 Python 启动器写入 log 文件
    send({ ts: ts, tag: tag, payload: payload });
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

// 兼容 Frida 16.x（Module.findExportByName 被移除）
function resolveExport(moduleName, exportName) {
    const mod = Process.findModuleByName(moduleName);
    if (!mod) return null;
    if (typeof mod.findExportByName === 'function') {
        return mod.findExportByName(exportName);
    }
    if (typeof mod.getExportByName === 'function') {
        try { return mod.getExportByName(exportName); } catch (e) { /* fall through */ }
    }
    const exps = mod.enumerateExports();
    for (let i = 0; i < exps.length; i++) {
        if (exps[i].name === exportName) return exps[i].address;
    }
    return null;
}

// ---- CreateFileW：观察盒子读哪些文件（尤其 Hearthstone 日志） ----
(function hookCreateFileW() {
    const addr = resolveExport('kernel32.dll', 'CreateFileW');
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
    const addr = resolveExport('kernel32.dll', 'ReadProcessMemory');
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
    const addr = resolveExport('winhttp.dll', 'WinHttpSendRequest');
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
    const addr = resolveExport('winhttp.dll', 'WinHttpConnect');
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

log('init', { pid: Process.id, arch: Process.arch, ready: true });

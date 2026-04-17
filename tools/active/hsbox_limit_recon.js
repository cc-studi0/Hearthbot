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

// ---- CreateFileW：观察盒子打开的所有文件/管道（含 IPC 命名管道） ----
// 注意：去掉 path filter 以避免漏掉 \\.\pipe\ 命名管道、共享内存等 IPC 通道。
// 用 per-path 节流（每个唯一 path 最多记录前 5 次）控制噪音。
(function hookCreateFileW() {
    const addr = resolveExport('kernel32.dll', 'CreateFileW');
    if (!addr) {
        log('init-warn', { msg: 'CreateFileW export not found' });
        return;
    }
    const seen = {};
    Interceptor.attach(addr, {
        onEnter: function (args) {
            const path = safeReadUtf16(args[0], 520);
            if (!path) return;
            this._path = path;
            this._ctx = this.context;
        },
        onLeave: function (retval) {
            if (!this._path) return;
            seen[this._path] = (seen[this._path] || 0) + 1;
            if (seen[this._path] > 5) return;
            log('CreateFileW', {
                path: this._path,
                handle: retval.toString(),
                nth: seen[this._path],
                bt: backtrace(this._ctx)
            });
        }
    });
    log('init-hook', { name: 'CreateFileW', at: addr.toString() });
})();

// ---- 命名管道 / 共享内存 IPC ----
(function hookIPC() {
    // OpenFileMappingW: 打开命名共享内存
    (function () {
        const addr = resolveExport('kernel32.dll', 'OpenFileMappingW');
        if (!addr) { log('init-warn', { msg: 'OpenFileMappingW not found' }); return; }
        Interceptor.attach(addr, {
            onEnter: function (args) {
                this._name = safeReadUtf16(args[2], 256);
                this._ctx = this.context;
            },
            onLeave: function (retval) {
                log('OpenFileMappingW', {
                    name: this._name,
                    handle: retval.toString(),
                    bt: backtrace(this._ctx, 6)
                });
            }
        });
        log('init-hook', { name: 'OpenFileMappingW', at: addr.toString() });
    })();

    // CreateFileMappingW: 创建命名共享内存
    (function () {
        const addr = resolveExport('kernel32.dll', 'CreateFileMappingW');
        if (!addr) { log('init-warn', { msg: 'CreateFileMappingW not found' }); return; }
        Interceptor.attach(addr, {
            onEnter: function (args) {
                this._name = safeReadUtf16(args[5], 256);
                this._ctx = this.context;
            },
            onLeave: function (retval) {
                if (!this._name) return; // 匿名映射跳过
                log('CreateFileMappingW', {
                    name: this._name,
                    handle: retval.toString(),
                    bt: backtrace(this._ctx, 6)
                });
            }
        });
        log('init-hook', { name: 'CreateFileMappingW', at: addr.toString() });
    })();

    // MapViewOfFile / MapViewOfFileEx
    ['MapViewOfFile', 'MapViewOfFileEx'].forEach(function (sym) {
        const addr = resolveExport('kernel32.dll', sym);
        if (!addr) { log('init-warn', { msg: sym + ' not found' }); return; }
        const seen = {};
        Interceptor.attach(addr, {
            onEnter: function (args) {
                this._h = args[0].toString();
                this._size = args[4] ? args[4].toInt32() : 0;
                this._ctx = this.context;
            },
            onLeave: function (retval) {
                const key = this._h + ':' + this._size;
                seen[key] = (seen[key] || 0) + 1;
                if (seen[key] > 3) return;
                log(sym, {
                    handle: this._h,
                    size: this._size,
                    mappedAt: retval.toString(),
                    nth: seen[key],
                    bt: backtrace(this._ctx, 6)
                });
            }
        });
        log('init-hook', { name: sym, at: addr.toString() });
    });

    // CreateNamedPipeW: 盒子作为 server 创建管道
    (function () {
        const addr = resolveExport('kernel32.dll', 'CreateNamedPipeW');
        if (!addr) { log('init-warn', { msg: 'CreateNamedPipeW not found' }); return; }
        Interceptor.attach(addr, {
            onEnter: function (args) {
                this._name = safeReadUtf16(args[0], 256);
                this._ctx = this.context;
            },
            onLeave: function (retval) {
                log('CreateNamedPipeW', {
                    name: this._name,
                    handle: retval.toString(),
                    bt: backtrace(this._ctx, 6)
                });
            }
        });
        log('init-hook', { name: 'CreateNamedPipeW', at: addr.toString() });
    })();
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

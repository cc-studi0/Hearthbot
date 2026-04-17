'use strict';

// ============================================================================
// 盒子 queryMatchAvailable 永久 true 补丁
// 用法：python Scripts/queryMatchAvailable_hook.py 自动加载本脚本
//
// 在 HSAng.exe 里找 MatchLogicPrivate::queryMatchAvailable 的 export，
// Interceptor.attach + onLeave 把返回的 0 改成 1，让盒子在标传传说仍出推荐。
// ============================================================================

// 如果 export 找不到，开发者用 IDA 反编译 HSAng.exe 找到该函数 RVA 填这里。
// 例: const MANUAL_RVA = ptr('0x12345');
const MANUAL_RVA = null;

const T0 = Date.now();
function log(tag, payload) {
    const ts = ((Date.now() - T0) / 1000).toFixed(3);
    send({ ts: ts, tag: tag, payload: payload });
}

function locate() {
    const mod = Process.findModuleByName('HSAng.exe');
    if (!mod) return { addr: null, source: 'no-module', symbol: null };

    if (MANUAL_RVA !== null) {
        return { addr: mod.base.add(MANUAL_RVA), source: 'manual-rva', symbol: '<manual>' };
    }

    const exps = mod.enumerateExports();
    for (let i = 0; i < exps.length; i++) {
        if (/queryMatchAvailable/i.test(exps[i].name)) {
            return { addr: exps[i].address, source: 'export', symbol: exps[i].name };
        }
    }
    return { addr: null, source: 'not-found', symbol: null };
}

const target = locate();
if (target.addr === null) {
    log('fatal', { msg: 'queryMatchAvailable not found in HSAng.exe', source: target.source });
} else {
    let hits = 0;
    let forced = 0;
    Interceptor.attach(target.addr, {
        onLeave: function (retval) {
            hits++;
            const orig = retval.toInt32();
            if (orig === 0) {
                retval.replace(1);
                forced++;
            }
            if (hits % 20 === 0) {
                log('patch-stat', { hits: hits, forced: forced });
            }
        }
    });
    log('hook-installed', {
        source: target.source,
        symbol: target.symbol,
        addr: target.addr.toString()
    });
}

log('init', { pid: Process.id, arch: Process.arch });

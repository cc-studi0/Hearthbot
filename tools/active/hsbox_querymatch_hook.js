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

    // 1. 优先 MANUAL_RVA（人工 IDA 找的硬编码偏移）
    if (MANUAL_RVA !== null) {
        return { addr: mod.base.add(MANUAL_RVA), source: 'manual-rva', symbol: '<manual>' };
    }

    // 2. 试 enumerateExports
    const exps = mod.enumerateExports();
    for (let i = 0; i < exps.length; i++) {
        if (/queryMatchAvailable/i.test(exps[i].name)) {
            return { addr: exps[i].address, source: 'export', symbol: exps[i].name };
        }
    }

    // 3. 兜底：字符串 scan + xref + 函数入口反推（C++ release 无导出常用方式）
    const r = locateByStringScan(mod);
    if (r) return r;

    return { addr: null, source: 'not-found', symbol: null };
}

function asciiToHexPattern(s) {
    return s.split('').map(c => c.charCodeAt(0).toString(16).padStart(2, '0')).join(' ');
}

function uint32ToHexBytesLE(n) {
    return [
        (n & 0xff).toString(16).padStart(2, '0'),
        ((n >>> 8) & 0xff).toString(16).padStart(2, '0'),
        ((n >>> 16) & 0xff).toString(16).padStart(2, '0'),
        ((n >>> 24) & 0xff).toString(16).padStart(2, '0'),
    ].join(' ');
}

function findFunctionEntry(insideAddr) {
    // 从 insideAddr 反向最多扫 8KB，找 ia32 标准 prologue: 55 8B EC (push ebp; mov ebp, esp)
    // 验证：prologue 之前 1 字节是 padding (CC) / nop (90) / 函数尾 (C3 ret / C2 ret n)
    for (let off = 0; off < 8192; off++) {
        try {
            const c = insideAddr.sub(off);
            if (c.readU8() === 0x55 &&
                c.add(1).readU8() === 0x8B &&
                c.add(2).readU8() === 0xEC) {
                const before = c.sub(1).readU8();
                if (before === 0xCC || before === 0x90 || before === 0xC3 || before === 0xC2) {
                    return c;
                }
            }
        } catch (e) {
            return null;
        }
    }
    return null;
}

function locateByStringScan(mod) {
    const needle = 'MatchLogicPrivate::queryMatchAvailable';
    const needleHex = asciiToHexPattern(needle);

    // 扫所有可读段找字符串
    let strAddr = null;
    const readableRanges = mod.enumerateRanges('r--');
    for (let i = 0; i < readableRanges.length; i++) {
        const r = readableRanges[i];
        try {
            const matches = Memory.scanSync(r.base, r.size, needleHex);
            if (matches.length > 0) {
                strAddr = matches[0].address;
                log('scan-string', { at: strAddr.toString(), needle: needle });
                break;
            }
        } catch (e) { /* skip range */ }
    }
    if (!strAddr) {
        log('scan-fail', { msg: 'string not found in any r-- range' });
        return null;
    }

    // 字符串地址的 4 字节小端，作为 imm32 操作数搜
    const strAddrInt = parseInt(strAddr.toString(), 16);
    const xrefHex = uint32ToHexBytesLE(strAddrInt);

    // 扫所有 r-x 段找 xref
    const codeRanges = mod.enumerateRanges('r-x');
    const candidates = [];
    for (let i = 0; i < codeRanges.length; i++) {
        const r = codeRanges[i];
        try {
            const matches = Memory.scanSync(r.base, r.size, xrefHex);
            for (let j = 0; j < matches.length; j++) {
                candidates.push(matches[j].address);
            }
        } catch (e) { /* skip range */ }
    }
    log('scan-xref', { count: candidates.length });
    if (candidates.length === 0) return null;

    // 对每个 xref 试找函数入口
    for (let i = 0; i < candidates.length; i++) {
        const xrefAddr = candidates[i];
        let opcode;
        try { opcode = xrefAddr.sub(1).readU8(); } catch (e) { continue; }
        // 0x68 = PUSH imm32, 0xB8-0xBF = MOV r32, imm32
        if (opcode !== 0x68 && (opcode < 0xB8 || opcode > 0xBF)) continue;
        const insideAddr = xrefAddr.sub(1);
        const funcEntry = findFunctionEntry(insideAddr);
        if (funcEntry) {
            return {
                addr: funcEntry,
                source: 'string-scan',
                symbol: '<inferred from "' + needle + '" xref>',
            };
        }
    }
    return null;
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

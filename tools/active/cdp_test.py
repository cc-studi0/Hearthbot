"""
CDP 测试: 验证通过 JS hook 能否控制推荐打法显示

测试方法: 在能用推荐的段位, 调用 onShowModule("ladderRecommend", false)
如果推荐消失了 → 证明 hook 可行 → 反过来强制 true 就能绕过限制

用法:
  python cdp_test.py hide    — 隐藏推荐 (验证控制力)
  python cdp_test.py show    — 恢复推荐
  python cdp_test.py status  — 查看当前状态
  python cdp_test.py hook    — 安装守护 hook (强制推荐始终可用)
"""
import json
import sys
import urllib.request
import asyncio

try:
    import websockets
except ImportError:
    print("[!] 需要安装 websockets: pip install websockets")
    sys.exit(1)


def get_pages():
    data = urllib.request.urlopen('http://127.0.0.1:9222/json').read()
    return json.loads(data)


async def find_active_page():
    """找到 onShowModule 存在的页面（需要在对局中）"""
    pages = get_pages()
    for p in pages:
        ws_url = p.get('webSocketDebuggerUrl', '')
        if not ws_url:
            continue
        try:
            async with websockets.connect(ws_url, max_size=10*1024*1024) as ws:
                await ws.send(json.dumps({
                    'id': 1,
                    'method': 'Runtime.evaluate',
                    'params': {
                        'expression': 'typeof window.onShowModule !== "undefined" || typeof window.onUpdateLadderActionRecommend !== "undefined"',
                        'returnByValue': True,
                    }
                }))
                resp = json.loads(await ws.recv())
                val = resp.get('result', {}).get('result', {}).get('value', False)
                if val:
                    return p
        except:
            pass
    # fallback: 返回第一个 analysis 页面
    for p in pages:
        if 'analysis' in p.get('url', ''):
            return p
    return None


async def cdp_eval(ws_url, expression):
    """通过 CDP 执行 JS 并返回结果"""
    async with websockets.connect(ws_url, max_size=10*1024*1024) as ws:
        await ws.send(json.dumps({
            'id': 1,
            'method': 'Runtime.evaluate',
            'params': {
                'expression': expression,
                'returnByValue': True,
                'awaitPromise': False,
            }
        }))
        resp = json.loads(await ws.recv())
        result = resp.get('result', {}).get('result', {})
        return result.get('value', result.get('description', ''))


async def cmd_status(ws_url):
    """查看当前推荐状态"""
    result = await cdp_eval(ws_url, '''
        (function(){
            var r = {};
            r.onShowModule = typeof window.onShowModule;
            r.onSwitchModule = typeof window.onSwitchModule;
            r.onUpdateLadderActionRecommend = typeof window.onUpdateLadderActionRecommend;

            // 查看推荐面板是否可见
            var el = document.querySelector('.recommend-playstyle-wrapper');
            r.recommendVisible = el ? 'YES (元素存在)' : 'NO (元素不存在)';

            // 查看推荐 tab 是否显示
            var tabs = document.querySelectorAll('.cemetery-mode li');
            tabs.forEach(function(t, i){
                r['tab_' + i] = t.textContent.substring(0, 20) +
                    (t.classList.contains('active') ? ' [激活]' : '') +
                    (t.classList.contains('hidden') ? ' [隐藏]' : ' [可见]');
            });

            // 查看 hook 状态
            r.hookInstalled = !!window.__medalSpoof_hooked;

            return JSON.stringify(r, null, 2);
        })()
    ''')
    print(result)


async def cmd_hide(ws_url):
    """隐藏推荐 (验证控制力)"""
    result = await cdp_eval(ws_url, '''
        (function(){
            if (window.onShowModule) {
                window.onShowModule("ladderRecommend", false);
                return "已调用 onShowModule('ladderRecommend', false)";
            } else {
                return "onShowModule 不存在";
            }
        })()
    ''')
    print(f"[*] {result}")


async def cmd_show(ws_url):
    """恢复推荐"""
    result = await cdp_eval(ws_url, '''
        (function(){
            if (window.onShowModule) {
                window.onShowModule("ladderRecommend", true);
                return "已调用 onShowModule('ladderRecommend', true)";
            } else {
                return "onShowModule 不存在";
            }
        })()
    ''')
    print(f"[*] {result}")


async def cmd_hook(ws_url):
    """安装守护 hook: 拦截 + 记录所有 C++ → JS 的段位相关调用"""
    result = await cdp_eval(ws_url, '''
        (function(){
            if (window.__medalSpoof_hooked) {
                return "Hook 已经安装过了 (用 status 查看日志)";
            }

            window.__medalSpoof_log = [];
            function log(msg) {
                var entry = new Date().toISOString().substring(11,19) + " " + msg;
                window.__medalSpoof_log.push(entry);
                if (window.__medalSpoof_log.length > 100) window.__medalSpoof_log.shift();
                console.log("[MedalSpoof] " + msg);
            }

            // ── Hook onShowModule ──
            var origShowModule = window.onShowModule;
            window.onShowModule = function(moduleName, visible) {
                log("onShowModule('" + moduleName + "', " + visible + ")");
                if (moduleName === "ladderRecommend") {
                    // 强制 true
                    log("  → 强制改为 true");
                    if (origShowModule) origShowModule("ladderRecommend", true);
                    return;
                }
                if (origShowModule) origShowModule(moduleName, visible);
            };

            // ── Hook onUpdateLadderActionRecommend ──
            window.__medalSpoof_dataCount = 0;
            var origUpdate = window.onUpdateLadderActionRecommend;

            function wrapUpdate(fn) {
                return function(data) {
                    window.__medalSpoof_dataCount++;
                    window.__medalSpoof_lastData = data;
                    window.__medalSpoof_lastTime = new Date().toISOString();
                    var preview = data ? data.substring(0, 80) : "(空)";
                    log("onUpdateLadderActionRecommend #" + window.__medalSpoof_dataCount + " len=" + (data?data.length:0) + " " + preview);
                    if (fn) return fn(data);
                };
            }

            // 替换当前函数
            window.onUpdateLadderActionRecommend = wrapUpdate(origUpdate);

            // 拦截未来的赋值 (React useEffect 会重新设置这个函数)
            var currentFn = window.onUpdateLadderActionRecommend;
            Object.defineProperty(window, 'onUpdateLadderActionRecommend', {
                get: function() { return currentFn; },
                set: function(fn) {
                    log("onUpdateLadderActionRecommend 被重新赋值");
                    currentFn = wrapUpdate(fn);
                },
                configurable: true
            });

            // ── Hook onSwitchModule ──
            var origSwitch = window.onSwitchModule;
            window.onSwitchModule = function(moduleName) {
                log("onSwitchModule('" + moduleName + "')");
                if (origSwitch) origSwitch(moduleName);
            };

            window.__medalSpoof_hooked = true;
            log("所有 Hook 安装完成");
            return "Hook 安装成功!\\n- onShowModule: ladderRecommend 强制 true\\n- onUpdateLadderActionRecommend: 记录所有数据\\n- onSwitchModule: 记录调用";
        })()
    ''')
    print(f"[+] {result}")


async def cmd_check_data(ws_url):
    """查看 hook 日志和收到的数据"""
    result = await cdp_eval(ws_url, '''
        JSON.stringify({
            hooked: !!window.__medalSpoof_hooked,
            dataCount: window.__medalSpoof_dataCount || 0,
            lastDataTime: window.__medalSpoof_lastTime || "从未收到",
            lastDataPreview: window.__medalSpoof_lastData ? window.__medalSpoof_lastData.substring(0, 300) : "无数据",
            recentLog: (window.__medalSpoof_log || []).slice(-15)
        }, null, 2)
    ''')
    print(result)


async def main():
    cmd = sys.argv[1] if len(sys.argv) > 1 else 'status'

    page = await find_active_page()
    if not page:
        print("[!] 找不到 analysis 页面, 盒子可能未运行或未在游戏中")
        return

    ws_url = page['webSocketDebuggerUrl']
    print(f"[+] 连接: {page['url'][:60]}")

    if cmd == 'status':
        await cmd_status(ws_url)
    elif cmd == 'hide':
        await cmd_hide(ws_url)
    elif cmd == 'show':
        await cmd_show(ws_url)
    elif cmd == 'hook':
        await cmd_hook(ws_url)
    elif cmd == 'data':
        await cmd_check_data(ws_url)
    else:
        print(f"未知命令: {cmd}")
        print("用法: python cdp_test.py [status|hide|show|hook|data]")


if __name__ == '__main__':
    asyncio.run(main())

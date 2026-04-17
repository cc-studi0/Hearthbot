"""盒子受限侦察 Frida 启动器。

用法:
    python Scripts/recon_hsbox.py \
        --script tools/active/hsbox_limit_recon.js \
        --output docs/superpowers/recon/raw/xxx.log \
        --duration 60

由 Scripts/recon_hsbox.ps1 调度。使用 frida-python SDK 附加 HSAng.exe，
以 send() 方式收集脚本消息，写入指定 log 文件。
"""
import argparse
import json
import sys
import time
from pathlib import Path

import frida


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--script', required=True, help='Frida JS 脚本路径')
    ap.add_argument('--output', required=True, help='log 输出文件')
    ap.add_argument('--duration', type=int, default=60, help='录制秒数')
    ap.add_argument('--target', default='HSAng.exe', help='目标进程名')
    ap.add_argument('--wait', type=float, default=0, help='target 不存在时等待秒数（启动期侦察用）')
    args = ap.parse_args()

    script_path = Path(args.script)
    out_path = Path(args.output)
    out_path.parent.mkdir(parents=True, exist_ok=True)

    if not script_path.exists():
        print(f'[FATAL] 找不到脚本: {script_path}', file=sys.stderr)
        return 2

    with open(script_path, 'r', encoding='utf-8') as f:
        src = f.read()

    # 等待目标进程出现（最多 wait 秒），便于"启动期"侦察
    waited = 0
    wait_max = args.wait
    session = None
    while True:
        try:
            session = frida.attach(args.target)
            break
        except frida.ProcessNotFoundError:
            if waited >= wait_max:
                print(f'[FATAL] 进程未找到 (等待 {wait_max}s): {args.target}', file=sys.stderr)
                return 3
            if waited == 0:
                print(f'[WAIT] 等待 {args.target} 启动 (最多 {wait_max}s)...', flush=True)
            time.sleep(0.2)
            waited += 0.2
        except frida.PermissionDeniedError:
            print('[FATAL] 权限不足，请用管理员运行', file=sys.stderr)
            return 4

    out_fp = open(out_path, 'w', encoding='utf-8', buffering=1)
    counters = {'send': 0, 'error': 0}

    def on_message(message, _data):
        try:
            if message['type'] == 'send':
                line = json.dumps(message['payload'], ensure_ascii=False)
                out_fp.write(line + '\n')
                counters['send'] += 1
            elif message['type'] == 'error':
                err = {
                    'tag': 'frida-error',
                    'desc': message.get('description', ''),
                    'stack': message.get('stack', ''),
                    'fileName': message.get('fileName', ''),
                    'lineNumber': message.get('lineNumber', 0),
                }
                out_fp.write(json.dumps(err, ensure_ascii=False) + '\n')
                counters['error'] += 1
        except Exception as ex:
            out_fp.write(json.dumps({'tag': 'py-on-message-error', 'err': str(ex)}, ensure_ascii=False) + '\n')

    script = session.create_script(src)
    script.on('message', on_message)
    script.load()

    print(f'[OK] Frida 已附加 PID={session._impl.pid if hasattr(session, "_impl") else "?"}, 录制 {args.duration} 秒...', flush=True)

    try:
        # 分段 sleep 以便响应 Ctrl+C 与进度汇报
        remaining = args.duration
        while remaining > 0:
            chunk = min(10, remaining)
            time.sleep(chunk)
            remaining -= chunk
            print(f'[...] 已录制 {args.duration - remaining}/{args.duration}s | send={counters["send"]} error={counters["error"]}', flush=True)
    except KeyboardInterrupt:
        print('[!] Ctrl+C 中断', file=sys.stderr)
    finally:
        try:
            session.detach()
        except Exception:
            pass
        out_fp.flush()
        out_fp.close()

    print(f'[DONE] send={counters["send"]} error={counters["error"]} -> {out_path}', flush=True)
    return 0


if __name__ == '__main__':
    sys.exit(main())

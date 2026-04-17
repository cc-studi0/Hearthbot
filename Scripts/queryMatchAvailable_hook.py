"""盒子 queryMatchAvailable hook 常驻启动器。

由 BotMain.HsBoxLimitBypass 通过 Process.Start 拉起。
attach HSAng.exe + 加载 Frida JS hook，监听 detach 事件自动重连。
"""
import argparse
import json
import sys
import threading
import time

import frida


HEARTBEAT_SEC = 30
RECONNECT_BACKOFF_SEC = 5
ATTACH_TIMEOUT_SEC = 300
TARGET_PROCESS = 'HSAng.exe'


class HookSession:
    def __init__(self, script_path):
        self.script_path = script_path
        self.session = None
        self.script = None
        self.hits = 0
        self.forced = 0
        self.fatal = False
        self.detached_event = threading.Event()
        self.lock = threading.Lock()

    def attach_with_retry(self):
        waited = 0.0
        while waited < ATTACH_TIMEOUT_SEC:
            try:
                self.session = frida.attach(TARGET_PROCESS)
                return True
            except frida.ProcessNotFoundError:
                if waited == 0:
                    print(json.dumps({'tag': 'wait-target', 'msg': f'waiting for {TARGET_PROCESS}'}), flush=True)
                time.sleep(0.2)
                waited += 0.2
            except frida.PermissionDeniedError:
                print(json.dumps({'tag': 'fatal', 'msg': 'permission denied (run as admin)'}), flush=True)
                return False
        print(json.dumps({'tag': 'fatal', 'msg': f'attach timeout after {ATTACH_TIMEOUT_SEC}s'}), flush=True)
        return False

    def load_script(self):
        with open(self.script_path, 'r', encoding='utf-8') as f:
            src = f.read()
        self.script = self.session.create_script(src)
        self.script.on('message', self._on_message)
        self.session.on('detached', self._on_detached)
        self.script.load()

    def _on_message(self, message, _data):
        try:
            if message['type'] == 'send':
                payload = message['payload']
                tag = payload.get('tag') if isinstance(payload, dict) else None
                if tag == 'patch-stat' and isinstance(payload.get('payload'), dict):
                    p = payload['payload']
                    with self.lock:
                        self.hits = p.get('hits', self.hits)
                        self.forced = p.get('forced', self.forced)
                if tag == 'fatal':
                    with self.lock:
                        self.fatal = True
                print(json.dumps(payload, ensure_ascii=False), flush=True)
            elif message['type'] == 'error':
                err = {'tag': 'frida-error',
                       'desc': message.get('description', ''),
                       'stack': message.get('stack', '')}
                print(json.dumps(err, ensure_ascii=False), flush=True)
        except Exception as ex:
            print(json.dumps({'tag': 'py-on-message-error', 'err': str(ex)}), flush=True)

    def _on_detached(self, reason, _crash):
        print(json.dumps({'tag': 'detached', 'reason': str(reason)}), flush=True)
        self.detached_event.set()

    def heartbeat_loop(self, stop_event):
        while not stop_event.wait(HEARTBEAT_SEC):
            with self.lock:
                payload = {'tag': 'heartbeat', 'hits': self.hits, 'forced': self.forced}
            print(json.dumps(payload), flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--script', default='tools/active/hsbox_querymatch_hook.js')
    args = ap.parse_args()

    stop_event = threading.Event()
    while not stop_event.is_set():
        sess = HookSession(args.script)
        if not sess.attach_with_retry():
            return 2
        try:
            sess.load_script()
        except Exception as ex:
            print(json.dumps({'tag': 'fatal', 'msg': f'load script failed: {ex}'}), flush=True)
            return 3

        hb_stop = threading.Event()
        hb_thread = threading.Thread(target=sess.heartbeat_loop, args=(hb_stop,), daemon=True)
        hb_thread.start()

        # 等 detach 或 fatal 或 stdin EOF
        while not sess.detached_event.is_set():
            with sess.lock:
                fatal = sess.fatal
            if fatal:
                print(json.dumps({'tag': 'exit', 'reason': 'fatal-from-js'}), flush=True)
                hb_stop.set()
                return 2
            if sys.stdin.closed:
                print(json.dumps({'tag': 'exit', 'reason': 'stdin-closed'}), flush=True)
                hb_stop.set()
                return 0
            time.sleep(0.5)

        hb_stop.set()
        hb_thread.join(timeout=1)

        try:
            sess.session.detach()
        except Exception:
            pass

        print(json.dumps({'tag': 'reconnecting', 'after_sec': RECONNECT_BACKOFF_SEC}), flush=True)
        time.sleep(RECONNECT_BACKOFF_SEC)

    return 0


if __name__ == '__main__':
    sys.exit(main())

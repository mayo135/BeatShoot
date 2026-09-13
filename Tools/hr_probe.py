#!/usr/bin/env python3
"""
ブリッジが本当に送信できているかを確認するツール。

    python3 hr_probe.py

UDP 5005 を待ち受けて、届いたパケットをそのまま表示します。
ここに数字が出れば「ブリッジ → ネットワーク」までは正常です。
その場合に Unity 側に出ないなら、原因は Unity 側です。

注意: Unity を再生中は Unity がポートを掴んでいるため、
      このツールは起動できません。Unity を停止してから実行してください。
"""
import socket
import sys
import time

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 5005

try:
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind(("0.0.0.0", PORT))
except OSError as e:
    print(f"UDP {PORT} を開けませんでした: {e}")
    print("Unity を再生中だとポートが使われています。Unity を停止してから実行してください。")
    sys.exit(1)

sock.settimeout(1.0)
print(f"UDP {PORT} で待受中。ブリッジからのパケットを表示します。Ctrl+C で終了。\n")

count = 0
last = time.time()
try:
    while True:
        try:
            data, addr = sock.recvfrom(1024)
            count += 1
            print(f"  [{count:4d}] {addr[0]} → {data.decode('ascii', 'replace').strip()}")
            last = time.time()
        except socket.timeout:
            if count == 0 and time.time() - last > 10:
                print("  ...まだ何も届いていません。hr_bridge.py が動いているか確認してください。")
                last = time.time()
except KeyboardInterrupt:
    print(f"\n終了しました。合計 {count} 件受信。")

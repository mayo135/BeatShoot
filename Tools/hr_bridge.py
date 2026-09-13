#!/usr/bin/env python3
"""
スマートウォッチ → Unity 心拍ブリッジ

BLE の標準心拍サービス(0x180D)から BPM を受け取り、UDP で Unity に流します。
Unity 側は HeartRateUdpReceiver がこれを受けます。

使い方:
    pip3 install bleak

    # アドレスを指定して起動（hr_scan.py で調べたもの）
    python3 hr_bridge.py 76CBFCFE-D485-2142-F798-E8EDD62F4064

    # アドレス省略時は心拍サービスを持つ機器を自動で探します
    python3 hr_bridge.py

    # 送信先を変える場合
    python3 hr_bridge.py <アドレス> --host 127.0.0.1 --port 5005

Unity を再生する前に起動しておいてください。
時計側で心拍計測を開始しておく必要があります（裏面の緑 LED が点灯）。
Ctrl+C で終了します。
"""
import argparse
import asyncio
import socket
import struct
import sys
import time

from bleak import BleakClient, BleakScanner
from bleak.exc import BleakError

HR_SERVICE = "0000180d-0000-1000-8000-00805f9b34fb"
HR_MEASURE = "00002a37-0000-1000-8000-00805f9b34fb"


def parse_bpm(data: bytearray) -> int:
    """Heart Rate Measurement (0x2A37) を BPM にする。"""
    flags = data[0]
    if flags & 0x01:
        return struct.unpack_from("<H", data, 1)[0]   # 16bit
    return data[1]                                     # 8bit


async def find_watch():
    print("心拍サービスを持つ機器をさがします...")
    devices = await BleakScanner.discover(timeout=8.0, return_adv=True)
    for addr, (dev, adv) in devices.items():
        if any(u.lower().startswith("0000180d") for u in adv.service_uuids):
            name = dev.name or adv.local_name or "(名前なし)"
            print(f"見つかりました: {name}  {addr}")
            return addr
    return None


async def run(address, host, port):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    dest = (host, port)
    stats = {"count": 0, "last": 0.0, "bpm": 0}

    if address is None:
        address = await find_watch()
        if address is None:
            print("心拍サービスを持つ機器が見つかりませんでした。")
            print("時計の画面を点灯させ、スマホのアプリを終了してから再実行してください。")
            return

    disconnected = asyncio.Event()

    def on_disconnect(_):
        print("\n切断されました。5 秒後に再接続します。")
        disconnected.set()

    def on_hr(_, data: bytearray):
        bpm = parse_bpm(data)
        if bpm <= 0 or bpm > 250:      # 明らかな異常値は捨てる
            return
        sock.sendto(str(bpm).encode("ascii"), dest)
        stats["count"] += 1
        stats["bpm"] = bpm
        now = time.time()
        # 端末を埋めないよう、値が変わったときと 5 秒ごとだけ表示する
        if bpm != stats.get("shown") or now - stats["last"] > 5.0:
            print(f"  BPM {bpm:3d}  →  {host}:{port}   (送信 {stats['count']} 件)")
            stats["shown"] = bpm
            stats["last"] = now

    print(f"{address} に接続します...")
    async with BleakClient(address, timeout=20.0,
                           disconnected_callback=on_disconnect) as client:
        if client.services.get_service(HR_SERVICE) is None:
            print("この機器に標準の心拍サービスがありません。")
            return

        await client.start_notify(HR_MEASURE, on_hr)
        print(f"接続しました。{host}:{port} へ送信中です。Ctrl+C で終了します。")
        print("時計本体で心拍計測を開始してください（裏面の緑 LED が点灯）。\n")

        last_warn = time.time()
        while not disconnected.is_set():
            await asyncio.sleep(1.0)
            # しばらく通知が来ないときだけ知らせる
            if stats["count"] == 0 and time.time() - last_warn > 15.0:
                print("  ...通知がまだ来ていません。時計の心拍計測をオンにしてください。")
                last_warn = time.time()


async def main_loop(address, host, port):
    while True:
        try:
            await run(address, host, port)
        except BleakError as e:
            print(f"\nBleakError: {e}")
        except asyncio.TimeoutError:
            print("\n接続がタイムアウトしました。")
        print("5 秒後に再接続します...(Ctrl+C で終了)")
        await asyncio.sleep(5.0)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("address", nargs="?", default=None, help="BLE アドレス（省略時は自動検索）")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=5005)
    args = ap.parse_args()

    try:
        asyncio.run(main_loop(args.address, args.host, args.port))
    except KeyboardInterrupt:
        print("\n終了しました。")


if __name__ == "__main__":
    main()

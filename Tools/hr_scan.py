#!/usr/bin/env python3
"""
BLE スマートウォッチ調査ツール（P18 などの心拍計を Unity につなぐ前の下調べ）

使い方:
    pip3 install bleak

    # 1) 周囲の BLE 機器を一覧する
    python3 hr_scan.py

    # 2) 目当ての機器の中身を調べる（アドレスは 1) の出力から）
    python3 hr_scan.py XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX

注意:
  * スマホの FitPro などが接続中だと繋がりません。アプリを終了し、
    可能なら OS の Bluetooth 設定からも切断してから実行してください。
  * macOS は初回にターミナルへ Bluetooth 権限が必要です。
    「システム設定 > プライバシーとセキュリティ > Bluetooth」を確認してください。
"""
import asyncio
import struct
import sys

from bleak import BleakClient, BleakScanner

HR_SERVICE = "0000180d-0000-1000-8000-00805f9b34fb"   # Heart Rate Service
HR_MEASURE = "00002a37-0000-1000-8000-00805f9b34fb"   # Heart Rate Measurement

# よく使われるサービスに名前を付けておく（見分けやすくするため）
KNOWN = {
    "00001800": "Generic Access",
    "00001801": "Generic Attribute",
    "0000180a": "Device Information",
    "0000180d": "★ Heart Rate Service（標準の心拍）",
    "0000180f": "Battery",
    "0000fee7": "独自(Tencent/中華ウェアラブル系でよく見る)",
    "0000fff0": "独自(汎用シリアル風)",
    "0000ffe0": "独自(汎用シリアル風)",
    "6e400001": "Nordic UART (NUS) 独自通信",
    "0000af00": "独自(DaFit/FitPro 系で報告あり)",
    "0000feea": "独自(DaFit 系で報告あり)",
}


async def scan():
    print("BLE 機器を 8 秒間さがします...\n")
    devices = await BleakScanner.discover(timeout=8.0, return_adv=True)
    rows = []
    for addr, (dev, adv) in devices.items():
        name = dev.name or adv.local_name or "(名前なし)"
        rows.append((adv.rssi, name, addr, list(adv.service_uuids)))
    rows.sort(reverse=True)

    if not rows:
        print("見つかりませんでした。時計の画面を点灯させ、アプリを終了して再実行してください。")
        return

    print(f"{'RSSI':>5}  {'名前':<24} アドレス")
    print("-" * 78)
    for rssi, name, addr, uuids in rows:
        mark = "  ← 心拍サービスあり" if any(u.startswith("0000180d") for u in uuids) else ""
        print(f"{rssi:>5}  {name:<24} {addr}{mark}")

    print("\n次はこれを実行してください:")
    print(f"    python3 hr_scan.py <アドレス>")


LISTEN_SECONDS = 30.0


async def inspect(address):
    print(f"{address} に接続します...")
    async with BleakClient(address, timeout=20.0) as client:
        print("接続しました。サービス一覧:\n")

        has_hr = False
        for service in client.services:
            short = service.uuid[:8]
            label = KNOWN.get(short, "")
            print(f"[Service] {service.uuid}  {label}")
            if short == "0000180d":
                has_hr = True
            for ch in service.characteristics:
                props = ",".join(ch.properties)
                print(f"    {ch.uuid}  ({props})")
            print()

        if not has_hr:
            print("=" * 70)
            print("標準の Heart Rate Service (0x180D) はありませんでした。")
            print("独自プロトコルの可能性が高いです。上の一覧のうち")
            print("notify を持つ characteristic が心拍の通知先の候補です。")
            print("この出力をそのまま貼ってもらえれば、次の手を判断します。")
            return

        print("=" * 70)
        print(f"標準の心拍サービスがありました。{LISTEN_SECONDS:.0f} 秒間 受信します。")
        print("時計を手首に密着させ、時計本体で心拍計測を開始してください。")
        print("（裏面の緑の LED が光っていれば計測中です）\n")

        count = 0

        def on_hr(_, data: bytearray):
            nonlocal count
            count += 1
            flags = data[0]
            if flags & 0x01:
                bpm = struct.unpack_from("<H", data, 1)[0]   # 16bit
            else:
                bpm = data[1]                                 # 8bit
            print(f"  BPM = {bpm}    (raw: {data.hex()})")

        await client.start_notify(HR_MEASURE, on_hr)
        await asyncio.sleep(LISTEN_SECONDS)
        await client.stop_notify(HR_MEASURE)

        print()
        if count == 0:
            print("=" * 70)
            print("通知が 1 件も来ませんでした。購読はできているので、")
            print("時計側が心拍を配信していない状態です。次を試してください。")
            print()
            print("  1) 時計本体の心拍計測画面を開き、計測を開始した状態で再実行")
            print("     （裏面の緑 LED が点灯しているか確認）")
            print("  2) 連続計測モード（常時計測 / 24時間計測）を時計の設定でオンにする")
            print("  3) 受信時間を延ばす:  python3 hr_scan.py <アドレス> 60")
            print()
            print("それでも来ない場合は、0xFEEA の write 系 characteristic に")
            print("計測開始コマンドを送る必要があります（独自プロトコルの解析）。")
        else:
            rate = count / LISTEN_SECONDS
            print("=" * 70)
            print(f"{count} 件受信しました（約 {rate:.2f} 件/秒）。")
            print("この方式で Unity に繋げます。")
            if rate < 0.5:
                print()
                print("※ 更新が 2 秒に 1 回より遅いので、ゲーム側では")
                print("   受信値を補間して滑らかに使う必要があります。")


HELP_BT_OFF = """
------------------------------------------------------------------
Bluetooth が使えない状態です。macOS では次の2つが同じメッセージになります。

 1) Bluetooth 本体がオフ
    → メニューバー、または システム設定 > Bluetooth をオンに

 2) ターミナルに Bluetooth の権限が無い（よくある方）
    → システム設定 > プライバシーとセキュリティ > Bluetooth で
      「ターミナル」をオンにしたあと、ターミナルを Cmd+Q で
      完全に終了してから開き直してください。
      （ウィンドウを閉じるだけでは反映されません）

確認用:  system_profiler SPBluetoothDataType | grep -i Powered
------------------------------------------------------------------
"""


def main():
    from bleak.exc import BleakError
    try:
        if len(sys.argv) >= 2:
            asyncio.run(inspect(sys.argv[1]))
        else:
            asyncio.run(scan())
    except BleakError as e:
        msg = str(e)
        print(f"\nBleakError: {msg}")
        if "turned off" in msg or "not authorized" in msg or "unauthorized" in msg:
            print(HELP_BT_OFF)
        elif "not found" in msg.lower():
            print("\nその機器が見つかりません。時計の画面を点灯させ、"
                  "スマホのアプリを終了してから再実行してください。")
    except KeyboardInterrupt:
        print("\n中断しました。")


if __name__ == "__main__":
    main()

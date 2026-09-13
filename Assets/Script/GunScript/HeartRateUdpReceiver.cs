using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

/// <summary>
/// スマートウォッチの心拍を UDP で受け取り、HeartRateProvider に流し込みます。
/// 送信側は Tools/hr_bridge.py（Mac のターミナルで起動）。
///
/// ブリッジが動いていないときは何もしないので、
/// HeartRateProvider はシミュレーションのまま動きます。
/// 実機が繋がった瞬間に自動で External モードへ切り替わります。
///
/// 取り付け先: HeartRate オブジェクト
/// </summary>
[DisallowMultipleComponent]
public class HeartRateUdpReceiver : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("未設定なら同じオブジェクト／シーンから自動で探します")]
    public HeartRateProvider provider;

    [Header("通信")]
    [Tooltip("hr_bridge.py の --port と合わせる")]
    public int port = 5005;

    [Header("なめらかさ")]
    [Tooltip("実機は 1 秒に 1 回しか更新されないため、値を補間して滑らかに繋ぎます。"
           + "大きいほど実測値に速く追従します")]
    public float smoothing = 2.5f;

    [Tooltip("この秒数だけ受信が途絶えたら、シミュレーションに戻します")]
    public float timeoutSeconds = 8f;

    [Header("デバッグ")]
    [Tooltip("画面に接続状況を表示する")]
    public bool showDebugHud = true;

    [Header("状態（読み取り専用）")]
    [SerializeField] bool _connected;
    [SerializeField] float _lastReceivedBpm;
    [SerializeField] int _packetCount;

    /// <summary>実機の心拍を受信中か。</summary>
    public bool IsReceiving => _connected;

    UdpClient _udp;
    Thread _thread;
    volatile bool _running;

    readonly object _lock = new object();
    float _incoming = -1f;          // 受信スレッドが書く
    bool _hasIncoming;

    float _smoothed = -1f;
    float _lastPacketTime = -999f;
    HeartRateProvider.Mode _originalMode;
    bool _modeSwapped;

    void Awake()
    {
        if (!provider) provider = GetComponent<HeartRateProvider>();
        if (!provider) provider = FindAnyObjectByType<HeartRateProvider>();
        if (provider) _originalMode = provider.mode;
    }

    void OnEnable()
    {
        try
        {
            _udp = new UdpClient(port);
            _running = true;
            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "HeartRateUdp" };
            _thread.Start();
            Debug.Log($"[HeartRate] UDP {port} で待受を開始しました。"
                    + "Tools/hr_bridge.py を起動すると実機の心拍に切り替わります。");
        }
        catch (SocketException e)
        {
            // 他のアプリがポートを使っている場合など。ゲーム自体は続行させる
            Debug.LogWarning($"[HeartRate] UDP {port} を開けませんでした: {e.Message}");
            _udp = null;
        }
    }

    void OnDisable()
    {
        _running = false;
        try { _udp?.Close(); } catch { }   // Receive のブロックを解除する
        _udp = null;
        if (_thread != null && _thread.IsAlive) _thread.Join(200);
        _thread = null;
        RestoreMode();
    }

    // ---------------------------------------------------------------
    void ReceiveLoop()
    {
        var any = new IPEndPoint(IPAddress.Any, 0);
        while (_running)
        {
            try
            {
                byte[] data = _udp.Receive(ref any);
                string text = System.Text.Encoding.ASCII.GetString(data).Trim();
                if (float.TryParse(text, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture,
                                   out float bpm))
                {
                    if (bpm > 20f && bpm < 250f)
                    {
                        lock (_lock) { _incoming = bpm; _hasIncoming = true; }
                    }
                }
            }
            catch (ObjectDisposedException) { return; }   // Close() による正常終了
            catch (SocketException) { return; }
            catch (Exception e)
            {
                Debug.LogWarning($"[HeartRate] 受信エラー: {e.Message}");
                return;
            }
        }
    }

    // ---------------------------------------------------------------
    void Update()
    {
        if (provider == null) return;

        // --- 受信スレッドからの取り込み ---
        bool got = false;
        float value = 0f;
        lock (_lock)
        {
            if (_hasIncoming) { value = _incoming; _hasIncoming = false; got = true; }
        }

        if (got)
        {
            _lastReceivedBpm = value;
            _packetCount++;
            _lastPacketTime = Time.time;

            if (_smoothed < 0f) _smoothed = value;      // 初回はそのまま

            if (!_modeSwapped)
            {
                _originalMode = provider.mode;
                provider.mode = HeartRateProvider.Mode.External;
                _modeSwapped = true;
                _connected = true;
                Debug.Log($"[HeartRate] 実機に切り替えました（BPM {value:F0}）。");
            }
        }

        // --- 受信が途絶えたらシミュレーションへ戻す ---
        if (_connected && Time.time - _lastPacketTime > timeoutSeconds)
        {
            Debug.LogWarning("[HeartRate] 受信が途絶えたため、シミュレーションに戻します。");
            RestoreMode();
            return;
        }

        if (!_connected) return;

        // --- 1Hz の飛び飛びの値を滑らかに繋ぐ ---
        _smoothed = Mathf.Lerp(_smoothed, _lastReceivedBpm,
                               1f - Mathf.Exp(-smoothing * Time.deltaTime));
        provider.SetBpm(_smoothed);
    }

    // ---------------------------------------------------------------
    GUIStyle _style;

    void OnGUI()
    {
        if (!showDebugHud) return;
        if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 16 };

        string text;
        if (_udp == null)
        {
            GUI.color = new Color(1f, 0.45f, 0.4f);
            text = $"WATCH: UDP {port} を開けませんでした（他のアプリが使用中の可能性）";
        }
        else if (_connected)
        {
            GUI.color = new Color(0.4f, 1f, 0.6f);
            float age = Time.time - _lastPacketTime;
            text = $"WATCH: 接続中   実測 {_lastReceivedBpm:F0} BPM   "
                 + $"受信 {_packetCount} 件   最終 {age:F1} 秒前";
        }
        else
        {
            GUI.color = new Color(0.75f, 0.75f, 0.78f);
            text = $"WATCH: 待機中（UDP {port}）  ブリッジ未起動 → シミュレーションで動作中";
        }

        GUI.Label(new Rect(12, 52, 700, 24), text, _style);
        GUI.color = Color.white;
    }

    void RestoreMode()
    {
        if (!_modeSwapped || provider == null) { _connected = false; return; }
        provider.mode = _originalMode;
        _modeSwapped = false;
        _connected = false;
        _smoothed = -1f;
    }
}

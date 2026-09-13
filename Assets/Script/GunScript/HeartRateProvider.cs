using UnityEngine;

/// <summary>
/// 心拍数(BPM)の供給元。
/// M5Stack + PPG センサーが手に入るまでは、この中の変数／シミュレーションで代用します。
/// 実機をつないだら Mode を External にして、受信側から SetBpm() を呼ぶだけで差し替わります。
/// </summary>
[DisallowMultipleComponent]
public class HeartRateProvider : MonoBehaviour
{
    public enum Mode
    {
        Manual,     // Inspector の bpm をそのまま使う（手で動かして挙動を確認する用）
        Simulated,  // 発砲などで上がり、時間で落ち着く擬似心拍
        External,   // 外部（M5Stack など）から SetBpm() で与える
    }

    [Header("動作モード")]
    public Mode mode = Mode.Simulated;

    [Header("現在値")]
    [Tooltip("現在の心拍数。Manual モードではここを直接動かして試せます")]
    [Range(40f, 200f)] public float bpm = 72f;

    [Header("Simulated の設定")]
    [Tooltip("落ち着いているときの心拍数")]
    public float restingBpm = 68f;

    [Tooltip("上限")]
    public float maxBpm = 165f;

    [Tooltip("1秒あたりに restingBpm へ戻る量")]
    public float recoveryPerSecond = 7f;

    [Tooltip("1発撃つたびに上がる量（SniperShooter から呼ばれます）")]
    public float shotStress = 16f;

    [Tooltip("構えている間、1秒あたりに上がる量（緊張の表現）")]
    public float aimStressPerSecond = 2.5f;

    [Header("ゆらぎ")]
    [Tooltip("常に乗る細かい変動の幅[BPM]。0 にすると数値が固まります")]
    public float noiseAmplitude = 1.8f;
    public float noiseSpeed = 0.35f;

    /// <summary>ゆらぎ込みの現在 BPM。読み取りはこれを使ってください。</summary>
    public float Bpm { get; private set; }

    /// <summary>0(落ち着いている)〜 1(限界) の興奮度。</summary>
    public float Excitement01 =>
        Mathf.InverseLerp(restingBpm, maxBpm, Bpm);

    float _base;

    void Awake()
    {
        _base = bpm;
        Bpm = bpm;
    }

    void Update()
    {
        switch (mode)
        {
            case Mode.Manual:
                _base = bpm;
                break;

            case Mode.Simulated:
                _base = Mathf.MoveTowards(_base, restingBpm, recoveryPerSecond * Time.deltaTime);
                _base = Mathf.Clamp(_base, 40f, maxBpm);
                bpm = _base;                       // Inspector に反映
                break;

            case Mode.External:
                _base = bpm;                       // SetBpm() が bpm を書き換える
                break;
        }

        float n = (Mathf.PerlinNoise(Time.time * noiseSpeed, 11.3f) - 0.5f) * 2f;
        Bpm = Mathf.Max(30f, _base + n * noiseAmplitude);
    }

    // ---------------------------------------------------------------
    /// <summary>外部センサーから心拍数を渡す（M5Stack 受信側から呼ぶ）。</summary>
    public void SetBpm(float value)
    {
        bpm = value;
        _base = value;
    }

    /// <summary>心拍を上げる。発砲・被弾・ダッシュなどのイベントから呼ぶ。</summary>
    public void AddStress(float amount)
    {
        if (mode != Mode.Simulated) return;
        _base = Mathf.Clamp(_base + amount, 40f, maxBpm);
        bpm = _base;
    }

    /// <summary>構え続けている間の緊張。AimSway から毎フレーム呼ばれます。</summary>
    public void AddAimStress(float deltaTime)
    {
        AddStress(aimStressPerSecond * deltaTime);
    }
}

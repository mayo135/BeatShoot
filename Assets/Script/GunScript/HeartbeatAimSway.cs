using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 心拍数(BPM)に応じて照準を揺らす。GGO のバレットサークルのイメージ。
///
/// 照準点をバネでつながれた質点として扱い、そこに2つの力を加えます。
///   1. ゆらぎ … ノイズでゆっくり漂わせる力
///   2. 拍動   … BPM に同期した心拍波形の力（ドクン、ドクン）
/// 位置を直接動かさず「力」で動かすので、動きが途切れず滑らかに繋がります。
///
/// カメラの向きには一切触りません。画面は静止したまま照準だけが漂います。
///
/// 取り付け先: Main Camera
/// </summary>
[DisallowMultipleComponent]
public class HeartbeatAimSway : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("心拍数の供給元。未設定ならシーン内から自動で探します")]
    public HeartRateProvider heartRate;

    [Tooltip("構えたときにズームするカメラ。未設定なら自分の Camera を使います")]
    public Camera aimCamera;

    [Header("構える（右クリック）")]
    [Tooltip("構える／戻すのにかかる時間[秒]")]
    public float aimBlendTime = 0.18f;

    [Tooltip("構えていないときの揺れの倍率")]
    [Range(0f, 2f)] public float hipSwayMultiplier = 0.35f;

    [Header("ズーム")]
    public float hipFov = 60f;
    public float aimFov = 22f;

    [Header("揺れ")]
    [Tooltip("この BPM 以下なら「落ち着いている」")]
    public float calmBpm = 65f;

    [Tooltip("この BPM で揺れが最大になる")]
    public float hotBpm = 155f;

    [Tooltip("落ち着いているときの揺れ幅[度]")]
    public float swayCalm = 0.15f;

    [Tooltip("限界時の揺れ幅[度]")]
    public float swayHot = 2.2f;

    [Tooltip("漂う速さ。大きいほど落ち着きなく動く")]
    public float swaySpeed = 0.9f;

    [Tooltip("拍動の蹴りの強さ。大きいほど1拍ごとに大きく跳ねる")]
    public float heartbeatKick = 55f;

    [Tooltip("戻る強さ（バネ）。大きいほど中心に素早く戻る")]
    public float springStiffness = 26f;

    [Tooltip("揺れの収まりやすさ。小さいほど尾を引いて大きく振れる")]
    public float damping = 4.5f;

    [Header("バレットサークル（弾のばらつき）")]
    [Tooltip("落ち着いて構えたときの円の半径[度]")]
    public float spreadCalm = 0.06f;

    [Tooltip("限界時の円の半径[度]")]
    public float spreadHot = 0.85f;

    [Tooltip("構えていないときの倍率")]
    public float hipSpreadMultiplier = 7f;

    [Header("デバッグ")]
    public bool showDebugHud = true;

    // --- 外部から読める状態 -----------------------------------------
    public float CurrentBpm { get; private set; }
    public float AimBlend01 { get; private set; }     // 0=腰だめ 1=構え
    public float SwayDegrees { get; private set; }    // 中心からのズレ[度]
    public float BeatPhase01 { get; private set; }    // 0..1 拍動の位相
    public float Excitement01 { get; private set; }   // 0=落ち着き 1=限界
    public float SpreadDegrees { get; private set; }  // バレットサークルの半径[度]
    public bool IsAiming { get; set; }                // 外部入力から制御する場合はここに書く

    /// <summary>揺れを含んだ実際の照準方向（ワールド）。レティクルも弾道もこれを使います。</summary>
    public Vector3 AimDirection { get; private set; }

    // --- 内部状態 ---------------------------------------------------
    Vector2 _pos;      // 照準のズレ [度]
    Vector2 _vel;      // その速度  [度/秒]
    float _aimVel;
    float _beatPhase;

    void Awake()
    {
        AimDirection = transform.forward;
        if (!heartRate) heartRate = FindAnyObjectByType<HeartRateProvider>();
        if (!aimCamera) aimCamera = GetComponent<Camera>();
        if (hipFov < 1f) hipFov = aimCamera ? aimCamera.fieldOfView : 60f;
        if (aimCamera) aimCamera.fieldOfView = hipFov;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // ---------- 1. 構えの状態 ----------
        bool wantAim = IsAiming || AimHeld();
        AimBlend01 = Mathf.SmoothDamp(AimBlend01, wantAim ? 1f : 0f,
                                      ref _aimVel, Mathf.Max(0.01f, aimBlendTime));
        if (AimBlend01 > 0.999f) AimBlend01 = 1f;
        if (AimBlend01 < 0.001f) AimBlend01 = 0f;

        if (wantAim && heartRate) heartRate.AddAimStress(dt);

        // ---------- 2. 心拍 ----------
        CurrentBpm = heartRate ? heartRate.Bpm : 72f;
        _beatPhase += dt * (CurrentBpm / 60f);
        if (_beatPhase >= 1f) _beatPhase -= Mathf.Floor(_beatPhase);
        BeatPhase01 = _beatPhase;

        Excitement01 = Mathf.InverseLerp(calmBpm, hotBpm, CurrentBpm);
        float amp = Mathf.Lerp(swayCalm, swayHot, Excitement01);

        // ---------- 3. 力を合成してバネで動かす ----------
        // (a) ゆらぎ: 漂う目標地点
        float t = Time.time * swaySpeed;
        Vector2 wander = new Vector2(Mathf.PerlinNoise(t, 0.13f) - 0.5f,
                                     Mathf.PerlinNoise(0.57f, t) - 0.5f) * 2f * amp;

        // (b) 拍動: 心拍波形そのものを力として加える。
        //     波形が連続なので、位置も速度も途切れずに繋がる。
        float a = Mathf.PerlinNoise(Time.time * 0.13f, 3.7f) * Mathf.PI * 2f;
        Vector2 kickDir = new Vector2(Mathf.Cos(a) * 0.6f, Mathf.Sin(a) * 0.5f + 0.5f);
        Vector2 kick = kickDir * (Heartbeat(_beatPhase) * heartbeatKick * amp);

        // (c) バネ・ダンパー
        Vector2 accel = (wander - _pos) * springStiffness - _vel * damping + kick;
        _vel += accel * dt;
        _pos += _vel * dt;

        // 構えていないときは大きめに揺らす
        Vector2 shown = _pos * Mathf.Lerp(hipSwayMultiplier, 1f, AimBlend01);
        SwayDegrees = shown.magnitude;

        // ---------- 4. 照準方向 ----------
        AimDirection = transform.rotation * Quaternion.Euler(-shown.y, shown.x, 0f) * Vector3.forward;

        // ---------- 5. バレットサークル ----------
        float aimed = Mathf.Lerp(spreadCalm, spreadHot, Excitement01);
        SpreadDegrees = Mathf.Lerp(aimed * hipSpreadMultiplier, aimed, AimBlend01);

        // ---------- 6. ズーム ----------
        if (aimCamera) aimCamera.fieldOfView = Mathf.Lerp(hipFov, aimFov, AimBlend01);
    }

    // ---------------------------------------------------------------
    /// <summary>実際に弾が飛ぶ向き。バレットサークル内にランダムに散ります。</summary>
    public Vector3 GetShotDirection()
    {
        Vector3 dir = AimDirection;
        if (SpreadDegrees <= 0f) return dir;

        float r = SpreadDegrees * Mathf.Sqrt(Random.value);
        float a = Random.value * Mathf.PI * 2f;
        Vector3 axis = transform.right * Mathf.Cos(a) + transform.up * Mathf.Sin(a);
        return Quaternion.AngleAxis(r, axis) * dir;
    }

    /// <summary>1拍ぶんの心拍波形（0..1 の位相 → 0..1 強度）。ドクン、ドクンの二峰。</summary>
    static float Heartbeat(float p)
    {
        float a = Mathf.Exp(-Mathf.Pow((p - 0.10f) / 0.055f, 2f));          // 収縮期（大）
        float b = 0.45f * Mathf.Exp(-Mathf.Pow((p - 0.30f) / 0.075f, 2f));  // second sound（小）
        return a + b;
    }

    static bool AimHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
        return Input.GetMouseButton(1);
#endif
    }

    // ---------------------------------------------------------------
    GUIStyle _hudStyle;

    void OnGUI()
    {
        if (!showDebugHud) return;
        if (_hudStyle == null) _hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 16 };
        GUI.color = Color.white;
        GUI.Label(new Rect(12, 8, 560, 24),
            string.Format("BPM {0,5:F1}   Sway {1,5:F2} deg   Circle {2,5:F2} deg   Aim {3,3:P0}",
                          CurrentBpm, SwayDegrees, SpreadDegrees, AimBlend01), _hudStyle);
    }
}

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 射撃と得点集計のサンプル。
/// Muzzle（銃口）から Raycast を飛ばし、当たった標的の TargetScoreZones で得点を判定します。
///
/// 新旧どちらの入力方式でも動きます（Input System / 旧 Input Manager を自動判別）。
///
/// 使い方:
///   1. 銃モデルのルート（または CameraRig）にこのコンポーネントを追加
///   2. muzzle に銃口の Transform、aimSource に狙う向きの基準（通常はカメラ）を割り当て
///   3. targetMask に標的用のレイヤーを指定
/// </summary>
public class SniperShooter : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("弾道の始点。銃モデル直下の Muzzle (0, 0, 1.05)")]
    public Transform muzzle;

    [Tooltip("狙う向きの基準。心拍ブレを適用しているカメラ／リグを指定")]
    public Transform aimSource;

    [Header("弾道")]
    public float maxDistance = 800f;
    public LayerMask targetMask = ~0;

    [Header("曳光弾（トレーサー）")]
    [Tooltip("弾の軌跡を表示する")]
    public bool showTracer = true;

    [Tooltip("弾速[m/s]。小さいほど軌跡をゆっくり追える")]
    public float tracerSpeed = 260f;

    [Tooltip("光る筋の長さ[m]")]
    public float tracerLength = 12f;

    [Tooltip("筋の太さ[m]")]
    public float tracerWidth = 0.05f;

    public Color tracerColor = new Color(1f, 0.85f, 0.35f, 1f);

    [Header("演出")]
    [Tooltip("命中痕（Quad など）。標的の前面に貼り付ける")]
    public GameObject bulletHolePrefab;
    public float bulletHoleLife = 30f;

    [Header("心拍連動")]
    [Tooltip("発砲で心拍を上げる先。未設定ならシーン内から自動で探します")]
    public HeartRateProvider heartRate;

    [Header("集計")]
    public int totalScore;
    public int shotCount;

    HeartbeatAimSway _sway;

    void Awake()
    {
        if (!heartRate) heartRate = FindAnyObjectByType<HeartRateProvider>();
    }

    void ShowTracer(Vector3 start, Vector3 end)
    {
        if (!showTracer) return;
        BulletTracer.Spawn(start, end, tracerSpeed, tracerLength, tracerWidth, tracerColor);
    }

    /// <summary>1発撃つ。</summary>
    public void Fire()
    {
        shotCount++;
        if (heartRate) heartRate.AddStress(heartRate.shotStress);

        // 弾道は「狙っている視点」から飛ばす（銃口から飛ばすと画面中央とズレるため）。
        // aimSource が未設定なら銃口、それも無ければ自分自身の向きを使う。
        Transform src = aimSource ? aimSource : (muzzle ? muzzle : transform);
        Vector3 origin = src.position;

        // 心拍による揺れ・バレットサークルがあればそれを反映した向きで撃つ
        if (!_sway) _sway = src.GetComponent<HeartbeatAimSway>();
        Vector3 dir = _sway ? _sway.GetShotDirection() : src.forward;

        // 軌跡は見た目どおり銃口から出す（判定はカメラ基準のまま）
        Vector3 tracerStart = muzzle ? muzzle.position : origin;

        if (!Physics.Raycast(origin, dir, out RaycastHit hit, maxDistance, targetMask))
        {
            ShowTracer(tracerStart, origin + dir * maxDistance);
            Debug.Log("Miss (何にも当たらず)");
            return;
        }

        ShowTracer(tracerStart, hit.point);

        // 標的本体、または親についている TargetScoreZones を探す
        var zones = hit.collider.GetComponentInParent<TargetScoreZones>();
        if (zones == null)
        {
            Debug.Log($"Hit {hit.collider.name}（標的ではない）");
            return;
        }

        var result = zones.Hit(hit.point);
        totalScore += result.score;

        Debug.Log($"{result.zone} : {result.score} pt " +
                  $"(local {result.local.x:F3}, {result.local.y:F3}) / 合計 {totalScore}");

        if (bulletHolePrefab != null && result.onFront)
        {
            var hole = Instantiate(
                bulletHolePrefab,
                hit.point + hit.normal * 0.002f,
                Quaternion.LookRotation(-hit.normal, Vector3.up));
            hole.transform.SetParent(zones.transform, true);
            if (bulletHoleLife > 0f) Destroy(hole, bulletHoleLife);
        }
    }

    void Update()
    {
        // 動作確認用。実際のゲームでは自前の入力処理から Fire() を呼んでください。
        if (FirePressedThisFrame()) Fire();
    }

    /// <summary>この1フレームで「撃つ」入力があったか（新旧の入力方式に両対応）。</summary>
    bool FirePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        // --- Input System パッケージ（Unity 6 の既定） ---
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        // --- 旧 Input Manager ---
        return Input.GetMouseButtonDown(0);
#endif
    }
}

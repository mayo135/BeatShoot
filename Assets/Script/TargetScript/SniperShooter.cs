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

    [Header("演出")]
    [Tooltip("命中痕（Quad など）。標的の前面に貼り付ける")]
    public GameObject bulletHolePrefab;
    public float bulletHoleLife = 30f;

    [Header("集計")]
    public int totalScore;
    public int shotCount;

    /// <summary>1発撃つ。</summary>
    public void Fire()
    {
        shotCount++;

        // 弾道は「狙っている視点」から飛ばす（銃口から飛ばすと画面中央とズレるため）。
        // aimSource が未設定なら銃口、それも無ければ自分自身の向きを使う。
        Transform src = aimSource ? aimSource : (muzzle ? muzzle : transform);
        Vector3 origin = src.position;
        Vector3 dir = src.forward;

        // Scene ビューで弾道を確認できるようにする
        Debug.DrawRay(origin, dir * maxDistance, Color.yellow, 2f);

        if (!Physics.Raycast(origin, dir, out RaycastHit hit, maxDistance, targetMask))
        {
            Debug.Log("Miss (何にも当たらず)");
            return;
        }

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
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) return true;
        return false;
#else
        // --- 旧 Input Manager ---
        return Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space);
#endif
    }
}

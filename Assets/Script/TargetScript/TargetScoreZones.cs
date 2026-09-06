using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 人型シルエット標的の得点判定。
/// Raycast の命中点をローカル座標に変換し、頭部／胸部の中心からの距離で
/// 10 / 9 / 8 / 7 点を判定します（同心円をコライダーで分けるより正確・軽量）。
///
/// 使い方:
///   1. SilhouetteTarget のルートにこのコンポーネントを追加
///   2. 同じオブジェクトに MeshCollider（Convex = OFF）を追加
///   3. 射撃側から Evaluate(hit.point) を呼ぶ
/// </summary>
[DisallowMultipleComponent]
public class TargetScoreZones : MonoBehaviour
{
    // ---------------------------------------------------------------
    [Serializable]
    public class Ring
    {
        [Tooltip("ゾーン名（ログや UI 表示用）")]
        public string label = "10";

        [Tooltip("中心からの半径（メートル）。内側から順に小さい値を並べる")]
        public float radius = 0.05f;

        [Tooltip("このリングに当たったときの得点")]
        public int score = 10;
    }

    [Serializable]
    public class Cluster
    {
        [Tooltip("部位名（Head / Chest など）")]
        public string label = "Chest";

        [Tooltip("標的ローカル座標での中心 (X, Y)。原点は標的の底面中央")]
        public Vector2 center = new Vector2(0f, 0.70f);

        [Tooltip("内側（高得点）から外側の順に並べる")]
        public Ring[] rings;
    }

    [Serializable]
    public struct HitResult
    {
        public int score;          // 得点
        public string zone;        // "Head 10" など。外れは "Miss"
        public Vector2 local;      // 標的ローカルの命中位置 (X, Y) [m]
        public bool onFront;       // 前面に当たったか
    }

    // ---------------------------------------------------------------
    [Header("得点ゾーン")]
    public Cluster[] clusters;

    [Header("その他の得点")]
    [Tooltip("シルエット内だがリングの外に当たったときの得点")]
    public int bodyScore = 5;

    [Tooltip("背面・側面に当たったときの得点")]
    public int backScore = 0;

    [Header("判定設定")]
    [Tooltip("標的の厚み [m]。前面 z=0、背面 z=-thickness")]
    public float thickness = 0.10f;

    [Tooltip("前面とみなす奥行きの許容値 [m]")]
    public float frontTolerance = 0.02f;

    [Serializable] public class ScoreEvent : UnityEvent<int> { }

    [Header("イベント")]
    public ScoreEvent onScored;

    // ---------------------------------------------------------------
    /// <summary>コンポーネント追加時に、生成モデルと同じ既定値を入れる。</summary>
    void Reset()
    {
        clusters = new Cluster[]
        {
            new Cluster {
                label = "Head",
                center = new Vector2(0f, 1.335f),
                rings = new Ring[] {
                    new Ring { label = "10", radius = 0.032f, score = 10 },
                    new Ring { label = "9",  radius = 0.053f, score =  9 },
                    new Ring { label = "8",  radius = 0.074f, score =  8 },
                    new Ring { label = "7",  radius = 0.095f, score =  7 },
                }
            },
            new Cluster {
                label = "Chest",
                center = new Vector2(0f, 0.700f),
                rings = new Ring[] {
                    new Ring { label = "10", radius = 0.052f, score = 10 },
                    new Ring { label = "9",  radius = 0.092f, score =  9 },
                    new Ring { label = "8",  radius = 0.132f, score =  8 },
                    new Ring { label = "7",  radius = 0.172f, score =  7 },
                }
            },
        };
        bodyScore = 5;
        backScore = 0;
        thickness = 0.10f;
    }

    // ---------------------------------------------------------------
    /// <summary>ワールド座標の命中点から得点を判定する。</summary>
    public HitResult Evaluate(Vector3 worldPoint)
    {
        Vector3 lp = transform.InverseTransformPoint(worldPoint);
        var r = new HitResult
        {
            local = new Vector2(lp.x, lp.y),
            onFront = lp.z >= -frontTolerance
        };

        if (!r.onFront)
        {
            r.score = backScore;
            r.zone = "Back";
            return r;
        }

        if (clusters != null)
        {
            foreach (var c in clusters)
            {
                if (c == null || c.rings == null || c.rings.Length == 0) continue;
                float d = Vector2.Distance(r.local, c.center);
                foreach (var ring in c.rings)          // 内側から順に判定
                {
                    if (d <= ring.radius)
                    {
                        r.score = ring.score;
                        r.zone = c.label + " " + ring.label;
                        return r;
                    }
                }
            }
        }

        r.score = bodyScore;
        r.zone = "Body";
        return r;
    }

    /// <summary>判定してイベントも発火する版。射撃側からはこちらを呼ぶと楽。</summary>
    public HitResult Hit(Vector3 worldPoint)
    {
        var r = Evaluate(worldPoint);
        onScored?.Invoke(r.score);
        return r;
    }

    // ---------------------------------------------------------------
    // Scene ビューでリングを可視化（選択時のみ）
    void OnDrawGizmosSelected()
    {
        if (clusters == null) return;
        foreach (var c in clusters)
        {
            if (c == null || c.rings == null) continue;
            for (int i = 0; i < c.rings.Length; i++)
            {
                float t = c.rings.Length <= 1 ? 0f : (float)i / (c.rings.Length - 1);
                Gizmos.color = Color.Lerp(Color.red, Color.cyan, t);
                DrawCircle(c.center, c.rings[i].radius);
            }
        }
    }

    void DrawCircle(Vector2 center, float radius, int segs = 48)
    {
        Vector3 prev = transform.TransformPoint(
            new Vector3(center.x + radius, center.y, 0.008f));
        for (int i = 1; i <= segs; i++)
        {
            float a = 2f * Mathf.PI * i / segs;
            Vector3 cur = transform.TransformPoint(new Vector3(
                center.x + radius * Mathf.Cos(a),
                center.y + radius * Mathf.Sin(a),
                0.008f));
            Gizmos.DrawLine(prev, cur);
            prev = cur;
        }
    }
}

using UnityEngine;

/// <summary>
/// GGO 風のレティクル（円の中に十字）。HeartbeatAimSway の照準方向に追従して画面内を揺れます。
///
///   ・円の大きさ  … バレットサークル。心拍が上がるほど広がる（＝当たりにくくなる）
///   ・円の色      … 落ち着いている＝シアン、限界＝赤
///   ・十字        … 現在の照準点
///
/// 取り付け先: Main Camera（HeartbeatAimSway と同じオブジェクト）
/// Canvas も画像アセットも不要です。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class GgoReticle : MonoBehaviour
{
    [Header("参照")]
    public HeartbeatAimSway sway;

    [Header("表示")]
    [Tooltip("構えていないときも表示する")]
    public bool showWhenHip = true;

    [Tooltip("腰だめ時の不透明度")]
    [Range(0f, 1f)] public float hipAlpha = 0.35f;

    [Header("円")]
    [Tooltip("円周の分割数")]
    [Range(16, 96)] public int circleSegments = 56;

    [Tooltip("線の太さ[px]")]
    public float lineWidth = 2f;

    [Tooltip("円の最小半径[px]。これ以下には縮みません")]
    public float minCircleRadius = 14f;

    [Tooltip("円の最大半径[px]")]
    public float maxCircleRadius = 320f;

    [Header("十字")]
    [Tooltip("十字の腕の長さ[px]")]
    public float crossArmLength = 11f;

    [Tooltip("中心の空き[px]")]
    public float crossGap = 5f;

    [Tooltip("中心の点を描く")]
    public bool centerDot = true;

    [Header("色")]
    public Color calmColor = new Color(0.35f, 1f, 0.85f, 1f);
    public Color hotColor = new Color(1f, 0.28f, 0.22f, 1f);

    Camera _cam;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        if (!sway) sway = GetComponent<HeartbeatAimSway>();
        if (!sway) sway = FindAnyObjectByType<HeartbeatAimSway>();
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (sway == null || _cam == null) return;

        float aim = sway.AimBlend01;
        float alpha = Mathf.Lerp(showWhenHip ? hipAlpha : 0f, 1f, aim);
        if (alpha <= 0.001f) return;

        // ---- 照準点を画面座標へ ----
        Vector3 worldPoint = _cam.transform.position + sway.AimDirection * 100f;
        Vector3 sp = _cam.WorldToScreenPoint(worldPoint);
        if (sp.z <= 0f) return;
        Vector2 center = new Vector2(sp.x, Screen.height - sp.y);   // OnGUI は上原点

        // ---- 円の半径[度] → [px] ----
        // 画面の縦 = fieldOfView 度 なので、1度あたりのピクセル数を出す
        float pxPerDeg = Screen.height / Mathf.Max(1f, _cam.fieldOfView);
        float radius = Mathf.Clamp(sway.SpreadDegrees * pxPerDeg, minCircleRadius, maxCircleRadius);

        // ---- 色 ----
        Color c = Color.Lerp(calmColor, hotColor, sway.Excitement01);
        c.a *= alpha;

        Color prev = GUI.color;
        Matrix4x4 prevM = GUI.matrix;

        DrawCircle(center, radius, c);
        DrawCross(center, c);

        GUI.matrix = prevM;
        GUI.color = prev;
    }

    // ---------------------------------------------------------------
    void DrawCircle(Vector2 center, float radius, Color c)
    {
        int n = Mathf.Max(12, circleSegments);
        Vector2 prev = center + new Vector2(radius, 0f);
        for (int i = 1; i <= n; i++)
        {
            float a = 2f * Mathf.PI * i / n;
            Vector2 cur = center + new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
            DrawLine(prev, cur, lineWidth, c);
            prev = cur;
        }
    }

    void DrawCross(Vector2 center, Color c)
    {
        float g = crossGap;
        float L = crossGap + crossArmLength;
        DrawLine(center + new Vector2(g, 0f), center + new Vector2(L, 0f), lineWidth, c);
        DrawLine(center - new Vector2(g, 0f), center - new Vector2(L, 0f), lineWidth, c);
        DrawLine(center + new Vector2(0f, g), center + new Vector2(0f, L), lineWidth, c);
        DrawLine(center - new Vector2(0f, g), center - new Vector2(0f, L), lineWidth, c);

        if (centerDot)
        {
            GUI.color = c;
            float s = Mathf.Max(1f, lineWidth);
            GUI.DrawTexture(new Rect(center.x - s * 0.5f, center.y - s * 0.5f, s, s),
                            Texture2D.whiteTexture);
        }
    }

    /// <summary>GUI 上に太さ付きの線を引く（回転行列を使うので斜めも描けます）。</summary>
    static void DrawLine(Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return;

        Matrix4x4 saved = GUI.matrix;
        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        GUIUtility.RotateAroundPivot(angle, a);
        GUI.color = color;
        GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), Texture2D.whiteTexture);
        GUI.matrix = saved;
    }
}

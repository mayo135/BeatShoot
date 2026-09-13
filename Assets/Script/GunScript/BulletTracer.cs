using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 曳光弾（トレーサー）。銃口から着弾点まで光の筋が飛んでいきます。
/// プレハブもマテリアルも不要で、BulletTracer.Spawn() を呼ぶだけです。
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class BulletTracer : MonoBehaviour
{
    static Material _shared;

    /// <summary>銃口から着弾点へ1発ぶんのトレーサーを飛ばす。</summary>
    public static void Spawn(Vector3 start, Vector3 end,
                             float speed, float length, float width, Color color)
    {
        var go = new GameObject("BulletTracer");
        var t = go.AddComponent<BulletTracer>();
        t.Init(start, end, speed, length, width, color);
    }

    LineRenderer _lr;
    Vector3 _start, _dir;
    float _total, _head, _speed, _length;
    Color _color;

    void Init(Vector3 start, Vector3 end, float speed, float length, float width, Color color)
    {
        _start = start;
        _total = Vector3.Distance(start, end);
        _dir = _total > 0.001f ? (end - start) / _total : Vector3.forward;
        _speed = Mathf.Max(1f, speed);
        _length = Mathf.Max(0.1f, length);
        _color = color;
        _head = 0f;

        _lr = GetComponent<LineRenderer>();
        _lr.material = SharedMaterial();
        _lr.positionCount = 2;
        _lr.useWorldSpace = true;
        _lr.widthMultiplier = width;
        _lr.numCapVertices = 2;
        _lr.shadowCastingMode = ShadowCastingMode.Off;
        _lr.receiveShadows = false;
        _lr.alignment = LineAlignment.View;
        Apply();
    }

    void Update()
    {
        _head += _speed * Time.deltaTime;

        float head = Mathf.Min(_head, _total);
        float tail = Mathf.Min(_head - _length, _total);

        if (tail >= _total) { Destroy(gameObject); return; }

        _lr.SetPosition(0, _start + _dir * Mathf.Max(0f, tail));
        _lr.SetPosition(1, _start + _dir * head);

        // 着弾後は筋が縮んでいくので、あわせて薄くする
        float a = _head > _total
                ? Mathf.InverseLerp(_total + _length, _total, _head)
                : 1f;
        var c = _color; c.a *= a;
        _lr.startColor = new Color(c.r, c.g, c.b, 0f);   // 後端は透明
        _lr.endColor = c;                                 // 先端が明るい
    }

    void Apply()
    {
        _lr.startColor = new Color(_color.r, _color.g, _color.b, 0f);
        _lr.endColor = _color;
        _lr.SetPosition(0, _start);
        _lr.SetPosition(1, _start);
    }

    // ---------------------------------------------------------------
    /// <summary>加算合成の発光マテリアル。URP / ビルトインのどちらでも動くよう順に探す。</summary>
    static Material SharedMaterial()
    {
        if (_shared != null) return _shared;

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Color");

        var m = new Material(sh) { name = "TracerAdditive" };

        // 透明・加算合成にする（URP は数値とキーワードの両方を設定する必要がある）
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 1f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.One);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)CullMode.Off);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.renderQueue = (int)RenderQueue.Transparent;

        _shared = m;
        return _shared;
    }
}

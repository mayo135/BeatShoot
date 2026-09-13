#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 射撃訓練場を組み立てるエディタ拡張。
/// メニュー: Tools > BeatShoot > 射撃訓練場を作る
///
/// 射座(z=0)から +Z 方向に伸びるレンジを、プリミティブだけで作ります。
/// 的は 25 / 50 / 75 / 100m にレーンをずらして配置するので、
/// どれも遮られずに狙えます。
/// </summary>
public static class ShootingRangeBuilder
{
    const string TargetPath = "Assets/Model/TargetModel/SilhouetteTarget.obj";
    const string MatFolder = "Assets/Model/RangeMaterials";
    const string RootName = "ShootingRange";

    // レーンごとの的: (距離[m], 横位置[m])
    static readonly (float dist, float x)[] Targets =
    {
        (25f, -6f), (50f, -2f), (75f, 2f), (100f, 6f),
    };

    static readonly float[] Markers = { 10f, 25f, 50f, 75f, 100f };

    const float RangeLength = 120f;
    const float HalfWidth = 11f;

    [MenuItem("Tools/BeatShoot/射撃訓練場を作る")]
    public static void Build()
    {
        var existing = GameObject.Find(RootName);
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("射撃訓練場",
                "既に射撃訓練場があります。作り直しますか？", "作り直す", "やめる")) return;
            Undo.DestroyObjectImmediate(existing);
        }

        var log = new StringBuilder("射撃訓練場を作成:\n");
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build Shooting Range");

        // 自動セットアップが作った仮の地面と的は片付ける
        var oldGround = GameObject.Find("Ground");
        if (oldGround != null) Undo.DestroyObjectImmediate(oldGround);
        foreach (var z in Object.FindObjectsByType<TargetScoreZones>(FindObjectsSortMode.None))
            Undo.DestroyObjectImmediate(z.gameObject);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build Shooting Range");

        var mFloor  = Mat("M_RangeFloor",  new Color(0.42f, 0.42f, 0.44f), 0.10f);
        var mWall   = Mat("M_RangeWall",   new Color(0.30f, 0.30f, 0.32f), 0.05f);
        var mBerm   = Mat("M_RangeBerm",   new Color(0.44f, 0.37f, 0.27f), 0.02f);
        var mMarker = Mat("M_RangeMarker", new Color(0.95f, 0.78f, 0.15f), 0.20f);
        var mBooth  = Mat("M_RangeBooth",  new Color(0.20f, 0.21f, 0.23f), 0.15f);

        // ---------- 床 ----------
        Box(root, "Floor", new Vector3(0f, -0.1f, RangeLength * 0.5f - 3f),
            new Vector3(HalfWidth * 2f, 0.2f, RangeLength + 12f), mFloor);

        // ---------- 側壁 ----------
        Box(root, "Wall_L", new Vector3(-HalfWidth, 1.75f, RangeLength * 0.5f),
            new Vector3(0.5f, 3.5f, RangeLength + 6f), mWall);
        Box(root, "Wall_R", new Vector3(HalfWidth, 1.75f, RangeLength * 0.5f),
            new Vector3(0.5f, 3.5f, RangeLength + 6f), mWall);

        // ---------- 奥のバックストップ ----------
        // 傾けた土手 + その後ろの壁。跳弾が抜けないように二重にする
        var berm = Box(root, "Berm", new Vector3(0f, 1.8f, RangeLength - 3f),
                       new Vector3(HalfWidth * 2f, 5.5f, 6f), mBerm);
        berm.transform.rotation = Quaternion.Euler(-32f, 0f, 0f);
        Box(root, "BackWall", new Vector3(0f, 4f, RangeLength + 1f),
            new Vector3(HalfWidth * 2f, 8f, 1f), mWall);

        // ---------- 射座 ----------
        Box(root, "FiringBench", new Vector3(0f, 0.55f, 1.2f),
            new Vector3(7f, 1.1f, 0.6f), mBooth);
        Box(root, "BoothBack", new Vector3(0f, 1.75f, -5.5f),
            new Vector3(HalfWidth * 2f, 3.5f, 0.5f), mBooth);
        Box(root, "BoothRoof", new Vector3(0f, 3.6f, -2f),
            new Vector3(HalfWidth * 2f, 0.3f, 8f), mBooth);
        Box(root, "Pillar_L", new Vector3(-5f, 1.8f, 1.6f),
            new Vector3(0.35f, 3.6f, 0.35f), mBooth);
        Box(root, "Pillar_R", new Vector3(5f, 1.8f, 1.6f),
            new Vector3(0.35f, 3.6f, 0.35f), mBooth);

        // ---------- レーン仕切り ----------
        for (int i = -1; i <= 1; i += 2)
            Box(root, $"Lane_{(i < 0 ? "L" : "R")}", new Vector3(i * 4f, 0.15f, 18f),
                new Vector3(0.15f, 0.3f, 34f), mBooth);

        // ---------- 距離マーカー ----------
        var markerRoot = new GameObject("DistanceMarkers");
        markerRoot.transform.SetParent(root.transform, false);
        foreach (float d in Markers)
        {
            // 距離が長いほど太くして、遠くからでも見えるようにする
            float w = Mathf.Lerp(0.25f, 0.6f, d / 100f);
            Box(markerRoot, $"Mark_{d:0}m", new Vector3(0f, 0.02f, d),
                new Vector3(HalfWidth * 2f - 1f, 0.06f, w), mMarker);
        }

        // ---------- 的 ----------
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(TargetPath);
        if (asset == null)
        {
            log.AppendLine("  ! 標的モデルが見つかりません: " + TargetPath);
        }
        else
        {
            var targetRoot = new GameObject("Targets");
            targetRoot.transform.SetParent(root.transform, false);

            foreach (var (dist, x) in Targets)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                go.name = $"Target_{dist:0}m";
                go.transform.SetParent(targetRoot.transform, false);
                go.transform.position = new Vector3(x, 0f, dist);
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);  // 前面を射手へ
                SetUpTarget(go);
                log.AppendLine($"  + {go.name} を ({x}, 0, {dist}) に配置");
            }
        }

        // ---------- プレイヤーを射座へ ----------
        var player = Object.FindAnyObjectByType<SimplePlayerController>();
        if (player != null)
        {
            Undo.RecordObject(player.transform, "Build Shooting Range");
            player.transform.position = new Vector3(0f, 0f, -1.5f);
            player.transform.rotation = Quaternion.identity;      // +Z を向く
            log.AppendLine("  = プレイヤーを射座 (0, 0, -1.5) へ移動");
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;

        log.AppendLine($"\nレンジ全長 {RangeLength}m / 幅 {HalfWidth * 2f}m。床の黄色い線は 10/25/50/75/100m。");
        Debug.Log(log.ToString());
    }

    // ---------------------------------------------------------------
    static GameObject Box(GameObject parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    /// <summary>的に得点判定とコライダーを付ける。</summary>
    static void SetUpTarget(GameObject target)
    {
        if (target.GetComponent<TargetScoreZones>() == null)
            ObjectFactory.AddComponent<TargetScoreZones>(target);

        Transform board = FindChild(target.transform, "Target_Board") ?? BiggestMesh(target.transform);
        if (board != null && board.GetComponent<MeshCollider>() == null)
            ObjectFactory.AddComponent<MeshCollider>(board.gameObject).convex = false;
    }

    static Transform FindChild(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            var r = FindChild(c, name);
            if (r != null) return r;
        }
        return null;
    }

    static Transform BiggestMesh(Transform root)
    {
        Transform best = null;
        int bestCount = -1;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            if (mf.sharedMesh.vertexCount > bestCount)
            {
                bestCount = mf.sharedMesh.vertexCount;
                best = mf.transform;
            }
        }
        return best;
    }

    // ---------------------------------------------------------------
    /// <summary>マテリアルを作って Assets に保存する（保存しないとシーン保存時に消えます）。</summary>
    static Material Mat(string name, Color color, float smoothness)
    {
        string path = $"{MatFolder}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder(MatFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Model"))
                AssetDatabase.CreateFolder("Assets", "Model");
            AssetDatabase.CreateFolder("Assets/Model", "RangeMaterials");
        }

        // Unity のオブジェクトは ?? が正しく効かないので明示的に判定する
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Diffuse");
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);

        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
#endif

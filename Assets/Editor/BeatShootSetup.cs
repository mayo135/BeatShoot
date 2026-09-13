#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// シーンに必要なオブジェクトを一括配置するエディタ拡張。
/// メニュー: Tools > BeatShoot > シーンを自動セットアップ
///
/// 何度実行しても重複しません（足りないものだけ作ります）。
/// 実行後に Cmd+Z で元に戻せます。
/// </summary>
public static class BeatShootSetup
{
    const string GunPath    = "Assets/Model/GunModel/Barrett_M82_lowpoly.obj";
    const string TargetPath = "Assets/Model/TargetModel/SilhouetteTarget.obj";
    const float  TargetDistance = 50f;   // 標的を置く距離[m]

    // ---------------------------------------------------------------
    /// <summary>
    /// スクリプトのコンパイル後に一度だけ自動実行する。
    /// すでにセットアップ済みなら何もしません。手動でやり直したいときはメニューから。
    /// </summary>
    [InitializeOnLoadMethod]
    static void AutoSetup()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SessionState.GetBool(AutoKey, false)) return;

            Camera cam = Camera.main ? Camera.main : Object.FindAnyObjectByType<Camera>();
            if (cam == null) return;                 // シーン未ロード。次のリロードで再挑戦

            SessionState.SetBool(AutoKey, true);

            // 既にセットアップ済みなら触らない
            if (cam.GetComponent<HeartbeatAimSway>() != null &&
                cam.GetComponent<GgoReticle>() != null &&
                Object.FindAnyObjectByType<SimplePlayerController>() != null &&
                Object.FindAnyObjectByType<HeartRateUdpReceiver>() != null &&
                Object.FindAnyObjectByType<HeartRateProvider>() != null) return;

            Setup();
            Debug.Log("BeatShoot: 自動セットアップを実行しました。"
                    + "元に戻すには Cmd+Z、保存するには Cmd+S。"
                    + "やり直しは Tools > BeatShoot > シーンを自動セットアップ。");
        };
    }

    const string AutoKey = "BeatShoot.AutoSetupDone";

    [MenuItem("Tools/BeatShoot/シーンを自動セットアップ")]
    public static void Setup()
    {
        var log = new StringBuilder("BeatShoot セットアップ:\n");
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("BeatShoot Setup");

        // ---------- カメラ ----------
        Camera cam = Camera.main ? Camera.main : Object.FindAnyObjectByType<Camera>();
        if (cam == null)
        {
            EditorUtility.DisplayDialog("BeatShoot",
                "シーンにカメラが見つかりません。Main Camera を作ってから実行してください。", "OK");
            return;
        }

        // ---------- プレイヤー ----------
        var player = Object.FindAnyObjectByType<SimplePlayerController>();
        if (player == null)
        {
            var pgo = new GameObject("Player");
            Undo.RegisterCreatedObjectUndo(pgo, "BeatShoot Setup");

            Vector3 p = cam.transform.position;
            pgo.transform.position = new Vector3(p.x, 0f, p.z);
            pgo.transform.rotation = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);

            var cc = ObjectFactory.AddComponent<CharacterController>(pgo);
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.35f;

            player = ObjectFactory.AddComponent<SimplePlayerController>(pgo);
            log.AppendLine("  + Player (CharacterController + SimplePlayerController) を作成");
        }

        // カメラをプレイヤーの目線位置へ
        if (cam.transform.parent != player.transform)
        {
            Undo.SetTransformParent(cam.transform, player.transform, "BeatShoot Setup");
            log.AppendLine("  = Main Camera を Player の子にしました");
        }
        Undo.RecordObject(cam.transform, "BeatShoot Setup");
        cam.transform.localPosition = new Vector3(0f, player.standEyeHeight, 0f);
        cam.transform.localRotation = Quaternion.identity;

        Undo.RecordObject(player, "BeatShoot Setup");
        player.cam = cam.transform;

        // ---------- 心拍プロバイダ ----------
        var hr = Object.FindAnyObjectByType<HeartRateProvider>();
        if (hr == null)
        {
            var go = new GameObject("HeartRate");
            Undo.RegisterCreatedObjectUndo(go, "BeatShoot Setup");
            hr = ObjectFactory.AddComponent<HeartRateProvider>(go);
            log.AppendLine("  + HeartRate (HeartRateProvider) を作成");
        }
        else log.AppendLine("  = HeartRate は既にあります");

        // スマートウォッチからの受信（ブリッジ未起動なら何もしません）
        if (hr.GetComponent<HeartRateUdpReceiver>() == null)
        {
            var rx = ObjectFactory.AddComponent<HeartRateUdpReceiver>(hr.gameObject);
            rx.provider = hr;
            log.AppendLine("  + HeartRate に HeartRateUdpReceiver を追加");
        }

        // ---------- 照準の揺れ ----------
        var sway = cam.GetComponent<HeartbeatAimSway>();
        if (sway == null)
        {
            sway = ObjectFactory.AddComponent<HeartbeatAimSway>(cam.gameObject);
            log.AppendLine("  + Main Camera に HeartbeatAimSway を追加");
        }
        Undo.RecordObject(sway, "BeatShoot Setup");
        sway.heartRate = hr;
        sway.aimCamera = cam;
        sway.hipFov = cam.fieldOfView;

        // ---------- レティクル ----------
        if (cam.GetComponent<GgoReticle>() == null)
        {
            var ret = ObjectFactory.AddComponent<GgoReticle>(cam.gameObject);
            ret.sway = sway;
            log.AppendLine("  + Main Camera に GgoReticle を追加");
        }

        // ---------- 射撃 ----------
        var shooter = cam.GetComponent<SniperShooter>();
        if (shooter == null)
        {
            shooter = ObjectFactory.AddComponent<SniperShooter>(cam.gameObject);
            log.AppendLine("  + Main Camera に SniperShooter を追加");
        }
        Undo.RecordObject(shooter, "BeatShoot Setup");
        shooter.aimSource = cam.transform;
        shooter.heartRate = hr;

        // ---------- 銃 ----------
        GameObject gun = FindInScene("Barrett");
        if (gun == null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(GunPath);
            if (asset == null) log.AppendLine("  ! 銃モデルが見つかりません: " + GunPath);
            else
            {
                gun = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                Undo.RegisterCreatedObjectUndo(gun, "BeatShoot Setup");
                log.AppendLine("  + 銃モデルを配置");
            }
        }
        if (gun != null)
        {
            Undo.SetTransformParent(gun.transform, cam.transform, "BeatShoot Setup");
            Undo.RecordObject(gun.transform, "BeatShoot Setup");
            gun.transform.localPosition = new Vector3(0.13f, -0.13f, 0.06f);
            gun.transform.localRotation = Quaternion.identity;
            gun.transform.localScale = Vector3.one;

            var muzzle = gun.transform.Find("Muzzle");
            if (muzzle == null)
            {
                var mgo = new GameObject("Muzzle");
                Undo.RegisterCreatedObjectUndo(mgo, "BeatShoot Setup");
                mgo.transform.SetParent(gun.transform, false);
                mgo.transform.localPosition = new Vector3(0f, 0f, 1.05f);
                muzzle = mgo.transform;
                log.AppendLine("  + Muzzle (0, 0, 1.05) を作成");
            }
            shooter.muzzle = muzzle;
        }

        // ---------- 標的 ----------
        var zones = Object.FindAnyObjectByType<TargetScoreZones>();
        GameObject target = zones ? zones.gameObject : FindInScene("SilhouetteTarget");
        if (target == null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(TargetPath);
            if (asset == null) log.AppendLine("  ! 標的モデルが見つかりません: " + TargetPath);
            else
            {
                target = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                Undo.RegisterCreatedObjectUndo(target, "BeatShoot Setup");

                Vector3 fwd = cam.transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
                fwd.Normalize();

                Vector3 pos = cam.transform.position + fwd * TargetDistance;
                pos.y = 0f;
                target.transform.position = pos;
                // 標的の前面(+Z)が射手を向くようにする
                target.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
                log.AppendLine($"  + 標的を {TargetDistance}m 先に配置");
            }
        }
        if (target != null)
        {
            if (target.GetComponent<TargetScoreZones>() == null)
            {
                ObjectFactory.AddComponent<TargetScoreZones>(target);
                log.AppendLine("  + 標的に TargetScoreZones を追加");
            }

            Transform board = FindChild(target.transform, "Target_Board") ?? BiggestMesh(target.transform);
            if (board != null && board.GetComponent<MeshCollider>() == null)
            {
                var mc = ObjectFactory.AddComponent<MeshCollider>(board.gameObject);
                mc.convex = false;
                log.AppendLine("  + " + board.name + " に MeshCollider を追加");
            }
        }

        // ---------- 地面 ----------
        if (GameObject.Find("Ground") == null)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Undo.RegisterCreatedObjectUndo(ground, "BeatShoot Setup");
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(30f, 1f, 30f);
            log.AppendLine("  + Ground を作成");
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);

        log.AppendLine("\n操作: WASD=移動 Space=ジャンプ Ctrl/C=しゃがみ 右クリック=構える 左クリック=発砲 Esc=カーソル解放");
        Debug.Log(log.ToString());
    }

    // ---------------------------------------------------------------
    /// <summary>名前に frag を含むオブジェクトのうち、最も階層が浅いものを返す。</summary>
    static GameObject FindInScene(string frag)
    {
        GameObject best = null;
        int bestDepth = int.MaxValue;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (!t.name.Contains(frag)) continue;
            int d = 0;
            for (var p = t.parent; p != null; p = p.parent) d++;
            if (d < bestDepth) { bestDepth = d; best = t.gameObject; }
        }
        return best;
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
}
#endif

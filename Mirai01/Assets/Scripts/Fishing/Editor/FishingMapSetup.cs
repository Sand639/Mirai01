using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **釣りのオンライン対戦で遊ぶ「マップ」を増やすためのツール。**
///
/// Unityのメニュー「Tools > Mirai01」から使う。
///
/// | メニュー | 何をするか |
/// | --- | --- |
/// | `釣りの新しいマップを作る（オンライン）` | 遊ぶのに必要な部品が入った**マップの雛形**を `Assets/Scenes/Test/FishingMap01.unity` のように作る。**同じ名前があれば番号をずらし、上書きしない** |
/// | `釣りのマップをビルドの一覧に登録し直す` | ロビー・最初の会場・すべてのマップを Build Profiles のシーン一覧に入れ、通信の部品の番号が抜けていれば付ける。**マップの名前を変えたあとや、複製したあとに実行する** |
/// | `開いている釣りマップを点検する` | 今開いているマップに、遊ぶのに必要な物がそろっているかを調べて Console に出す |
///
/// ## `釣りのオンライン用シーンを作る` との違い
///
/// あちらは `FishingOnline.unity` を**毎回まるごと作り直す**ので、手で置いた物が消える。
/// このツールで作ったマップは**一度作ったら二度と上書きしない**ので、Unity 上で自由に作り変えてよい。
///
/// ## ロビーとのつながり
///
/// 名前が `FishingMap` で始まり、ビルドの一覧に入っているシーンは、
/// **ロビーのマップ選びに自動で並ぶ**（`FishingLobbyUI`）。ロビーのシーンを触る必要はない。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class FishingMapSetup
{
    /// <summary>マップのシーン名の頭。**ロビーはこの名前で始まるシーンを候補に並べる。**</summary>
    public const string MapPrefix = "FishingMap";

    public const string LobbyScenePath = FishingSceneBuilder.SceneFolder + "/FishingLobby.unity";
    public const string OnlineScenePath = FishingSceneBuilder.SceneFolder + "/FishingOnline.unity";
    private const string LobbySceneName = "FishingLobby";

    /// <summary>プレイヤーが出てくる円の半径が読めなかったときに使う値（`FishingNetPlayer` の初期値）。</summary>
    private const float DefaultSpawnRadius = 6f;

    // ------------------------------------------------------------
    // 新しいマップを作る
    // ------------------------------------------------------------

    [MenuItem("Tools/Mirai01/釣りの新しいマップを作る（オンライン）")]
    public static void CreateNewMap()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        if (!CheckPrerequisites(out GameObject supplyPrefab, out GameObject bombPrefab))
        {
            return;
        }

        string scenePath = NextFreeMapPath();
        string sceneName = Path.GetFileNameWithoutExtension(scenePath);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        FishingSceneBuilder.CreateLight();

        // ---- ステージ（ここを自由に作り変える）----
        // 床と、四方の壁とゴールは「Stage」の中にまとめる
        GameObject stage = new GameObject("Stage");
        List<GameObject> before = RootObjects(scene);
        FishingSceneBuilder.CreateGround();
        FishingNetSceneBuilder.CreateNetworkPocketWalls();
        MoveNewRootsUnder(scene, before, stage.transform);

        // ---- 動く障害物（見本を1つ。要らなければ消してよい）----
        GameObject obstacles = new GameObject("Obstacles");
        Material obstacleMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingObstacleTestSetup.ObstacleMaterialPath, new Color(0.55f, 0.3f, 0.75f));
        GameObject sample = FishingObstacleTestSetup.CreateObstacle(
            "Obstacle_Sample", new Vector3(0f, 0f, 12f), 0f,
            new Vector3(6f, 1.2f, 1f), obstacleMaterial,
            new[] { new Vector3(-9f, 0f, 0f), new Vector3(9f, 0f, 0f) },
            MovingObstacle.PathMode.PingPong, 3f, 0.5f, false);
        sample.transform.SetParent(obstacles.transform, true);

        // ---- ここから下は遊ぶための仕組み（消さない）----
        GameObject matchObject = new GameObject("FishingMatch");
        matchObject.AddComponent<NetworkObject>();
        matchObject.AddComponent<FishingMatch>();

        GameObject hudObject = new GameObject("FishingHUD");
        hudObject.AddComponent<FishingStatusUI>();
        FishingMatchUI matchUI = hudObject.AddComponent<FishingMatchUI>();
        FishingSceneBuilder.SetString(matchUI, "lobbySceneName", LobbySceneName);

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 16f, -9f);
        cameraObject.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 500f;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<TopDownCameraFollow>();

        // 物資と爆発物は、今あるオンライン用のプレハブを使う（作り直さない）
        FishingSpawnerSetup.CreateSpawner(supplyPrefab, bombPrefab);

        FishingSceneBuilder.CreateUI();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, scenePath);

        // 保存したあとで、通信の部品に番号を付けて保存し直す（EnsureNetworkIds の説明を参照）
        EnsureNetworkIdsInScene(scenePath);

        RegisterMapsInBuildSettings();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"【マップ】新しいマップ「{sceneName}」を作りました。\n" +
            "場所: " + scenePath + "\n" +
            "・「Stage」の中の床・壁と、「Obstacles」の中の障害物は、自由に作り変えてよい\n" +
            "・FishingMatch / FishingHUD / Main Camera / FishingObjectSpawner / HookUI は遊ぶための仕組みなので消さない\n" +
            "・ビルドの一覧に登録したので、ロビーのマップ選びに出てくる\n" +
            "・作り変えたら `Tools > Mirai01 > 開いている釣りマップを点検する` で確かめる");

        CheckOpenMap();
    }

    /// <summary>雛形に使うオンライン用のプレハブと、ロビーがあるかを確かめる。</summary>
    private static bool CheckPrerequisites(out GameObject supplyPrefab, out GameObject bombPrefab)
    {
        supplyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FishingSpawnerSetup.OnlineSupplyPrefabPath);
        bombPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FishingSpawnerSetup.OnlineBombPrefabPath);

        bool lobbyExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(LobbyScenePath) != null;

        if (supplyPrefab != null && bombPrefab != null && lobbyExists)
        {
            return true;
        }

        Debug.LogError(
            "【マップ】マップを作る前に必要な物がありません。\n" +
            (lobbyExists ? "" : "・ロビーのシーン " + LobbyScenePath + "\n") +
            (supplyPrefab != null ? "" : "・" + FishingSpawnerSetup.OnlineSupplyPrefabPath + "\n") +
            (bombPrefab != null ? "" : "・" + FishingSpawnerSetup.OnlineBombPrefabPath + "\n") +
            "先に `Tools > Mirai01 > 釣りのオンライン用シーンを作る（ロビー＋会場）` を実行してください。");
        return false;
    }

    /// <summary>`FishingMap01` から順に、まだ無い名前を探す。**既存のマップは上書きしない。**</summary>
    private static string NextFreeMapPath()
    {
        for (int number = 1; ; number++)
        {
            string path = $"{FishingSceneBuilder.SceneFolder}/{MapPrefix}{number:00}.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null && !File.Exists(path))
            {
                return path;
            }
        }
    }

    private static List<GameObject> RootObjects(Scene scene)
    {
        return new List<GameObject>(scene.GetRootGameObjects());
    }

    /// <summary><paramref name="before"/> のあとに増えた一番上の物を、<paramref name="parent"/> の中へ移す。</summary>
    private static void MoveNewRootsUnder(Scene scene, List<GameObject> before, Transform parent)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (!before.Contains(root) && root.transform != parent)
            {
                root.transform.SetParent(parent, true);
            }
        }
    }

    // ------------------------------------------------------------
    // 通信の部品の番号
    // ------------------------------------------------------------

    /// <summary>
    /// シーンを開き直し、**番号（GlobalObjectIdHash）が 0 のままの通信の部品に番号を付けて**保存する。
    ///
    /// Netcode は、シーンに置いた NetworkObject（試合のまとめ役・ゴール）を**この番号で全員のPCで対応づける。**
    /// 番号は Netcode が自動で付けるが、**付けられるのはシーンが保存されたあと**。
    /// ツールで「部品を置く → 保存」とすると、置いた時点ではまだ保存されていないので **0 のまま残る**
    /// （0 のままだと、参加者の画面でゴールや試合が同期されない）。
    /// そこで保存したあとにもう一度開き直して番号を付けさせ、保存し直している。
    ///
    /// ⚠ **開き直した時点で、番号はメモリ上では付いている**（読み込み時に Netcode が付ける）が、
    /// 「変更あり」の印が付かないので、**そのままでは保存されず、ファイルは 0 のまま**になる。
    /// だから番号の有無にかかわらず、必ず「変更あり」にして保存する（番号は同じ値になるので、何度やっても変わらない）。
    /// </summary>
    private static void EnsureNetworkIdsInScene(string scenePath)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        int remaining = EnsureNetworkIds();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        if (remaining > 0)
        {
            Debug.LogWarning($"【マップ】{scene.name} に、番号が付けられなかった通信の部品が {remaining} 個あります。" +
                             "Unity でシーンを開いて Ctrl+S で保存し直してください。");
        }
    }

    /// <summary>今開いているシーンの NetworkObject すべてに番号を付けさせる。戻り値は、それでも 0 のままの数。</summary>
    private static int EnsureNetworkIds()
    {
        System.Reflection.MethodInfo validate = typeof(NetworkObject).GetMethod("OnValidate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);

        int remaining = 0;

        foreach (NetworkObject networkObject in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // Netcode が番号を付けるときに使う処理を、そのまま呼ぶ
            validate?.Invoke(networkObject, null);
            EditorUtility.SetDirty(networkObject);

            if (ReadNetworkId(networkObject) == 0)
            {
                remaining++;
            }
        }

        return remaining;
    }

    /// <summary>NetworkObject の番号を読む（外から直接読めない項目なので、保存される値として読む）。</summary>
    private static long ReadNetworkId(NetworkObject networkObject)
    {
        SerializedProperty property = new SerializedObject(networkObject).FindProperty("GlobalObjectIdHash");
        return property != null ? property.longValue : -1;
    }

    // ------------------------------------------------------------
    // ビルドの一覧への登録
    // ------------------------------------------------------------

    [MenuItem("Tools/Mirai01/釣りのマップをビルドの一覧に登録し直す")]
    public static void RegisterMapsFromMenu()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        string openScenePath = SceneManager.GetActiveScene().path;

        // 複製したマップなどで、通信の部品の番号が 0 のまま残っていないかも直しておく
        foreach (string path in FindMapScenePaths())
        {
            EnsureNetworkIdsInScene(path);
        }

        int added = RegisterMapsInBuildSettings();
        AssetDatabase.SaveAssets();

        if (!string.IsNullOrEmpty(openScenePath))
        {
            EditorSceneManager.OpenScene(openScenePath, OpenSceneMode.Single);
        }

        Debug.Log($"【マップ】ビルドの一覧を確かめました（新しく入れた数：{added}）。\n" +
                  "入っているマップ：" + string.Join(" / ", FindMapScenePaths()));
    }

    /// <summary>
    /// ロビー・最初の会場・すべてのマップを、ビルドのシーン一覧に入れる。
    /// **Netcode でシーンをまとめて切り替えるには、一覧に入っている必要がある。**
    /// ロビーは起動時に開くよう先頭へ置く。戻り値は新しく入れた（または有効に戻した）数。
    /// </summary>
    public static int RegisterMapsInBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        int changed = 0;

        List<string> wanted = new List<string> { LobbyScenePath, OnlineScenePath };
        wanted.AddRange(FindMapScenePaths());

        foreach (string path in wanted)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            {
                continue;
            }

            EditorBuildSettingsScene existing = scenes.Find(entry => entry.path == path);

            if (existing == null)
            {
                scenes.Add(new EditorBuildSettingsScene(path, true));
                changed++;
            }
            else if (!existing.enabled)
            {
                existing.enabled = true;
                changed++;
            }
        }

        if (changed > 0)
        {
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        return changed;
    }

    /// <summary>`Assets/Scenes/Test/` にある、名前が `FishingMap` で始まるシーンをすべて探す。</summary>
    public static List<string> FindMapScenePaths()
    {
        List<string> paths = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { FishingSceneBuilder.SceneFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path).StartsWith(MapPrefix))
            {
                paths.Add(path);
            }
        }

        paths.Sort();
        return paths;
    }

    // ------------------------------------------------------------
    // 点検
    // ------------------------------------------------------------

    [MenuItem("Tools/Mirai01/開いている釣りマップを点検する")]
    public static void CheckOpenMap()
    {
        Scene scene = SceneManager.GetActiveScene();
        List<string> problems = new List<string>();
        List<string> notes = new List<string>();

        CheckSceneName(scene, problems);
        CheckSystems(problems);
        CheckPockets(problems);
        CheckSpawner(problems, notes);
        CheckSpawnRing(problems, notes);

        string header = $"【マップの点検】{scene.name}\n";

        if (problems.Count == 0)
        {
            Debug.Log(header + "✔ 遊ぶのに必要な物はそろっています。\n" + string.Join("\n", notes));
        }
        else
        {
            Debug.LogWarning(header + $"✖ 直したほうがよい所が {problems.Count} 件あります。\n・" +
                             string.Join("\n・", problems) +
                             (notes.Count > 0 ? "\n\n" + string.Join("\n", notes) : ""));
        }

        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog("釣りマップの点検",
                problems.Count == 0
                    ? "遊ぶのに必要な物はそろっています。"
                    : $"直したほうがよい所が {problems.Count} 件あります。\n\n・" + string.Join("\n・", problems),
                "OK");
        }
    }

    private static void CheckSceneName(Scene scene, List<string> problems)
    {
        if (string.IsNullOrEmpty(scene.path))
        {
            problems.Add("シーンがまだ保存されていません。");
            return;
        }

        string name = Path.GetFileNameWithoutExtension(scene.path);
        if (!name.StartsWith(MapPrefix) && scene.path != OnlineScenePath)
        {
            problems.Add($"シーン名が「{MapPrefix}」で始まっていないので、ロビーのマップ選びに出ません（今：{name}）。");
        }

        bool inBuildList = System.Array.Exists(EditorBuildSettings.scenes,
            entry => entry.enabled && entry.path == scene.path);
        if (!inBuildList)
        {
            problems.Add("ビルドの一覧に入っていません。`釣りのマップをビルドの一覧に登録し直す` を実行してください。");
        }
    }

    private static void CheckSystems(List<string> problems)
    {
        FishingMatch[] matches = Object.FindObjectsByType<FishingMatch>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (matches.Length != 1)
        {
            problems.Add($"FishingMatch（試合のまとめ役）はちょうど1つ必要です（今：{matches.Length}）。");
        }
        else if (matches[0].GetComponent<NetworkObject>() == null)
        {
            problems.Add("FishingMatch に NetworkObject が付いていません。");
        }

        RequireOne<FishingMatchUI>("FishingMatchUI（残り時間と結果の表示）", problems);
        RequireOne<FishingStatusUI>("FishingStatusUI（点数の表示）", problems);
        RequireOne<HookChargeUI>("HookChargeUI（チャージのゲージ）", problems);
        RequireOne<TopDownCameraFollow>("TopDownCameraFollow の付いたカメラ", problems);

        // プレイヤーはロビーから連れてくるので、マップの中に置いてはいけない物
        if (Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
        {
            problems.Add("NetworkManager が置かれています。**マップには置かない**（ロビーから連れてくる）。");
        }

        int zeroIds = 0;
        foreach (NetworkObject networkObject in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (ReadNetworkId(networkObject) == 0)
            {
                zeroIds++;
            }
        }
        if (zeroIds > 0)
        {
            problems.Add($"通信の部品のうち {zeroIds} 個に番号（GlobalObjectIdHash）が付いていません。" +
                         "シーンを保存（Ctrl+S）するか、`釣りのマップをビルドの一覧に登録し直す` を実行してください。");
        }

        if (Object.FindObjectsByType<FishingPlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
        {
            problems.Add("プレイヤーが置かれています。**マップには置かない**（ロビーで生まれた本体がそのまま来る）。");
        }
    }

    private static void RequireOne<T>(string label, List<string> problems) where T : Object
    {
        int count = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        if (count == 0)
        {
            problems.Add($"{label} がありません。");
        }
    }

    /// <summary>ゴールが4つあり、Pocket Index が 0〜3 で重なっていないか。</summary>
    private static void CheckPockets(List<string> problems)
    {
        FishingNetPocket[] pockets = Object.FindObjectsByType<FishingNetPocket>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (pockets.Length != FishingTeams.PocketCount)
        {
            problems.Add($"ゴール（FishingNetPocket）はちょうど {FishingTeams.PocketCount} つ必要です（今：{pockets.Length}）。");
        }

        HashSet<int> used = new HashSet<int>();

        foreach (FishingNetPocket pocket in pockets)
        {
            int index = pocket.PocketIndex;

            if (index < 0 || index >= FishingTeams.PocketCount)
            {
                problems.Add($"ゴール「{pocket.name}」の Pocket Index が 0〜{FishingTeams.PocketCount - 1} の外です（今：{index}）。");
            }
            else if (!used.Add(index))
            {
                problems.Add($"Pocket Index {index} のゴールが2つ以上あります（「{pocket.name}」）。0〜3 を1つずつ使ってください。");
            }

            if (pocket.GetComponent<NetworkObject>() == null)
            {
                problems.Add($"ゴール「{pocket.name}」に NetworkObject が付いていません。");
            }

            Collider trigger = pocket.GetComponent<Collider>();
            if (trigger == null || !trigger.isTrigger)
            {
                problems.Add($"ゴール「{pocket.name}」に、Is Trigger が ON の当たり判定が付いていません。");
            }
        }
    }

    private static void CheckSpawner(List<string> problems, List<string> notes)
    {
        FishingObjectSpawner[] spawners = Object.FindObjectsByType<FishingObjectSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (spawners.Length != 1)
        {
            problems.Add($"FishingObjectSpawner（物資を降らせる係）はちょうど1つ必要です（今：{spawners.Length}）。");
            return;
        }

        SerializedObject serialized = new SerializedObject(spawners[0]);
        CheckSpawnPrefab(serialized, "supplyPrefab", "Supply Prefab", problems);
        CheckSpawnPrefab(serialized, "bombPrefab", "Bomb Prefab", problems);

        Vector2 half = serialized.FindProperty("areaHalfSize").vector2Value;
        Vector3 center = spawners[0].transform.position;
        notes.Add($"物資が降る範囲：中心 ({center.x:0.#}, {center.z:0.#}) から X ±{half.x:0.#}m ／ Z ±{half.y:0.#}m" +
                  "（スポナーを選ぶとシーン画面に枠が出る。**ステージの広さを変えたら Area Half Size も合わせる**）");
    }

    private static void CheckSpawnPrefab(SerializedObject spawner, string field, string label, List<string> problems)
    {
        GameObject prefab = spawner.FindProperty(field).objectReferenceValue as GameObject;

        if (prefab == null)
        {
            problems.Add($"スポナーの {label} が空です。");
        }
        else if (prefab.GetComponent<NetworkObject>() == null)
        {
            problems.Add($"スポナーの {label} が1人用のプレハブです（{prefab.name}）。オンライン用（FishingOnline〜）を入れてください。");
        }
    }

    /// <summary>
    /// プレイヤーが出てくる場所（中心から半径 6m の円の上に8か所）に、床があってふさがっていないか。
    /// プレイヤーの出てくる場所は `FishingNetPlayer` が決めていて、**マップ側では変えられない。**
    /// </summary>
    private static void CheckSpawnRing(List<string> problems, List<string> notes)
    {
        float radius = ReadSpawnRadius();
        Physics.SyncTransforms();

        for (int i = 0; i < FishingTeams.MaxPlayers; i++)
        {
            float angle = i * (360f / FishingTeams.MaxPlayers);
            Vector3 point = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * radius);

            if (!Physics.Raycast(point + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 60f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                problems.Add($"プレイヤー{i + 1}が出てくる場所 ({point.x:0.#}, {point.z:0.#}) に床がありません。");
                continue;
            }

            if (hit.point.y > 0.3f)
            {
                string what = hit.collider.GetComponentInParent<MovingObstacle>() != null ? "動く障害物" : "物";
                problems.Add($"プレイヤー{i + 1}が出てくる場所 ({point.x:0.#}, {point.z:0.#}) が{what}「{hit.collider.name}」でふさがっています。");
            }
            else if (hit.point.y < -0.3f)
            {
                problems.Add($"プレイヤー{i + 1}が出てくる場所 ({point.x:0.#}, {point.z:0.#}) の床が低すぎます（高さ {hit.point.y:0.#}m）。床は高さ0に置いてください。");
            }
        }

        notes.Add($"プレイヤーが出てくる場所：中心 (0, 0) から半径 {radius:0.#}m の円の上（床は高さ0。ここはふさがない）");
    }

    private static float ReadSpawnRadius()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FishingNetSceneBuilder.OnlinePlayerPrefabPath);
        FishingNetPlayer player = prefab != null ? prefab.GetComponent<FishingNetPlayer>() : null;

        if (player == null)
        {
            return DefaultSpawnRadius;
        }

        SerializedProperty property = new SerializedObject(player).FindProperty("spawnRadius");
        return property != null ? property.floatValue : DefaultSpawnRadius;
    }
}

using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
// Key（押すキーの種類）は UnityEngine.InputSystem にある

/// <summary>
/// **宇宙ごみ集めの「ロビー」と「マップ」を作るツール。**
///
/// Unityのメニュー「Tools &gt; Mirai01 &gt; 宇宙ごみ集めのシーンを作る（ロビー＋マップ）」から実行する。
///
/// ## できるもの
///
///   ・`Assets/Scenes/Test/SpaceJunkLobby.unity` … つないで待ち合わせる部屋。
///     **設定端末**が置いてあり、ホストだけが近づいて `E` で詳細設定を開ける
///   ・`Assets/Scenes/Prototype/SpaceJunk/SpaceJunkMap01〜03.unity` …
///     **釣りのマップをコピーして、中の部品を宇宙ごみ用に差し替えたもの**
///   ・`Assets/Prefabs/SpaceJunk/Online/` … 試合の係・プレイヤー・素材3種類
///
/// ## 釣りを壊さないために
///
///   ・**釣りのシーンとプレハブには一切書き込まない。** マップはコピーしてから差し替える
///   ・**すでにある SpaceJunkMap は作り直さない。** 手で直した中身が消えないようにするため
///     （作り直したいときは、そのシーンを消してからもう一度実行する）
///
/// ## マップの差し替えでやっていること
///
/// | 釣りの部品 | 宇宙ごみの部品 |
/// | --- | --- |
/// | `FishingMatch`（試合のまとめ役） | `SpaceJunkRound`（1ラウンドの進行役） |
/// | `FishingMatchUI` / `FishingStatusUI` | `SpaceJunkMatchUI` |
/// | `FishingNetPocket`（ゴール） | `SpaceJunkGoal`（自陣ゴール） |
/// | `FishingObjectSpawner` | `SpaceJunkSpawner`（素材3種類） |
/// | 最初から置いてある物資・爆発物 | **取り除く**（スポナーが出すため） |
///
/// 地形・壁・障害物・カメラ・ゲージはそのまま残る。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class SpaceJunkSetup
{
    public const string LobbySceneName = "SpaceJunkLobby";
    public const string LobbyScenePath = "Assets/Scenes/Test/" + LobbySceneName + ".unity";

    public const string MapSceneFolder = "Assets/Scenes/Prototype/SpaceJunk";

    /// <summary>コピー元の釣りマップと、コピー先の宇宙ごみマップ。</summary>
    private static readonly string[,] MapSources =
    {
        { "Assets/Scenes/Test/FishingMap01.unity", MapSceneFolder + "/SpaceJunkMap01.unity" },
        { "Assets/Scenes/Test/FishingMap02.unity", MapSceneFolder + "/SpaceJunkMap02.unity" },
        { "Assets/Scenes/Prototype/Fish/FishingMap03.unity", MapSceneFolder + "/SpaceJunkMap03.unity" },
        { "Assets/Scenes/Test/FishingOnline.unity", MapSceneFolder + "/SpaceJunkMap04.unity" }
    };

    [MenuItem("Tools/Mirai01/宇宙ごみ集めのシーンを作る（ロビー＋マップ）")]
    public static void CreateAll()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        FishingSceneBuilder.EnsureFolders();
        FishingSceneBuilder.EnsureFolder(MapSceneFolder);
        SpaceJunkPrefabBuilder.EnsureFolders();

        InputActionAsset inputActions = FishingSceneBuilder.LoadInputActions();

        // プレハブ作りは作業用オブジェクトを一時的に置くので、空のシーンで行う
        // （いま開いているシーンを汚さないため）
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject sessionPrefab = SpaceJunkPrefabBuilder.CreateSessionPrefab(LobbySceneName);
        GameObject playerPrefab = SpaceJunkPrefabBuilder.CreatePlayerPrefab(inputActions);
        GameObject[] materialPrefabs = SpaceJunkPrefabBuilder.CreateMaterialPrefabs();

        CreateLobbyScene(playerPrefab, sessionPrefab);

        List<string> madeMaps = new List<string>();
        List<string> skippedMaps = new List<string>();
        ConvertMaps(materialPrefabs, madeMaps, skippedMaps);

        RegisterScenesInBuildSettings(madeMaps);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string mapList = madeMaps.Count > 0 ? string.Join("\n　　　　　", madeMaps) : "（1つも作れませんでした）";
        string skipped = skippedMaps.Count > 0
            ? "\n\n※ すでにあったので作り直さなかったマップ：\n　　　　　" + string.Join("\n　　　　　", skippedMaps) +
              "\n　作り直したいときは、そのシーンを消してからもう一度実行してください。"
            : string.Empty;

        Debug.Log(
            "【宇宙ごみ】ロビーとマップを作りました。\n" +
            "ロビー　: " + LobbyScenePath + "\n" +
            "マップ　: " + mapList + "\n" +
            "プレハブ: " + SpaceJunkPrefabBuilder.OnlinePrefabFolder + "\n" +
            "\n遊び方：\n" +
            "1. " + LobbySceneName + ".unity を開いて再生\n" +
            "2. 「インターネットで遊ぶ」→「部屋を作る」（合言葉が出るまで10〜20秒）\n" +
            "3. 相手に合言葉を伝えて入ってもらう（最大8人）\n" +
            "4. **ホストが設定端末に近づいて E キー** → チーム分け・何本先取・最大時間・使うマップを決める\n" +
            "5. 「ゲーム開始」を押す → 全員が1ラウンド目のマップへ\n" +
            "\n※ Build Settings にロビーとマップを自動で登録しました。" +
            "これが無いと、全員のシーンをまとめて切り替えられません。" +
            skipped);
    }

    // ------------------------------------------------------------
    // ロビーのシーン
    // ------------------------------------------------------------

    /// <summary>
    /// ロビーのシーンを作る。**毎回作り直す**（釣りのロビーと同じ扱い）。
    /// 歩き回れる地面と、ホスト専用の設定端末を置く。
    /// </summary>
    private static void CreateLobbyScene(GameObject playerPrefab, GameObject sessionPrefab)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        FishingSceneBuilder.CreateLight();

        Material groundMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/TestGround.mat", new Color(0.72f, 0.72f, 0.72f));
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(2f, 1f, 2f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

        // ロビーのカメラ。**追従は付けない**（ロビーでは全体が見えていればよい）
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 14f, -14f);
        cameraObject.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.1f;
        cameraObject.AddComponent<AudioListener>();

        CreateSettingsTerminal();

        // ---- 通信のまとめ役 ----
        GameObject managerObject = new GameObject("NetworkManager");
        NetworkManager manager = managerObject.AddComponent<NetworkManager>();
        UnityTransport transport = managerObject.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 7777);

        // ⚠ **AddComponent した直後の NetworkManager は NetworkConfig が null。**
        //   インスペクターから足したときは Reset() が呼ばれて作られるが、
        //   スクリプトの AddComponent では呼ばれないため、ここで自分で用意する
        if (manager.NetworkConfig == null)
        {
            manager.NetworkConfig = new NetworkConfig();
        }

        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.PlayerPrefab = playerPrefab;
        manager.NetworkConfig.TickRate = FishingNetSceneBuilder.NetworkTickRate;

        // **これがONでないと、全員のシーンをまとめて切り替えられない。**
        manager.NetworkConfig.EnableSceneManagement = true;

        // 既存の接続画面をそのまま使う（LAN／インターネット／1人で練習）
        managerObject.AddComponent<LanConnectionUi>();
        InternetConnection internet = managerObject.AddComponent<InternetConnection>();
        managerObject.AddComponent<LanAutoStart>();
        managerObject.AddComponent<LanConnectionLogger>();
        managerObject.AddComponent<NetworkStatusHud>();

        // **8人まで入れるようにする**（初期値は4のため）
        FishingSceneBuilder.SetInt(internet, "maxPlayers", SpaceJunkTeams.MaxPlayers);

        // ロビーの画面（参加者一覧と、端末を開いたときの詳細設定）
        managerObject.AddComponent<SpaceJunkLobbyUI>();

        // 試合の係を、ホストが1つだけ出す役
        SpaceJunkSessionSpawner spawner = managerObject.AddComponent<SpaceJunkSessionSpawner>();
        FishingSceneBuilder.SetRef(spawner, "sessionPrefab", sessionPrefab);

        EditorUtility.SetDirty(manager);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, LobbyScenePath);
    }

    /// <summary>ホストだけが使える設定端末を置く。</summary>
    private static void CreateSettingsTerminal()
    {
        Material terminalMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/SpaceJunkTerminal.mat", new Color(0.3f, 0.35f, 0.45f));

        GameObject terminal = GameObject.CreatePrimitive(PrimitiveType.Cube);
        terminal.name = "SettingsTerminal";
        terminal.transform.position = new Vector3(0f, 0.75f, 5f);
        terminal.transform.localScale = new Vector3(1.2f, 1.5f, 0.6f);

        MeshRenderer renderer = terminal.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = terminalMaterial;

        SpaceJunkLobbyTerminal script = terminal.AddComponent<SpaceJunkLobbyTerminal>();
        FishingSceneBuilder.SetRef(script, "highlightRenderer", renderer);

        // **押すキーは明示的に入れておく。**
        // 以前 KeyCode（古い入力）で作っていたころのシーンが残っていると、
        // 数字だけがそのまま読まれて別のキーになってしまうため
        FishingSceneBuilder.SetInt(script, "interactKey", (int)Key.E);
    }

    // ------------------------------------------------------------
    // マップ（釣りのマップをコピーして差し替える）
    // ------------------------------------------------------------

    private static void ConvertMaps(GameObject[] materialPrefabs, List<string> made, List<string> skipped)
    {
        for (int i = 0; i < MapSources.GetLength(0); i++)
        {
            string source = MapSources[i, 0];
            string destination = MapSources[i, 1];

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(source) == null)
            {
                Debug.LogWarning($"[JUNK] コピー元のシーンが見つかりません：{source}（とばします）");
                continue;
            }

            // **すでにあるマップは作り直さない。** 手で直した中身が消えないようにするため
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(destination) != null)
            {
                skipped.Add(destination);
                made.Add(destination);
                continue;
            }

            if (!AssetDatabase.CopyAsset(source, destination))
            {
                Debug.LogError($"[JUNK] シーンをコピーできませんでした：{source} → {destination}");
                continue;
            }

            ConvertOneMap(destination, materialPrefabs);
            made.Add(destination);
        }
    }

    /// <summary>コピーしたマップを開いて、釣りの部品を宇宙ごみの部品に差し替える。</summary>
    private static void ConvertOneMap(string scenePath, GameObject[] materialPrefabs)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        ReplaceMatch();
        ReplaceHud();
        ReplaceGoals();
        ReplaceSpawner(materialPrefabs);
        RemovePlacedItems();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, scenePath);
    }

    /// <summary>試合のまとめ役 → 1ラウンドの進行役。**NetworkObject はそのまま使い回す。**</summary>
    private static void ReplaceMatch()
    {
        FishingMatch[] matches = Object.FindObjectsByType<FishingMatch>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (FishingMatch match in matches)
        {
            GameObject host = match.gameObject;
            UnpackIfPrefabInstance(host);
            host.name = "SpaceJunkRound";

            Object.DestroyImmediate(match);

            if (host.GetComponent<NetworkObject>() == null)
            {
                host.AddComponent<NetworkObject>();
            }

            if (host.GetComponent<SpaceJunkRound>() == null)
            {
                host.AddComponent<SpaceJunkRound>();
            }
        }
    }

    /// <summary>釣りの画面表示 → 宇宙ごみの画面表示。</summary>
    private static void ReplaceHud()
    {
        FishingMatchUI[] matchUis = Object.FindObjectsByType<FishingMatchUI>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (FishingMatchUI matchUi in matchUis)
        {
            GameObject host = matchUi.gameObject;
            UnpackIfPrefabInstance(host);
            host.name = "SpaceJunkHUD";

            Object.DestroyImmediate(matchUi);

            // 釣りの点数表示（爆発カウントダウンとスタン）は使わないので外す
            FishingStatusUI status = host.GetComponent<FishingStatusUI>();
            if (status != null)
            {
                Object.DestroyImmediate(status);
            }

            if (host.GetComponent<SpaceJunkMatchUI>() == null)
            {
                host.AddComponent<SpaceJunkMatchUI>();
            }
        }
    }

    /// <summary>釣りのゴール → 自陣ゴール。**並び順の番号と床の見た目は引き継ぐ。**</summary>
    private static void ReplaceGoals()
    {
        FishingNetPocket[] pockets = Object.FindObjectsByType<FishingNetPocket>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (FishingNetPocket pocket in pockets)
        {
            GameObject host = pocket.gameObject;
            UnpackIfPrefabInstance(host);

            int index = GetInt(pocket, "pocketIndex");
            Object padRenderer = GetRef(pocket, "padRenderer");

            host.name = host.name.Replace("Pocket_", "Goal_");

            Object.DestroyImmediate(pocket);

            SpaceJunkGoal goal = host.GetComponent<SpaceJunkGoal>();
            if (goal == null)
            {
                goal = host.AddComponent<SpaceJunkGoal>();
            }

            FishingSceneBuilder.SetInt(goal, "goalIndex", index);
            FishingSceneBuilder.SetRef(goal, "padRenderer", padRenderer);
        }
    }

    /// <summary>釣りのスポナー → 素材3種類のスポナー。**出す範囲と高さは引き継ぐ。**</summary>
    private static void ReplaceSpawner(GameObject[] materialPrefabs)
    {
        FishingObjectSpawner[] spawners = Object.FindObjectsByType<FishingObjectSpawner>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (FishingObjectSpawner spawner in spawners)
        {
            GameObject host = spawner.gameObject;
            UnpackIfPrefabInstance(host);
            host.name = "SpaceJunkSpawner";

            Vector2 area = GetVector2(spawner, "areaHalfSize");
            float dropHeight = GetFloat(spawner, "dropHeight");

            Object.DestroyImmediate(spawner);

            SpaceJunkSpawner replacement = host.GetComponent<SpaceJunkSpawner>();
            if (replacement == null)
            {
                replacement = host.AddComponent<SpaceJunkSpawner>();
            }

            SetVector2(replacement, "areaHalfSize", area);
            FishingSceneBuilder.SetFloat(replacement, "dropHeight", dropHeight);

            if (materialPrefabs.Length >= 3)
            {
                FishingSceneBuilder.SetRef(replacement, "platePrefab", materialPrefabs[0]);
                FishingSceneBuilder.SetRef(replacement, "circuitPrefab", materialPrefabs[1]);
                FishingSceneBuilder.SetRef(replacement, "fuelPrefab", materialPrefabs[2]);
            }
        }
    }

    /// <summary>
    /// **最初から置いてある物資・爆発物を取り除く。**
    /// 宇宙ごみではスポナーが素材を出すので、釣りの物が残っていると混ざってしまう。
    /// </summary>
    private static void RemovePlacedItems()
    {
        HookableObject[] items = Object.FindObjectsByType<HookableObject>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (HookableObject item in items)
        {
            if (item != null)
            {
                Object.DestroyImmediate(item.gameObject);
            }
        }
    }

    // ------------------------------------------------------------
    // Build Settings への登録
    // ------------------------------------------------------------

    /// <summary>
    /// ロビーとマップを Build Settings に登録する。
    ///
    /// **Netcode でシーンをまとめて切り替えるには、そのシーンがビルドの一覧に
    /// 入っている必要がある。** 入っていないと「読み込めません」で止まる。
    /// すでに入っていれば何もしない（何度実行しても増えない）。
    /// </summary>
    private static void RegisterScenesInBuildSettings(List<string> mapPaths)
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(
            EditorBuildSettings.scenes);

        bool changed = AddSceneIfMissing(scenes, LobbyScenePath);

        foreach (string path in mapPaths)
        {
            changed |= AddSceneIfMissing(scenes, path);
        }

        if (changed)
        {
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[JUNK] Build Settings に、ロビーと宇宙ごみのマップを登録しました。");
        }
    }

    private static bool AddSceneIfMissing(List<EditorBuildSettingsScene> scenes, string path)
    {
        foreach (EditorBuildSettingsScene entry in scenes)
        {
            if (entry.path == path)
            {
                // 入っているが無効になっている場合は有効に戻す
                if (!entry.enabled)
                {
                    entry.enabled = true;
                    return true;
                }
                return false;
            }
        }

        scenes.Add(new EditorBuildSettingsScene(path, true));
        return true;
    }

    // ------------------------------------------------------------
    // プレハブからの切り離し
    // ------------------------------------------------------------

    /// <summary>
    /// **差し替える相手がプレハブのインスタンスなら、先に切り離す。**
    ///
    /// 釣りのマップは、試合のまとめ役・ゴール・スポナーなどを**プレハブとして**置いている。
    /// そのまま部品を消したり足したりすると、**「プレハブに対する上書き」として記録される**ため、
    /// できあがった宇宙ごみのマップが**釣りのプレハブに繋がったまま**になる。
    ///
    /// その状態だと、**釣りのプレハブを直したときに宇宙ごみのマップも一緒に変わる**
    /// （消したはずの部品が復活する、など）。
    /// 宇宙ごみのマップは釣りとは別物にしたいので、ここで縁を切っておく。
    ///
    /// プレハブでなければ何もしない。
    /// （2026/9/20 に追加。それ以前に作られた `SpaceJunkMap` は繋がったままなので、
    ///  作り直すときに切り離される。`リスクリスト.md` に登録済み）
    /// </summary>
    private static void UnpackIfPrefabInstance(GameObject target)
    {
        if (target == null || !PrefabUtility.IsPartOfPrefabInstance(target))
        {
            return;
        }

        GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(target);

        if (instanceRoot == null)
        {
            return;
        }

        PrefabUtility.UnpackPrefabInstance(
            instanceRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        Debug.Log($"[JUNK] 「{instanceRoot.name}」を釣りのプレハブから切り離しました。");
    }

    // ------------------------------------------------------------
    // 値を読む道具（FishingSceneBuilder には書き込む側しか無いため）
    // ------------------------------------------------------------

    private static int GetInt(Object owner, string field)
    {
        SerializedProperty property = new SerializedObject(owner).FindProperty(field);
        return property != null ? property.intValue : 0;
    }

    private static float GetFloat(Object owner, string field)
    {
        SerializedProperty property = new SerializedObject(owner).FindProperty(field);
        return property != null ? property.floatValue : 0f;
    }

    private static Vector2 GetVector2(Object owner, string field)
    {
        SerializedProperty property = new SerializedObject(owner).FindProperty(field);
        return property != null ? property.vector2Value : Vector2.zero;
    }

    private static Object GetRef(Object owner, string field)
    {
        SerializedProperty property = new SerializedObject(owner).FindProperty(field);
        return property != null ? property.objectReferenceValue : null;
    }

    private static void SetVector2(Object owner, string field, Vector2 value)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(field);

        if (property == null)
        {
            return;
        }

        property.vector2Value = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}

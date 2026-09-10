using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **オンライン対戦の「ロビー」と「釣り会場」のシーンを作るツール。**
///
/// Unityのメニュー「Tools > Mirai01 > 釣りのオンライン用シーンを作る（ロビー＋会場）」から実行する。
///
/// できるもの：
///   ・`Assets/Scenes/Test/FishingLobby.unity` … つないで待ち合わせる部屋。
///     既存の接続画面（LAN／インターネット／1人）に、**ロビー表示とゲーム開始ボタン**を足したもの
///   ・`Assets/Scenes/Test/FishingOnline.unity` … みんなで釣る会場
///   ・`Assets/Prefabs/FishingOnlinePlayer.prefab` … オンラインで1人分になる本体
///
/// **1人用のシーン（FishingHookTest / FishingArenaTest）は触らない。**
///
/// ⚠ **シーンを全員でまとめて切り替えるには、両方のシーンが Build Settings に
/// 入っている必要がある**（Netcode の決まり）。このツールが自動で登録する。
/// 検証用シーンをビルドに入れない決まりとぶつかるので、`質問リスト.md` に登録済み。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class FishingOnlineSetup
{
    private const string LobbyScenePath = FishingSceneBuilder.SceneFolder + "/FishingLobby.unity";
    private const string OnlineScenePath = FishingSceneBuilder.SceneFolder + "/FishingOnline.unity";
    private const string OnlineSceneName = "FishingOnline";

    [MenuItem("Tools/Mirai01/釣りのオンライン用シーンを作る（ロビー＋会場）")]
    public static void CreateAll()
    {
        FishingSceneBuilder.EnsureFolders();
        InputActionAsset inputActions = FishingSceneBuilder.LoadInputActions();

        // 会場のシーンを先に作り、その中でプレハブも用意する
        // （プレハブ作りは作業用オブジェクトを一時的に置くため、
        //  いま開いているシーンを汚さないよう、新しいシーンにしてから行う）
        GameObject playerPrefab = CreateOnlineScene(inputActions);

        CreateLobbyScene(playerPrefab);

        RegisterScenesInBuildSettings();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "【オンライン】ロビーと釣り会場のシーンを作りました。\n" +
            "ロビー: " + LobbyScenePath + "\n" +
            "会場　: " + OnlineScenePath + "\n" +
            "プレハブ: " + FishingNetSceneBuilder.OnlinePlayerPrefabPath + "\n" +
            "\n遊び方：\n" +
            "1. FishingLobby.unity を開いて再生\n" +
            "2. 「インターネットで遊ぶ」→「部屋を作る」（合言葉が出るまで10〜20秒）\n" +
            "3. 相手に合言葉を伝えて入ってもらう（最大8人）\n" +
            "4. 人数がそろったら、ホストが「ゲーム開始」を押す → 全員が会場へ移動\n" +
            "\n※ Build Settings に両方のシーンを自動で登録しました。" +
            "これが無いと、全員のシーンをまとめて切り替えられません。");
    }

    // ------------------------------------------------------------
    // ロビーのシーン
    // ------------------------------------------------------------

    private static void CreateLobbyScene(GameObject playerPrefab)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

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
        cameraObject.transform.position = new Vector3(0f, 12f, -12f);
        cameraObject.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.1f;
        cameraObject.AddComponent<AudioListener>();

        // ---- 通信のまとめ役 ----
        GameObject managerObject = new GameObject("NetworkManager");
        NetworkManager manager = managerObject.AddComponent<NetworkManager>();
        UnityTransport transport = managerObject.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 7777);

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
        FishingSceneBuilder.SetInt(internet, "maxPlayers", FishingTeams.MaxPlayers);

        // ロビー表示とゲーム開始ボタン
        FishingLobbyUI lobbyUI = managerObject.AddComponent<FishingLobbyUI>();
        FishingSceneBuilder.SetString(lobbyUI, "gameSceneName", OnlineSceneName);

        EditorUtility.SetDirty(manager);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, LobbyScenePath);
    }

    // ------------------------------------------------------------
    // 釣り会場のシーン
    // ------------------------------------------------------------

    /// <summary>釣り会場のシーンを作る。あわせてオンライン用プレイヤーのプレハブも用意して返す。</summary>
    private static GameObject CreateOnlineScene(InputActionAsset inputActions)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // プレハブ類は、この新しいシーンの中で組んで保存する（作業用オブジェクトはすぐ消える）
        ExplosionEffect explosionPrefab = FishingSceneBuilder.CreateExplosionPrefab();
        GameObject playerPrefab = FishingNetSceneBuilder.CreateOnlinePlayerPrefab(inputActions);

        FishingSceneBuilder.CreateLight();
        FishingSceneBuilder.CreateGround();

        // ---- 試合のまとめ役（チーム数・点数・ゴールの割り当て）----
        GameObject matchObject = new GameObject("FishingMatch");
        matchObject.AddComponent<NetworkObject>();
        matchObject.AddComponent<FishingMatch>();

        // ---- 画面表示（点数・爆発カウントダウン・スタン）----
        // 点数はオンラインの試合から読むので ScoreBoard は入れない
        GameObject hudObject = new GameObject("FishingHUD");
        hudObject.AddComponent<FishingStatusUI>();

        // ---- 四方の壁とゴール ----
        FishingNetSceneBuilder.CreateNetworkPocketWalls();

        // ---- 見下ろしカメラ（追う相手は、自分のプレイヤーが生まれた時点で決まる）----
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 16f, -9f);
        cameraObject.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 500f;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<TopDownCameraFollow>();

        // ---- 物資と爆発物 ----
        // 8人で取り合うので、1人用より多めに置く
        FishingNetSceneBuilder.CreateNetworkSupplyRing(12, 9f);
        FishingNetSceneBuilder.CreateNetworkBomb("Bomb_1", new Vector3(-5.5f, 0.5f, 5.5f), explosionPrefab);
        FishingNetSceneBuilder.CreateNetworkBomb("Bomb_2", new Vector3(5.5f, 0.5f, -5.5f), explosionPrefab);
        FishingNetSceneBuilder.CreateNetworkBomb("Bomb_3", new Vector3(5.5f, 0.5f, 5.5f), explosionPrefab);
        FishingNetSceneBuilder.CreateNetworkBomb("Bomb_4", new Vector3(-5.5f, 0.5f, -5.5f), explosionPrefab);

        // ---- チャージとスキルチェックのゲージ ----
        // 自分のプレイヤーが生まれた時点で、そのプレイヤーに結びつけられる
        FishingSceneBuilder.CreateUI();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, OnlineScenePath);

        return playerPrefab;
    }

    // ------------------------------------------------------------
    // Build Settings への登録
    // ------------------------------------------------------------

    /// <summary>
    /// ロビーと会場のシーンを Build Settings に登録する。
    ///
    /// **Netcode でシーンをまとめて切り替えるには、そのシーンがビルドの一覧に
    /// 入っている必要がある。** 入っていないと「読み込めません」で止まる。
    /// すでに入っていれば何もしない（何度実行しても増えない）。
    /// </summary>
    private static void RegisterScenesInBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(
            EditorBuildSettings.scenes);

        bool changed = false;
        changed |= AddSceneIfMissing(scenes, LobbyScenePath);
        changed |= AddSceneIfMissing(scenes, OnlineScenePath);

        if (changed)
        {
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[FISH] Build Settings にロビーと会場のシーンを登録しました。");
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
}

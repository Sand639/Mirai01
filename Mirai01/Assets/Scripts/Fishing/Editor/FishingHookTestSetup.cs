using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// **釣りフック（フック・投擲アクション）の検証用シーンを作るツール。**
///
/// Unityのメニュー「Tools > Mirai01 > 釣りフックの検証シーンを作る」から実行できる。
/// 実行すると Assets/Scenes/Test/FishingHookTest.unity ができる。
/// そのシーンを開いて再生ボタンを押すだけで、フックの操作を確かめられる。
///
/// 中身：見下ろしカメラ、床、四方の壁とポケット、マウス方向を向くプレイヤー
///       （既存の PlayerRig を土台に使用）、物資、爆発物、スコア表示、
///       チャージ量とスキルチェックのゲージ。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// ※ 何度実行しても作り直せる（シーンを上書きする）。
/// </summary>
public static class FishingHookTestSetup
{
    private const string MaterialFolder = "Assets/Art/Materials";
    private const string PrefabFolder = "Assets/Prefabs";
    private const string SceneFolder = "Assets/Scenes/Test";
    private const string ScenePath = SceneFolder + "/FishingHookTest.unity";

    private const string PlayerRigPath = PrefabFolder + "/PlayerRig.prefab";
    private const string ExplosionPrefabPath = PrefabFolder + "/FishingExplosion.prefab";
    private const string RopeMaterialPath = MaterialFolder + "/Rope.mat";
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

    private const float BarWidth = 420f;

    // ---- ステージの大きさ ----
    private const float ArenaHalf = 20f;      // 床の半分の大きさ（40m四方）
    private const float WallLine = 19f;       // 壁を置く位置
    private const float WallHeight = 1.5f;
    private const float WallThickness = 1f;
    private const float PocketWidth = 5f;     // 壁を切り欠く幅
    private const float PocketDepth = 4f;     // ポケットの奥行き

    private const int ZoneVisualCount = 2;    // スキルチェックの枠の表示数

    [MenuItem("Tools/Mirai01/釣りフックの検証シーンを作る")]
    public static void CreateScene()
    {
        EnsureFolder("Assets/Scenes");
        EnsureFolder(SceneFolder);
        EnsureFolder("Assets/Art");
        EnsureFolder(MaterialFolder);
        EnsureFolder(PrefabFolder);

        InputActionAsset inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        if (inputActions == null)
        {
            Debug.LogWarning(InputActionsPath + " が見つかりません。入力の設定は手で入れてください。");
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 爆発の見た目をプレハブとして用意する（作業用オブジェクトはすぐ消す）
        ExplosionEffect explosionPrefab = CreateExplosionPrefab();

        // ---- 明かり ----
        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

        // ---- 床 ----
        Material groundMaterial = GetOrCreateMaterial(MaterialFolder + "/TestGround.mat", new Color(0.72f, 0.72f, 0.72f));
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(ArenaHalf * 0.2f, 1f, ArenaHalf * 0.2f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

        // ---- 点数と画面表示 ----
        GameObject hudObject = new GameObject("FishingHUD");
        ScoreBoard scoreBoard = hudObject.AddComponent<ScoreBoard>();
        FishingStatusUI statusUI = hudObject.AddComponent<FishingStatusUI>();

        // ---- 四方の壁とポケット ----
        Material wallMaterial = GetOrCreateMaterial(MaterialFolder + "/FishingWall.mat", new Color(0.5f, 0.55f, 0.62f));
        Material padMaterial = GetOrCreateMaterial(MaterialFolder + "/FishingPocketPad.mat", new Color(0.25f, 0.4f, 0.6f));

        CreateWallWithPocket("North", "北", 0f, 0, scoreBoard, wallMaterial, padMaterial);
        CreateWallWithPocket("East", "東", 90f, 0, scoreBoard, wallMaterial, padMaterial);
        CreateWallWithPocket("South", "南", 180f, 0, scoreBoard, wallMaterial, padMaterial);
        CreateWallWithPocket("West", "西", 270f, 0, scoreBoard, wallMaterial, padMaterial);

        // ---- プレイヤー（既存の PlayerRig を土台にする）----
        GameObject player = CreatePlayer();

        // ---- 見下ろしカメラ ----
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 16f, -9f);
        cameraObject.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 500f;
        cameraObject.AddComponent<AudioListener>();
        TopDownCameraFollow follow = cameraObject.AddComponent<TopDownCameraFollow>();
        SetRef(follow, "target", player.transform);

        PlayerAimController aim = player.GetComponent<PlayerAimController>();
        SetRef(aim, "aimCamera", camera);
        SetInt(aim, "groundMask", ~0);

        // ---- フック先端 ----
        Material hookMaterial = GetOrCreateMaterial(MaterialFolder + "/FishingHook.mat", new Color(0.85f, 0.85f, 0.9f));
        GameObject hookObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        hookObject.name = "Hook";
        hookObject.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
        hookObject.GetComponent<MeshRenderer>().sharedMaterial = hookMaterial;
        hookObject.GetComponent<SphereCollider>().isTrigger = true;
        Rigidbody hookBody = hookObject.AddComponent<Rigidbody>();
        hookBody.isKinematic = true;
        hookBody.useGravity = false;
        HookProjectile hookProjectile = hookObject.AddComponent<HookProjectile>();

        // ---- 糸 ----
        Transform handPoint = player.transform.Find("HandPoint");
        GameObject lineObject = new GameObject("HookLine");
        lineObject.transform.SetParent(player.transform, false);
        LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
        lineRenderer.widthMultiplier = 0.06f;
        lineRenderer.numCapVertices = 2;
        lineRenderer.textureMode = LineTextureMode.Tile;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Material ropeMaterial = AssetDatabase.LoadAssetAtPath<Material>(RopeMaterialPath)
            ?? GetOrCreateMaterial(MaterialFolder + "/FishingLine.mat", new Color(0.9f, 0.9f, 0.8f));
        lineRenderer.sharedMaterial = ropeMaterial;
        HookLine hookLine = lineObject.AddComponent<HookLine>();

        // ---- 物資 ----
        Material supplyMaterial = GetOrCreateMaterial(MaterialFolder + "/FishingSupply.mat", new Color(0.85f, 0.6f, 0.3f));
        int supplyCount = 6;
        for (int i = 0; i < supplyCount; i++)
        {
            float angle = (360f / supplyCount) * i;
            Vector3 position = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, 7.5f);
            position.y = 0.4f;
            CreateSupply($"Supply_{i + 1}", position, supplyMaterial);
        }

        // ---- 爆発物 ----
        Material bombMaterial = GetOrCreateMaterial(MaterialFolder + "/FishingBomb.mat", new Color(0.35f, 0.32f, 0.36f));
        Material fuseMaterial = GetOrCreateMaterial(MaterialFolder + "/FishingBombFuse.mat", new Color(0.95f, 0.75f, 0.2f));
        CreateBomb("Bomb_1", new Vector3(-5.5f, 0.5f, 5.5f), bombMaterial, fuseMaterial, explosionPrefab);
        CreateBomb("Bomb_2", new Vector3(5.5f, 0.5f, -5.5f), bombMaterial, fuseMaterial, explosionPrefab);

        // ---- UI（チャージとスキルチェック）----
        HookChargeUI ui = CreateUI();

        // ---- スタンの見せ方 ----
        PlayerStun stun = player.GetComponent<PlayerStun>();
        WireStun(player, stun);

        // ---- プレイヤー側スクリプトの結線 ----
        HookController hookController = player.GetComponent<HookController>();
        ThrowController throwController = player.GetComponent<ThrowController>();
        FishingPlayerController mover = player.GetComponent<FishingPlayerController>();

        SetRef(mover, "aim", aim);
        SetRef(mover, "stun", stun);
        SetRef(mover, "inputActions", inputActions);

        SetRef(hookController, "aim", aim);
        SetRef(hookController, "handPoint", handPoint);
        SetRef(hookController, "hook", hookProjectile);
        SetRef(hookController, "line", hookLine);
        SetRef(hookController, "throwController", throwController);
        SetRef(hookController, "ui", ui);
        SetRef(hookController, "stun", stun);
        SetRef(hookController, "inputActions", inputActions);
        SetInt(hookController, "hookableMask", ~0);

        SetRef(throwController, "hook", hookController);
        SetRef(throwController, "inputActions", inputActions);

        SetRef(statusUI, "scoreBoard", scoreBoard);
        SetRef(statusUI, "playerStun", stun);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "釣りフックの検証シーンを作りました。\n" +
            "シーン: " + ScenePath + "\n" +
            "・WASDで移動、マウスで向き。左クリックを押し込んでチャージ→離してフック発射\n" +
            "・物資に当たると引き寄せが始まり、ゲージが1回だけ左から右へ動く\n" +
            "・前半の枠で押すと後ろへ、後半の枠で押すと前へ飛ばす（枠を外すとミス）\n" +
            "・四方の壁のポケットに入れると点が入る\n" +
            "・黒い箱は爆発物。釣り上げてから5秒で爆発する");
    }

    // ------------------------------------------------------------
    // プレイヤー
    // ------------------------------------------------------------

    private static GameObject CreatePlayer()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRigPath);
        GameObject player;

        if (prefab != null)
        {
            player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            // 検証シーンを PlayerRig.prefab の変更から切り離すため、完全に展開する
            PrefabUtility.UnpackPrefabInstance(player, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }
        else
        {
            Debug.LogWarning(PlayerRigPath + " が見つかりません。仮のカプセルでプレイヤーを作ります。");
            player = new GameObject("PlayerRig");
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(player.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            Object.DestroyImmediate(body.GetComponent<Collider>());
        }

        player.name = "PlayerRig";
        player.transform.position = Vector3.zero;
        player.transform.rotation = Quaternion.identity;

        // 一人称/三人称のマウス視点操作は見下ろしと噛み合わないので、丸ごと取り除く。
        // （enabled = false だけだと Awake が走ってカーソルをロックしたりエラーを出したりする）
        RemoveComponent<PlayerViewSwitcher>(player);
        RemoveComponent<PlayerController>(player);

        // PlayerRig の中にカメラが入っているので取り除く（見下ろしカメラを別に置くため）
        foreach (Camera childCamera in player.GetComponentsInChildren<Camera>(true))
        {
            Object.DestroyImmediate(childCamera.gameObject);
        }

        // 一人称用の照準キャンバスは隠す
        Transform reticle = player.transform.Find("ReticleCanvas");
        if (reticle != null)
        {
            reticle.gameObject.SetActive(false);
        }

        // 手元（フックと糸の起点）
        if (player.transform.Find("HandPoint") == null)
        {
            GameObject hand = new GameObject("HandPoint");
            hand.transform.SetParent(player.transform, false);
            hand.transform.localPosition = new Vector3(0f, 1.0f, 0.6f);
        }

        if (player.GetComponent<CharacterController>() == null)
        {
            CharacterController cc = player.AddComponent<CharacterController>();
            cc.height = 2f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0f, 1f, 0f);
        }

        player.AddComponent<PlayerAimController>();
        player.AddComponent<PlayerStun>();
        player.AddComponent<FishingPlayerController>();
        player.AddComponent<HookController>();
        player.AddComponent<ThrowController>();

        return player;
    }

    /// <summary>スタン中に点滅させる見た目と、頭の上に出す印をつなぐ。</summary>
    private static void WireStun(GameObject player, PlayerStun stun)
    {
        // 点滅させるのは体の見た目だけ（頭の上の印は別に出す）
        Transform body = player.transform.Find("Body");
        Renderer[] bodyRenderers = body != null
            ? body.GetComponentsInChildren<Renderer>(true)
            : new Renderer[0];

        SetObjectArray(stun, "bodyRenderers", bodyRenderers);

        // 頭の上に出す印
        Material markMaterial = GetOrCreateMaterial(
            MaterialFolder + "/FishingStunMark.mat", new Color(1f, 0.25f, 0.2f));

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "StunMarker";
        marker.transform.SetParent(player.transform, false);
        marker.transform.localPosition = new Vector3(0f, 2.45f, 0f);
        marker.transform.localScale = new Vector3(0.5f, 0.12f, 0.12f);
        marker.GetComponent<MeshRenderer>().sharedMaterial = markMaterial;
        Object.DestroyImmediate(marker.GetComponent<Collider>());
        marker.SetActive(false);

        SetRef(stun, "stunMarker", marker);
    }

    // ------------------------------------------------------------
    // 壁とポケット
    // ------------------------------------------------------------

    /// <summary>
    /// 1辺ぶんの壁と、その中央のポケットを作る。
    ///
    /// <paramref name="yaw"/> で向きを決め、ローカルの +Z を「外向き」として組む。
    /// こうすると、同じ計算で4辺すべてを作れる。
    /// </summary>
    private static void CreateWallWithPocket(string name, string label, float yaw,
        int ownerPlayerIndex, ScoreBoard scoreBoard, Material wallMaterial, Material padMaterial)
    {
        GameObject root = new GameObject($"Wall_{name}");
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        float pocketHalf = PocketWidth * 0.5f;
        float segmentLength = ArenaHalf - pocketHalf;
        float segmentCenter = pocketHalf + segmentLength * 0.5f;
        float wallY = WallHeight * 0.5f;

        // 壁を左右2枚に分けて、中央にポケットの入口を空ける
        AddBox(root.transform, "Segment_L",
            new Vector3(-segmentCenter, wallY, WallLine),
            new Vector3(segmentLength, WallHeight, WallThickness), wallMaterial);
        AddBox(root.transform, "Segment_R",
            new Vector3(segmentCenter, wallY, WallLine),
            new Vector3(segmentLength, WallHeight, WallThickness), wallMaterial);

        // ポケットの床（入ったときに光る部分）
        float padZ = WallLine + PocketDepth * 0.5f;
        GameObject pad = AddBox(root.transform, "Pad",
            new Vector3(0f, 0.05f, padZ),
            new Vector3(PocketWidth, 0.1f, PocketDepth), padMaterial);

        // ポケットの奥と左右
        AddBox(root.transform, "Pocket_Back",
            new Vector3(0f, wallY, WallLine + PocketDepth + WallThickness * 0.5f),
            new Vector3(PocketWidth + 2f, WallHeight, WallThickness), wallMaterial);
        AddBox(root.transform, "Pocket_Side_L",
            new Vector3(-(pocketHalf + 0.5f), wallY, padZ),
            new Vector3(1f, WallHeight, PocketDepth), wallMaterial);
        AddBox(root.transform, "Pocket_Side_R",
            new Vector3(pocketHalf + 0.5f, wallY, padZ),
            new Vector3(1f, WallHeight, PocketDepth), wallMaterial);

        // 得点になる当たり判定
        GameObject trigger = new GameObject($"Pocket_{name}");
        trigger.transform.SetParent(root.transform, false);
        trigger.transform.localPosition = new Vector3(0f, WallHeight * 0.5f, padZ);
        BoxCollider triggerCollider = trigger.AddComponent<BoxCollider>();
        triggerCollider.isTrigger = true;
        triggerCollider.size = new Vector3(PocketWidth - 0.4f, WallHeight + 0.4f, PocketDepth - 0.4f);

        ScorePocket pocket = trigger.AddComponent<ScorePocket>();
        SetString(pocket, "pocketLabel", label);
        SetInt(pocket, "ownerPlayerIndex", ownerPlayerIndex);
        SetRef(pocket, "scoreBoard", scoreBoard);
        SetRef(pocket, "padRenderer", pad.GetComponent<MeshRenderer>());
    }

    private static GameObject AddBox(Transform parent, string name,
        Vector3 localPosition, Vector3 size, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPosition;
        box.transform.localScale = size;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
        return box;
    }

    // ------------------------------------------------------------
    // 物資・爆発物
    // ------------------------------------------------------------

    private static void CreateSupply(string name, Vector3 position, Material material)
    {
        GameObject supply = GameObject.CreatePrimitive(PrimitiveType.Cube);
        supply.name = name;
        supply.transform.position = position;
        supply.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
        supply.GetComponent<MeshRenderer>().sharedMaterial = material;

        Rigidbody body = supply.AddComponent<Rigidbody>();
        body.mass = 1f;
        body.linearDamping = 0.4f;
        body.angularDamping = 0.6f;

        supply.AddComponent<HookableObject>();
    }

    /// <summary>爆発する物資を1つ置く。**点数は0**にしてポケットに入れても得点にならないようにする。</summary>
    private static void CreateBomb(string name, Vector3 position,
        Material bodyMaterial, Material fuseMaterial, ExplosionEffect explosionPrefab)
    {
        GameObject bomb = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bomb.name = name;
        bomb.transform.position = position;
        bomb.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
        MeshRenderer bombRenderer = bomb.GetComponent<MeshRenderer>();
        bombRenderer.sharedMaterial = bodyMaterial;

        // 爆発物だと見て分かるように、上に導火線を立てる
        GameObject fuse = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        fuse.name = "Fuse";
        fuse.transform.SetParent(bomb.transform, false);
        fuse.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        fuse.transform.localScale = new Vector3(0.18f, 0.22f, 0.18f);
        fuse.GetComponent<MeshRenderer>().sharedMaterial = fuseMaterial;
        Object.DestroyImmediate(fuse.GetComponent<Collider>());

        Rigidbody body = bomb.AddComponent<Rigidbody>();
        body.mass = 1.2f;
        body.linearDamping = 0.4f;
        body.angularDamping = 0.6f;

        HookableObject hookable = bomb.AddComponent<HookableObject>();
        SetInt(hookable, "scoreValue", 0);
        SetFloat(hookable, "respawnSeconds", 8f);

        ExplosiveObject explosive = bomb.AddComponent<ExplosiveObject>();
        SetObjectArray(explosive, "blinkRenderers", new Renderer[] { bombRenderer });
        SetRef(explosive, "explosionEffectPrefab", explosionPrefab);
    }

    /// <summary>爆発の見た目のプレハブを作る（すでにあれば作り直す）。</summary>
    private static ExplosionEffect CreateExplosionPrefab()
    {
        Material material = GetOrCreateMaterial(
            MaterialFolder + "/FishingExplosion.mat", new Color(1f, 0.55f, 0.12f));

        GameObject root = new GameObject("FishingExplosion");
        ExplosionEffect effect = root.AddComponent<ExplosionEffect>();

        GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Ball";
        ball.transform.SetParent(root.transform, false);
        ball.transform.localScale = Vector3.zero;
        ball.GetComponent<MeshRenderer>().sharedMaterial = material;
        Object.DestroyImmediate(ball.GetComponent<Collider>());

        GameObject flashObject = new GameObject("Flash");
        flashObject.transform.SetParent(root.transform, false);
        Light flash = flashObject.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = new Color(1f, 0.6f, 0.25f);
        flash.intensity = 0f;
        flash.range = 12f;

        SetRef(effect, "ball", ball.transform);
        SetRef(effect, "flash", flash);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ExplosionPrefabPath);
        Object.DestroyImmediate(root);

        return saved != null ? saved.GetComponent<ExplosionEffect>() : null;
    }

    // ------------------------------------------------------------
    // UI
    // ------------------------------------------------------------

    private static HookChargeUI CreateUI()
    {
        GameObject canvasObject = new GameObject("HookUI",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        // ---- チャージ表示（下から70px、中央）----
        GameObject chargeRoot = MakeContainer("ChargeRoot", canvasObject.transform, new Vector2(0f, 70f));
        MakeImage("ChargeBG", chargeRoot.transform,
            new Vector2(BarWidth, 24f), new Color(0.12f, 0.12f, 0.12f, 0.8f), Vector2.zero);
        Image chargeFill = MakeImage("ChargeFill", chargeRoot.transform,
            new Vector2(BarWidth, 24f), new Color(0.3f, 0.8f, 1f, 1f), new Vector2(-BarWidth * 0.5f, 0f));
        chargeFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        chargeFill.rectTransform.localScale = new Vector3(0f, 1f, 1f);

        // ---- スキルチェック表示（下から120px、中央）----
        GameObject timingRoot = MakeContainer("TimingRoot", canvasObject.transform, new Vector2(0f, 120f));
        MakeImage("TimingBG", timingRoot.transform,
            new Vector2(BarWidth, 28f), new Color(0.12f, 0.12f, 0.12f, 0.8f), Vector2.zero);

        // 枠は「通常→良い→最適」の順に重ねる（あとに作ったほうが上に出る）
        GameObject[] zoneRoots = new GameObject[ZoneVisualCount];
        Image[] hitRects = new Image[ZoneVisualCount];
        Image[] goodRects = new Image[ZoneVisualCount];
        Image[] perfectRects = new Image[ZoneVisualCount];

        // 前半＝後ろへ投げる枠は青、後半＝前へ投げる枠は橙、で見分けられるようにする
        Color[] zoneTints =
        {
            new Color(0.35f, 0.6f, 1f, 0.35f),
            new Color(1f, 0.6f, 0.25f, 0.35f)
        };

        for (int i = 0; i < ZoneVisualCount; i++)
        {
            zoneRoots[i] = MakeContainer($"Zone{i + 1}", timingRoot.transform, Vector2.zero);
            zoneRoots[i].GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0.5f);
            zoneRoots[i].GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
            zoneRoots[i].GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0.5f);
            zoneRoots[i].GetComponent<RectTransform>().anchoredPosition = Vector2.zero;

            Color tint = zoneTints[Mathf.Min(i, zoneTints.Length - 1)];

            hitRects[i] = MakeImage("Hit", zoneRoots[i].transform,
                new Vector2(90f, 28f), tint, Vector2.zero);
            goodRects[i] = MakeImage("Good", zoneRoots[i].transform,
                new Vector2(50f, 28f), new Color(1f, 0.85f, 0.2f, 0.55f), Vector2.zero);
            perfectRects[i] = MakeImage("Perfect", zoneRoots[i].transform,
                new Vector2(18f, 28f), new Color(0.3f, 1f, 0.45f, 0.8f), Vector2.zero);
        }

        // マーカーは一番上に出したいので、最後に作る
        Image marker = MakeImage("Marker", timingRoot.transform,
            new Vector2(6f, 44f), Color.white, Vector2.zero);

        chargeRoot.SetActive(false);
        timingRoot.SetActive(false);

        HookChargeUI ui = canvasObject.AddComponent<HookChargeUI>();
        SetRef(ui, "chargeRoot", chargeRoot);
        SetRef(ui, "chargeFill", chargeFill.rectTransform);
        SetRef(ui, "timingRoot", timingRoot);
        SetRef(ui, "timingMarker", marker.rectTransform);
        SetFloat(ui, "barWidth", BarWidth);

        // 枠の見た目（入れ子のクラスの配列）をつなぐ
        SerializedObject serialized = new SerializedObject(ui);
        SerializedProperty zones = serialized.FindProperty("zoneVisuals");
        zones.arraySize = ZoneVisualCount;

        for (int i = 0; i < ZoneVisualCount; i++)
        {
            SerializedProperty element = zones.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("root").objectReferenceValue = zoneRoots[i];
            element.FindPropertyRelative("hitRect").objectReferenceValue = hitRects[i].rectTransform;
            element.FindPropertyRelative("goodRect").objectReferenceValue = goodRects[i].rectTransform;
            element.FindPropertyRelative("perfectRect").objectReferenceValue = perfectRects[i].rectTransform;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();

        return ui;
    }

    private static GameObject MakeContainer(string name, Transform parent, Vector2 anchoredPosition)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = Vector2.zero;
        return go;
    }

    private static Image MakeImage(string name, Transform parent, Vector2 size, Color color, Vector2 anchoredPosition)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPosition;
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    // ------------------------------------------------------------
    // 補助
    // ------------------------------------------------------------

    private static void RemoveComponent<T>(GameObject target) where T : Component
    {
        // 依存（RequireComponent）で消せないことがあるので、消えるまで数回試す
        for (int i = 0; i < 4; i++)
        {
            T component = target.GetComponent<T>();
            if (component == null)
            {
                return;
            }
            Object.DestroyImmediate(component, true);
        }
    }

    private static void SetRef(Object owner, string field, Object value)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            Debug.LogWarning($"{owner.GetType().Name} に {field} が見つかりません。");
            return;
        }
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetObjectArray(Object owner, string field, Object[] values)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            Debug.LogWarning($"{owner.GetType().Name} に {field} が見つかりません。");
            return;
        }

        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetInt(Object owner, string field, int value)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            return;
        }
        property.intValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetFloat(Object owner, string field, float value)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            return;
        }
        property.floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetString(Object owner, string field, string value)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            return;
        }
        property.stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Material GetOrCreateMaterial(string path, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new Material(shader) { color = color };

        if (AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateAsset(material, path);
        }

        return material;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        int lastSlash = path.LastIndexOf('/');
        string parent = path.Substring(0, lastSlash);
        string folderName = path.Substring(lastSlash + 1);
        AssetDatabase.CreateFolder(parent, folderName);
    }
}

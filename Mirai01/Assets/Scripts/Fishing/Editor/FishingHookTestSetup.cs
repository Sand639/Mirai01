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
/// 中身：見下ろしカメラ、床、マウス方向を向くプレイヤー（既存の PlayerRig を土台に使用）、
///       引っ掛けられる物資、チャージ量とタイミングの簡単な表示。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// ※ 何度実行しても作り直せる（シーンを上書きする）。
/// </summary>
public static class FishingHookTestSetup
{
    private const string MaterialFolder = "Assets/Art/Materials";
    private const string SceneFolder = "Assets/Scenes/Test";
    private const string ScenePath = SceneFolder + "/FishingHookTest.unity";

    private const string PlayerRigPath = "Assets/Prefabs/PlayerRig.prefab";
    private const string RopeMaterialPath = MaterialFolder + "/Rope.mat";
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

    private const float BarWidth = 420f;

    [MenuItem("Tools/Mirai01/釣りフックの検証シーンを作る")]
    public static void CreateScene()
    {
        EnsureFolder("Assets/Scenes");
        EnsureFolder(SceneFolder);
        EnsureFolder("Assets/Art");
        EnsureFolder(MaterialFolder);

        InputActionAsset inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        if (inputActions == null)
        {
            Debug.LogWarning(InputActionsPath + " が見つかりません。入力の設定は手で入れてください。");
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

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
        ground.transform.localScale = new Vector3(4f, 1f, 4f); // 40m 四方
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

        // ---- 周りの低い壁（投げた物資が転がり出ないように）----
        Material wallMaterial = GetOrCreateMaterial(MaterialFolder + "/FishingWall.mat", new Color(0.5f, 0.55f, 0.62f));
        CreateWall("Wall_North", new Vector3(0f, 0.75f, 19f), new Vector3(40f, 1.5f, 1f), wallMaterial);
        CreateWall("Wall_South", new Vector3(0f, 0.75f, -19f), new Vector3(40f, 1.5f, 1f), wallMaterial);
        CreateWall("Wall_East", new Vector3(19f, 0.75f, 0f), new Vector3(1f, 1.5f, 40f), wallMaterial);
        CreateWall("Wall_West", new Vector3(-19f, 0.75f, 0f), new Vector3(1f, 1.5f, 40f), wallMaterial);

        // ---- プレイヤー（既存の PlayerRig を土台にする）----
        GameObject player = CreatePlayer(inputActions);

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

        // カメラ参照を狙い計算に渡す
        PlayerAimController aim = player.GetComponent<PlayerAimController>();
        SetRef(aim, "aimCamera", camera);
        SetInt(aim, "groundMask", ~0);

        // ---- フック先端 ----
        Material hookMaterial = GetOrCreateMaterial(MaterialFolder + "/FishingHook.mat", new Color(0.85f, 0.85f, 0.9f));
        GameObject hookObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        hookObject.name = "Hook";
        hookObject.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
        hookObject.GetComponent<MeshRenderer>().sharedMaterial = hookMaterial;
        SphereCollider hookCollider = hookObject.GetComponent<SphereCollider>();
        hookCollider.isTrigger = true;
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
        Material ropeMaterial = AssetDatabase.LoadAssetAtPath<Material>(RopeMaterialPath);
        if (ropeMaterial == null)
        {
            ropeMaterial = GetOrCreateMaterial(MaterialFolder + "/FishingLine.mat", new Color(0.9f, 0.9f, 0.8f));
        }
        lineRenderer.sharedMaterial = ropeMaterial;
        HookLine hookLine = lineObject.AddComponent<HookLine>();

        // ---- 物資 ----
        Material supplyMaterial = GetOrCreateMaterial(MaterialFolder + "/FishingSupply.mat", new Color(0.85f, 0.6f, 0.3f));
        int count = 6;
        for (int i = 0; i < count; i++)
        {
            float angle = (360f / count) * i;
            Vector3 position = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, 7.5f);
            position.y = 0.4f;
            CreateSupply($"Supply_{i + 1}", position, supplyMaterial);
        }

        // ---- UI ----
        HookChargeUI ui = CreateUI();

        // ---- プレイヤー側スクリプトの結線 ----
        HookController hookController = player.GetComponent<HookController>();
        ThrowController throwController = player.GetComponent<ThrowController>();
        FishingPlayerController mover = player.GetComponent<FishingPlayerController>();

        SetRef(mover, "aim", aim);
        SetRef(mover, "inputActions", inputActions);

        SetRef(hookController, "aim", aim);
        SetRef(hookController, "handPoint", handPoint);
        SetRef(hookController, "hook", hookProjectile);
        SetRef(hookController, "line", hookLine);
        SetRef(hookController, "throwController", throwController);
        SetRef(hookController, "ui", ui);
        SetRef(hookController, "inputActions", inputActions);
        SetInt(hookController, "hookableMask", ~0);

        SetRef(throwController, "hook", hookController);
        SetRef(throwController, "inputActions", inputActions);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "釣りフックの検証シーンを作りました。\n" +
            "シーン: " + ScenePath + "\n" +
            "遊び方：WASDで移動、マウスで向き。左クリックを押し込んでチャージ→離してフック発射。\n" +
            "物資に当たると引き寄せが始まるので、ゲージが枠に入った瞬間に左クリックで投げる。");
    }

    // ------------------------------------------------------------
    // プレイヤー
    // ------------------------------------------------------------

    private static GameObject CreatePlayer(InputActionAsset inputActions)
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
            CharacterController cc = player.AddComponent<CharacterController>();
            cc.height = 2f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0f, 1f, 0f);
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
        Transform handPoint = player.transform.Find("HandPoint");
        if (handPoint == null)
        {
            GameObject hand = new GameObject("HandPoint");
            hand.transform.SetParent(player.transform, false);
            hand.transform.localPosition = new Vector3(0f, 1.0f, 0.6f);
            handPoint = hand.transform;
        }

        if (player.GetComponent<CharacterController>() == null)
        {
            CharacterController cc = player.AddComponent<CharacterController>();
            cc.height = 2f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0f, 1f, 0f);
        }

        player.AddComponent<PlayerAimController>();
        player.AddComponent<FishingPlayerController>();
        player.AddComponent<HookController>();
        player.AddComponent<ThrowController>();

        return player;
    }

    // ------------------------------------------------------------
    // 物資・壁
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

    private static void CreateWall(string name, Vector3 position, Vector3 size, Material material)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = size;
        wall.GetComponent<MeshRenderer>().sharedMaterial = material;
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

        // チャージ表示（下から70px、中央）
        GameObject chargeRoot = MakeContainer("ChargeRoot", canvasObject.transform, new Vector2(0f, 70f));
        Image chargeBg = MakeImage("ChargeBG", chargeRoot.transform,
            new Vector2(BarWidth, 24f), new Color(0.12f, 0.12f, 0.12f, 0.8f), Vector2.zero);
        Image chargeFill = MakeImage("ChargeFill", chargeRoot.transform,
            new Vector2(BarWidth, 24f), new Color(0.3f, 0.8f, 1f, 1f), new Vector2(-BarWidth * 0.5f, 0f));
        chargeFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        chargeFill.rectTransform.localScale = new Vector3(0f, 1f, 1f);
        chargeBg.raycastTarget = false;

        // タイミング表示（下から120px、中央）
        GameObject timingRoot = MakeContainer("TimingRoot", canvasObject.transform, new Vector2(0f, 120f));
        MakeImage("TimingBG", timingRoot.transform,
            new Vector2(BarWidth, 28f), new Color(0.12f, 0.12f, 0.12f, 0.8f), Vector2.zero);
        Image goodZone = MakeImage("GoodZone", timingRoot.transform,
            new Vector2(120f, 28f), new Color(1f, 0.85f, 0.2f, 0.45f), Vector2.zero);
        Image perfectZone = MakeImage("PerfectZone", timingRoot.transform,
            new Vector2(40f, 28f), new Color(0.3f, 1f, 0.45f, 0.7f), Vector2.zero);
        Image marker = MakeImage("Marker", timingRoot.transform,
            new Vector2(6f, 44f), Color.white, Vector2.zero);

        chargeRoot.SetActive(false);
        timingRoot.SetActive(false);

        HookChargeUI ui = canvasObject.AddComponent<HookChargeUI>();
        SetRef(ui, "chargeRoot", chargeRoot);
        SetRef(ui, "chargeFill", chargeFill.rectTransform);
        SetRef(ui, "timingRoot", timingRoot);
        SetRef(ui, "timingMarker", marker.rectTransform);
        SetRef(ui, "goodZone", goodZone.rectTransform);
        SetRef(ui, "perfectZone", perfectZone.rectTransform);
        SetFloat(ui, "barWidth", BarWidth);

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

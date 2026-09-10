using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **オンライン用の部品を組み立てる置き場。**
///
/// 1人用の部品は <see cref="FishingSceneBuilder"/> にあり、
/// **通信のための上乗せだけ**をこちらに分けてある。
/// こうしておくと、1人用のシーンを作る処理に手を入れずにオンラインを足せる。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
internal static class FishingNetSceneBuilder
{
    public const string OnlinePlayerPrefabPath =
        FishingSceneBuilder.PrefabFolder + "/FishingOnlinePlayer.prefab";

    /// <summary>1秒あたり何回、位置などを送るか（`LanPlayTestSetup` と同じ値）。</summary>
    public const uint NetworkTickRate = 60;

    /// <summary>プレイヤーをなめらかに見せるための待ち時間（秒）。短いほど反応がよい。</summary>
    private const float PlayerInterpolationTime = 0.05f;

    /// <summary>物資をなめらかに見せるための待ち時間（秒）。物理なので少し長め。</summary>
    private const float SupplyInterpolationTime = 0.075f;

    // ------------------------------------------------------------
    // オンライン用プレイヤーのプレハブ
    // ------------------------------------------------------------

    /// <summary>
    /// **オンラインで1人分になるプレハブを作る。**
    ///
    /// `NetworkManager` の Player Prefab に入れると、つないだ人数分だけ自動で生まれる。
    /// **フックと糸をプレハブの中に入れてある**のが1人用との違い
    /// （1人用はシーンに1つ置いているが、オンラインでは人ごとに必要なため）。
    ///
    /// 見た目は基本形状のカプセル。`PlayerRig.prefab` を使わない理由は、
    /// 中に一人称/三人称の操作とカメラが入っており、
    /// **プレハブとして持つと毎回それを取り除く処理が必要になる**ため。
    /// </summary>
    public static GameObject CreateOnlinePlayerPrefab(InputActionAsset inputActions)
    {
        Material bodyMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/PlayerRigBody.mat", Color.white);

        GameObject root = new GameObject("FishingOnlinePlayer");

        // ---- 通信の部品 ----
        root.AddComponent<NetworkObject>();

        NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
        networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        networkTransform.SyncScaleX = false;
        networkTransform.SyncScaleY = false;
        networkTransform.SyncScaleZ = false;
        networkTransform.PositionMaxInterpolationTime = PlayerInterpolationTime;
        networkTransform.RotationMaxInterpolationTime = PlayerInterpolationTime;
        networkTransform.UseUnreliableDeltas = true;

        // ---- 体 ----
        CharacterController controller = root.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = 0.4f;
        controller.center = new Vector3(0f, 1f, 0f);
        controller.slopeLimit = 50f;
        controller.stepOffset = 0.3f;

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        MeshRenderer bodyRenderer = body.GetComponent<MeshRenderer>();
        bodyRenderer.sharedMaterial = bodyMaterial;

        // 前を向いているのが分かる印（体の子にしておくと、色も一緒に変わる）
        GameObject frontMark = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frontMark.name = "FrontMark";
        frontMark.transform.SetParent(body.transform, false);
        frontMark.transform.localPosition = new Vector3(0f, 0.25f, 0.5f);
        frontMark.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
        Object.DestroyImmediate(frontMark.GetComponent<Collider>());
        MeshRenderer frontRenderer = frontMark.GetComponent<MeshRenderer>();
        frontRenderer.sharedMaterial = bodyMaterial;

        // ---- 手元・糸・フック ----
        GameObject hand = new GameObject("HandPoint");
        hand.transform.SetParent(root.transform, false);
        hand.transform.localPosition = new Vector3(0f, 1.0f, 0.6f);

        GameObject lineObject = new GameObject("HookLine");
        lineObject.transform.SetParent(root.transform, false);
        LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
        lineRenderer.widthMultiplier = 0.06f;
        lineRenderer.numCapVertices = 2;
        lineRenderer.textureMode = LineTextureMode.Tile;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.sharedMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/Rope.mat", new Color(0.9f, 0.9f, 0.8f));
        HookLine hookLine = lineObject.AddComponent<HookLine>();

        GameObject hookObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        hookObject.name = "Hook";
        hookObject.transform.SetParent(root.transform, false);
        hookObject.transform.localPosition = new Vector3(0f, 1.0f, 0.6f);
        hookObject.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
        hookObject.GetComponent<MeshRenderer>().sharedMaterial =
            FishingSceneBuilder.GetOrCreateMaterial(
                FishingSceneBuilder.MaterialFolder + "/FishingHook.mat",
                new Color(0.85f, 0.85f, 0.9f));
        hookObject.GetComponent<SphereCollider>().isTrigger = true;
        Rigidbody hookBody = hookObject.AddComponent<Rigidbody>();
        hookBody.isKinematic = true;
        hookBody.useGravity = false;
        HookProjectile hookProjectile = hookObject.AddComponent<HookProjectile>();

        // ---- スタンの印 ----
        GameObject stunMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stunMarker.name = "StunMarker";
        stunMarker.transform.SetParent(root.transform, false);
        stunMarker.transform.localPosition = new Vector3(0f, 2.45f, 0f);
        stunMarker.transform.localScale = new Vector3(0.5f, 0.12f, 0.12f);
        stunMarker.GetComponent<MeshRenderer>().sharedMaterial =
            FishingSceneBuilder.GetOrCreateMaterial(
                FishingSceneBuilder.MaterialFolder + "/FishingStunMark.mat",
                new Color(1f, 0.25f, 0.2f));
        Object.DestroyImmediate(stunMarker.GetComponent<Collider>());
        stunMarker.SetActive(false);

        // ---- 操作のスクリプト ----
        PlayerAimController aim = root.AddComponent<PlayerAimController>();
        PlayerStun stun = root.AddComponent<PlayerStun>();
        FishingPlayerController mover = root.AddComponent<FishingPlayerController>();
        HookController hookController = root.AddComponent<HookController>();
        ThrowController throwController = root.AddComponent<ThrowController>();
        FishingNetPlayer netPlayer = root.AddComponent<FishingNetPlayer>();

        // ---- 結線 ----
        FishingSceneBuilder.SetInt(aim, "groundMask", ~0);

        FishingSceneBuilder.SetObjectArray(stun, "bodyRenderers",
            new Renderer[] { bodyRenderer, frontRenderer });
        FishingSceneBuilder.SetRef(stun, "stunMarker", stunMarker);

        FishingSceneBuilder.SetRef(mover, "aim", aim);
        FishingSceneBuilder.SetRef(mover, "stun", stun);
        FishingSceneBuilder.SetRef(mover, "inputActions", inputActions);

        FishingSceneBuilder.SetRef(hookController, "aim", aim);
        FishingSceneBuilder.SetRef(hookController, "handPoint", hand.transform);
        FishingSceneBuilder.SetRef(hookController, "hook", hookProjectile);
        FishingSceneBuilder.SetRef(hookController, "line", hookLine);
        FishingSceneBuilder.SetRef(hookController, "throwController", throwController);
        FishingSceneBuilder.SetRef(hookController, "stun", stun);
        FishingSceneBuilder.SetRef(hookController, "netPlayer", netPlayer);
        FishingSceneBuilder.SetRef(hookController, "inputActions", inputActions);
        FishingSceneBuilder.SetInt(hookController, "hookableMask", ~0);

        FishingSceneBuilder.SetRef(throwController, "hook", hookController);
        FishingSceneBuilder.SetRef(throwController, "inputActions", inputActions);

        // 自分のぶんだけ有効にするスクリプト（他の人のぶんは止める）
        FishingSceneBuilder.SetObjectArray(netPlayer, "ownerOnlyScripts",
            new MonoBehaviour[] { aim, mover, hookController, throwController });
        FishingSceneBuilder.SetRef(netPlayer, "characterController", controller);
        FishingSceneBuilder.SetObjectArray(netPlayer, "teamRenderers",
            new Renderer[] { bodyRenderer, frontRenderer });
        FishingSceneBuilder.SetRef(netPlayer, "hookVisual", hookObject.transform);
        FishingSceneBuilder.SetRef(netPlayer, "line", hookLine);
        FishingSceneBuilder.SetRef(netPlayer, "handPoint", hand.transform);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, OnlinePlayerPrefabPath);
        Object.DestroyImmediate(root);

        return saved;
    }

    // ------------------------------------------------------------
    // オンライン用のゴール（ポケット）
    // ------------------------------------------------------------

    /// <summary>
    /// 四方に、中央を切り欠いてゴールを付けた壁を置く（オンライン用）。
    /// 1人用との違いは、ゴールが <see cref="FishingNetPocket"/> になり、
    /// **持ち主のチームが人数に応じて変わる**こと。
    /// </summary>
    public static void CreateNetworkPocketWalls()
    {
        Material wallMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/FishingWall.mat", new Color(0.5f, 0.55f, 0.62f));
        Material padMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/FishingPocketPad.mat", new Color(0.25f, 0.4f, 0.6f));

        // 並び順は FishingTeams.PocketPlaceName と合わせる（0=北 / 1=東 / 2=南 / 3=西）
        CreateNetworkWallWithPocket("North", 0, 0f, wallMaterial, padMaterial);
        CreateNetworkWallWithPocket("East", 1, 90f, wallMaterial, padMaterial);
        CreateNetworkWallWithPocket("South", 2, 180f, wallMaterial, padMaterial);
        CreateNetworkWallWithPocket("West", 3, 270f, wallMaterial, padMaterial);
    }

    private static void CreateNetworkWallWithPocket(string name, int pocketIndex, float yaw,
        Material wallMaterial, Material padMaterial)
    {
        GameObject root = new GameObject($"Wall_{name}");
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        float pocketHalf = FishingSceneBuilder.PocketWidth * 0.5f;
        float segmentLength = FishingSceneBuilder.ArenaHalf - pocketHalf;
        float segmentCenter = pocketHalf + segmentLength * 0.5f;
        float wallY = FishingSceneBuilder.WallHeight * 0.5f;
        float wallLine = FishingSceneBuilder.WallLine;
        float depth = FishingSceneBuilder.PocketDepth;
        float thickness = FishingSceneBuilder.WallThickness;
        float height = FishingSceneBuilder.WallHeight;

        FishingSceneBuilder.AddBox(root.transform, "Segment_L",
            new Vector3(-segmentCenter, wallY, wallLine),
            new Vector3(segmentLength, height, thickness), wallMaterial);
        FishingSceneBuilder.AddBox(root.transform, "Segment_R",
            new Vector3(segmentCenter, wallY, wallLine),
            new Vector3(segmentLength, height, thickness), wallMaterial);

        float padZ = wallLine + depth * 0.5f;
        GameObject pad = FishingSceneBuilder.AddBox(root.transform, "Pad",
            new Vector3(0f, 0.05f, padZ),
            new Vector3(FishingSceneBuilder.PocketWidth, 0.1f, depth), padMaterial);

        FishingSceneBuilder.AddBox(root.transform, "Pocket_Back",
            new Vector3(0f, wallY, wallLine + depth + thickness * 0.5f),
            new Vector3(FishingSceneBuilder.PocketWidth + 2f, height, thickness), wallMaterial);
        FishingSceneBuilder.AddBox(root.transform, "Pocket_Side_L",
            new Vector3(-(pocketHalf + 0.5f), wallY, padZ),
            new Vector3(1f, height, depth), wallMaterial);
        FishingSceneBuilder.AddBox(root.transform, "Pocket_Side_R",
            new Vector3(pocketHalf + 0.5f, wallY, padZ),
            new Vector3(1f, height, depth), wallMaterial);

        // 得点の判定。**通信で合図を送るので NetworkObject が必要**
        GameObject trigger = new GameObject($"Pocket_{name}");
        trigger.transform.SetParent(root.transform, false);
        trigger.transform.localPosition = new Vector3(0f, wallY, padZ);

        BoxCollider triggerCollider = trigger.AddComponent<BoxCollider>();
        triggerCollider.isTrigger = true;
        triggerCollider.size = new Vector3(
            FishingSceneBuilder.PocketWidth - 0.4f, height + 0.4f, depth - 0.4f);

        trigger.AddComponent<NetworkObject>();

        FishingNetPocket pocket = trigger.AddComponent<FishingNetPocket>();
        FishingSceneBuilder.SetInt(pocket, "pocketIndex", pocketIndex);
        FishingSceneBuilder.SetRef(pocket, "padRenderer", pad.GetComponent<MeshRenderer>());
    }

    // ------------------------------------------------------------
    // オンライン用の物資と爆発物
    // ------------------------------------------------------------

    /// <summary>プレイヤーの周りに、円形に物資を並べる（オンライン用）。</summary>
    public static void CreateNetworkSupplyRing(int count, float radius)
    {
        Material material = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/FishingSupply.mat", new Color(0.85f, 0.6f, 0.3f));

        for (int i = 0; i < count; i++)
        {
            float angle = (360f / count) * i;
            Vector3 position = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, radius);
            position.y = 0.4f;

            GameObject supply = FishingSceneBuilder.CreateBox(
                $"Supply_{i + 1}", position, new Vector3(0.8f, 0.8f, 0.8f), material);

            AddSupplyParts(supply, 1f);
        }
    }

    /// <summary>爆発する物資を1つ置く（オンライン用）。</summary>
    public static void CreateNetworkBomb(string name, Vector3 position, ExplosionEffect explosionPrefab)
    {
        Material bodyMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/FishingBomb.mat", new Color(0.35f, 0.32f, 0.36f));
        Material fuseMaterial = FishingSceneBuilder.GetOrCreateMaterial(
            FishingSceneBuilder.MaterialFolder + "/FishingBombFuse.mat", new Color(0.95f, 0.75f, 0.2f));

        GameObject bomb = FishingSceneBuilder.CreateBox(
            name, position, new Vector3(0.9f, 0.9f, 0.9f), bodyMaterial);
        MeshRenderer bombRenderer = bomb.GetComponent<MeshRenderer>();

        GameObject fuse = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        fuse.name = "Fuse";
        fuse.transform.SetParent(bomb.transform, false);
        fuse.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        fuse.transform.localScale = new Vector3(0.18f, 0.22f, 0.18f);
        fuse.GetComponent<MeshRenderer>().sharedMaterial = fuseMaterial;
        Object.DestroyImmediate(fuse.GetComponent<Collider>());

        HookableObject hookable = AddSupplyParts(bomb, 1.2f);
        FishingSceneBuilder.SetInt(hookable, "scoreValue", 0);
        FishingSceneBuilder.SetFloat(hookable, "respawnSeconds", 8f);

        ExplosiveObject explosive = bomb.AddComponent<ExplosiveObject>();
        FishingSceneBuilder.SetObjectArray(explosive, "blinkRenderers",
            new Renderer[] { bombRenderer });
        FishingSceneBuilder.SetRef(explosive, "explosionEffectPrefab", explosionPrefab);

        // 導火線を全員でそろえる部品（オンライン用）
        bomb.AddComponent<FishingNetBomb>();
    }

    /// <summary>物資に、物理と通信の部品をまとめて付ける。</summary>
    private static HookableObject AddSupplyParts(GameObject supply, float mass)
    {
        Rigidbody body = supply.AddComponent<Rigidbody>();
        body.mass = mass;
        body.linearDamping = 0.4f;
        body.angularDamping = 0.6f;

        HookableObject hookable = supply.AddComponent<HookableObject>();

        supply.AddComponent<NetworkObject>();

        // **持ち主が動かす形。** 引き寄せ中は引っ掛けた人が持ち主になり、
        // 投げたあとはホストが持ち主に戻る（FishingNetSupply が入れ替える）
        NetworkTransform networkTransform = supply.AddComponent<NetworkTransform>();
        networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        networkTransform.SyncScaleX = false;
        networkTransform.SyncScaleY = false;
        networkTransform.SyncScaleZ = false;
        networkTransform.PositionMaxInterpolationTime = SupplyInterpolationTime;
        networkTransform.RotationMaxInterpolationTime = SupplyInterpolationTime;

        supply.AddComponent<FishingNetSupply>();

        return hookable;
    }
}

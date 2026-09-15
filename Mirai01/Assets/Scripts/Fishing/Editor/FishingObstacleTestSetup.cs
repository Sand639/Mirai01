using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **動く障害物の検証シーンを作るツール。**（新しく足したツール）
///
/// Unityのメニュー「Tools > Mirai01 > 釣りの障害物の検証シーンを作る」から実行できる。
/// 作られるシーン：`Assets/Scenes/Test/FishingObstacleTest.unity`
///
/// 中身は「ステージ」検証シーン（ポケット・物資）に、**動く障害物を3つ**足したもの。
///   ・北側を東西に行き来する障害物（行って戻る）
///   ・南側を四角くぐるぐる回る障害物（角で止まる）
///   ・西側を南北に行き来する障害物（ゆっくり止まって、ゆっくり動き出す）
/// 爆発物は、障害物だけを確かめやすいように置いていない。
///
/// **他の検証シーン（FishingHookTest / FishingArenaTest / オンライン用）は上書きしない。**
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// ※ 何度実行しても作り直せる（シーンを上書きする）。
/// </summary>
public static class FishingObstacleTestSetup
{
    private const string ScenePath = FishingSceneBuilder.SceneFolder + "/FishingObstacleTest.unity";
    public const string ObstacleMaterialPath = FishingSceneBuilder.MaterialFolder + "/FishingObstacle.mat";

    [MenuItem("Tools/Mirai01/釣りの障害物の検証シーンを作る")]
    public static void CreateScene()
    {
        FishingSceneBuilder.EnsureFolders();
        InputActionAsset inputActions = FishingSceneBuilder.LoadInputActions();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        FishingSceneBuilder.CreateLight();
        FishingSceneBuilder.CreateGround();

        // ---- 点数と画面表示 ----
        GameObject hudObject = new GameObject("FishingHUD");
        ScoreBoard scoreBoard = hudObject.AddComponent<ScoreBoard>();
        FishingStatusUI statusUI = hudObject.AddComponent<FishingStatusUI>();

        FishingSceneBuilder.CreatePocketWalls(scoreBoard);

        GameObject player = FishingSceneBuilder.CreatePlayer();
        Camera camera = FishingSceneBuilder.CreateCamera(player);
        HookProjectile hook = FishingSceneBuilder.CreateHook();
        HookLine line = FishingSceneBuilder.CreateLine(player);

        FishingSceneBuilder.CreateSupplyRing(8, 8f);

        // ---- 動く障害物 ----
        Material material = FishingSceneBuilder.GetOrCreateMaterial(
            ObstacleMaterialPath, new Color(0.55f, 0.3f, 0.75f));

        // 北側を東西に行き来する（横長）
        CreateObstacle("Obstacle_NorthSlide", new Vector3(0f, 0f, 12f), 0f,
            new Vector3(6f, 1.2f, 1f), material,
            new[] { new Vector3(-11f, 0f, 0f), new Vector3(11f, 0f, 0f) },
            MovingObstacle.PathMode.PingPong, 4f, 0.4f, false);

        // 南側を四角くぐるぐる回る（横長。角で少し止まる）
        CreateObstacle("Obstacle_SouthLoop", new Vector3(0f, 0f, -12f), 0f,
            new Vector3(5f, 1.2f, 1f), material,
            new[]
            {
                new Vector3(-10f, 0f, 2f),
                new Vector3(10f, 0f, 2f),
                new Vector3(10f, 0f, -3f),
                new Vector3(-10f, 0f, -3f)
            },
            MovingObstacle.PathMode.Loop, 5f, 0.6f, false);

        // 西側を南北に行き来する（縦長。ゆっくり止まってゆっくり動き出す）
        CreateObstacle("Obstacle_WestSlide", new Vector3(-14f, 0f, 0f), 90f,
            new Vector3(6f, 1.2f, 1f), material,
            new[] { new Vector3(0f, 0f, -7f), new Vector3(0f, 0f, 7f) },
            MovingObstacle.PathMode.PingPong, 3f, 1f, true);

        HookChargeUI ui = FishingSceneBuilder.CreateUI();

        FishingSceneBuilder.WirePlayer(player, camera, hook, line, ui, inputActions, statusUI, scoreBoard);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "【障害物】釣りの障害物の検証シーンを作りました。\n" +
            "シーン: " + ScenePath + "\n" +
            "・紫の長い箱が障害物。決まった道すじを動き続ける\n" +
            "・プレイヤーは通り抜けられず、動いてきた障害物に押し出される\n" +
            "・物資も押しのけられる\n" +
            "・道すじは障害物を選ぶとシーン画面に橙の線で出る。Inspector の Points で変えられる");
    }

    /// <summary>
    /// 動く障害物を1つ置く。
    /// <paramref name="points"/> は、置いた位置 <paramref name="position"/> からのずれ。
    /// **マップの雛形を作るツール（FishingMapSetup）からも使う。**
    /// </summary>
    public static GameObject CreateObstacle(string name, Vector3 position, float yaw, Vector3 size,
        Material material, Vector3[] points, MovingObstacle.PathMode mode,
        float speed, float waitSeconds, bool easeInOut)
    {
        // 足元が床（高さ0）に来るように、高さの半分だけ持ち上げる
        Vector3 center = position + new Vector3(0f, size.y * 0.5f, 0f);

        GameObject obstacle = FishingSceneBuilder.CreateBox(name, center, size, material);
        obstacle.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        Rigidbody body = obstacle.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        MovingObstacle mover = obstacle.AddComponent<MovingObstacle>();

        SerializedObject serialized = new SerializedObject(mover);

        SerializedProperty pointsProperty = serialized.FindProperty("points");
        pointsProperty.arraySize = points.Length;
        for (int i = 0; i < points.Length; i++)
        {
            pointsProperty.GetArrayElementAtIndex(i).vector3Value = points[i];
        }

        serialized.FindProperty("mode").enumValueIndex = (int)mode;
        serialized.FindProperty("moveSpeed").floatValue = speed;
        serialized.FindProperty("waitSeconds").floatValue = waitSeconds;
        serialized.FindProperty("easeInOut").boolValue = easeInOut;

        serialized.ApplyModifiedPropertiesWithoutUndo();

        return obstacle;
    }
}

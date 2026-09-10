using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **レースの検証シーンを作り直すツール。**
///
/// Unityのメニューから実行できる。
///
/// | メニュー | 作られるもの |
/// | --- | --- |
/// | `Tools > Mirai01 > レースの検証シーンを作り直す` | **カートのレース**（`RaceTest.unity`） |
/// | `Tools > Mirai01 > 走るレースの検証シーンを作り直す` | **ロボットが走るレース**（`RunnerRaceTest.unity`） |
///
/// **コースは計算で並べている。**
/// 手で置くと、道と壁とチェックポイントがずれて
/// 「通ったのに数えない」という直しにくい不具合になるため。
///
/// 2つのシーンは**同じ作り方を共有**していて、違うのは
/// **コースの大きさ**と**走る人（カートかロボットか）**だけ。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class RaceCourseSetup
{
    private const string PrefabFolder = "Assets/Prefabs";
    private const string SceneFolder = "Assets/Scenes/Test";
    private const string MaterialFolder = "Assets/Art/Materials";

    private const string KartPrefabPath = PrefabFolder + "/RaceKart.prefab";
    private const string RobotPrefabPath = PrefabFolder + "/RobotRig.prefab";
    private const string KartScenePath = SceneFolder + "/RaceTest.unity";
    private const string RunnerScenePath = SceneFolder + "/RunnerRaceTest.unity";
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

    /// <summary>コースの大きさ。走る速さに合わせて変える。</summary>
    private struct CourseSize
    {
        /// <summary>直線の長さ（メートル）</summary>
        public float Straight;

        /// <summary>カーブの半径（メートル）</summary>
        public float Radius;

        /// <summary>道の幅（メートル）</summary>
        public float Width;

        /// <summary>道を何個の板に分けて並べるか</summary>
        public int Segments;

        /// <summary>通過点の数（0番がスタート／ゴール）</summary>
        public int Checkpoints;

        /// <summary>1周の長さ（メートル）</summary>
        public float Perimeter => Straight * 2f + Mathf.PI * Radius * 2f;
    }

    /// <summary>カート用。速いので大きく作る（1周およそ354m）。</summary>
    private static CourseSize KartCourse => new CourseSize
    {
        Straight = 70f, Radius = 34f, Width = 16f, Segments = 96, Checkpoints = 6,
    };

    /// <summary>走る人用。**歩く速さに合わせて小さくしてある**（1周およそ148m）。</summary>
    private static CourseSize RunnerCourse => new CourseSize
    {
        Straight = 30f, Radius = 14f, Width = 10f, Segments = 64, Checkpoints = 6,
    };

    // ------------------------------------------------------------
    // メニュー
    // ------------------------------------------------------------

    [MenuItem("Tools/Mirai01/レースの検証シーンを作り直す")]
    public static void CreateAll()
    {
        PrepareFolders();

        Material body = CreateMaterial(MaterialFolder + "/RaceKartBody.mat", new Color(0.85f, 0.25f, 0.25f));
        Material wheel = CreateMaterial(MaterialFolder + "/RaceKartWheel.mat", new Color(0.12f, 0.12f, 0.14f));

        GameObject kartPrefab = CreateKartPrefab(body, wheel);

        BuildScene(KartScenePath, KartCourse, kartPrefab, false, false,
            "W / S  進む・戻る　　A / D  ハンドル　　R  やり直し");

        Finish(KartScenePath);
    }

    [MenuItem("Tools/Mirai01/走るレースの検証シーンを作り直す")]
    public static void CreateRunnerRace()
    {
        PrepareFolders();

        GameObject robotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath);

        if (robotPrefab == null)
        {
            Debug.LogError($"{RobotPrefabPath} が見つかりません。先にロボットのプレハブを用意してください。");
            return;
        }

        // **月面運動会。** 重力が時間で変わるのがこのシーンの目玉
        BuildScene(RunnerScenePath, RunnerCourse, robotPrefab, true, true,
            "W / A / S / D  走る　　Space  ジャンプ　　V  視点　　R  やり直し");

        Finish(RunnerScenePath);
    }

    private static void PrepareFolders()
    {
        EnsureFolder(PrefabFolder);
        EnsureFolder("Assets/Scenes");
        EnsureFolder(SceneFolder);
        EnsureFolder("Assets/Art");
        EnsureFolder(MaterialFolder);
    }

    private static void Finish(string scenePath)
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"レースの検証シーンを作りました：{scenePath}");
    }

    // ------------------------------------------------------------
    // カートのプレハブ
    // ------------------------------------------------------------

    private static GameObject CreateKartPrefab(Material bodyMaterial, Material wheelMaterial)
    {
        GameObject root = new GameObject("RaceKart");

        CharacterController controller = root.AddComponent<CharacterController>();
        controller.radius = 0.8f;
        controller.height = 1.6f;
        controller.center = new Vector3(0f, 0.8f, 0f);
        controller.stepOffset = 0.3f;
        controller.slopeLimit = 50f;
        controller.skinWidth = 0.05f;

        RaceRacer racer = root.AddComponent<RaceRacer>();
        KartController kart = root.AddComponent<KartController>();

        var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);

        SerializedObject serialized = new SerializedObject(kart);
        serialized.FindProperty("racer").objectReferenceValue = racer;

        if (inputActions != null)
        {
            serialized.FindProperty("inputActions").objectReferenceValue = inputActions;
        }
        else
        {
            Debug.LogWarning($"{InputActionsPath} が見つかりません。入力の設定は手で入れてください。");
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();

        // ----- 見た目 -----
        CreatePart(root.transform, PrimitiveType.Cube, "Body",
            new Vector3(0f, 0.55f, 0f), new Vector3(1.6f, 0.5f, 2.6f), bodyMaterial);

        // 前が分かるように、鼻先を1つ付ける
        CreatePart(root.transform, PrimitiveType.Cube, "Nose",
            new Vector3(0f, 0.75f, 1.0f), new Vector3(0.9f, 0.4f, 0.6f), bodyMaterial);

        CreateWheel(root.transform, "WheelFrontLeft", new Vector3(-0.85f, 0.35f, 0.9f), wheelMaterial);
        CreateWheel(root.transform, "WheelFrontRight", new Vector3(0.85f, 0.35f, 0.9f), wheelMaterial);
        CreateWheel(root.transform, "WheelRearLeft", new Vector3(-0.85f, 0.35f, -0.9f), wheelMaterial);
        CreateWheel(root.transform, "WheelRearRight", new Vector3(0.85f, 0.35f, -0.9f), wheelMaterial);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, KartPrefabPath);
        Object.DestroyImmediate(root);

        return saved;
    }

    private static void CreateWheel(Transform parent, string name, Vector3 position, Material material)
    {
        GameObject wheel = CreatePart(parent, PrimitiveType.Cylinder, name,
            position, new Vector3(0.7f, 0.12f, 0.7f), material);

        // 円柱は縦向きなので、横に倒してタイヤにする
        wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
    }

    private static GameObject CreatePart(Transform parent, PrimitiveType type, string name,
        Vector3 position, Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<MeshRenderer>().sharedMaterial = material;

        // 見た目だけなので、当たり判定は要らない（本体の CharacterController が受け持つ）
        Object.DestroyImmediate(part.GetComponent<Collider>());

        return part;
    }

    // ------------------------------------------------------------
    // シーン
    // ------------------------------------------------------------

    /// <summary>
    /// レースのシーンを1つ作る。
    /// </summary>
    /// <param name="scenePath">保存先</param>
    /// <param name="size">コースの大きさ</param>
    /// <param name="racerPrefab">走らせるもの（カート、またはロボット）</param>
    /// <param name="prefabHasCamera">**そのプレハブがカメラを内蔵しているか。**
    /// 内蔵しているならシーンにカメラを置かない（2つあると映らなくなる）</param>
    /// <param name="withGravityShifter">**重力が時間で変わる係を置くか**（月面運動会）</param>
    /// <param name="hint">画面の左下に出す操作の案内</param>
    private static void BuildScene(string scenePath, CourseSize size, GameObject racerPrefab,
        bool prefabHasCamera, bool withGravityShifter, string hint)
    {
        Material road = CreateMaterial(MaterialFolder + "/RaceRoad.mat", new Color(0.24f, 0.25f, 0.28f));
        Material wall = CreateMaterial(MaterialFolder + "/RaceWall.mat", new Color(0.85f, 0.86f, 0.88f));
        Material ground = CreateMaterial(MaterialFolder + "/RaceGround.mat", new Color(0.30f, 0.45f, 0.25f));
        Material line = CreateMaterial(MaterialFolder + "/RaceStartLine.mat", new Color(0.95f, 0.95f, 0.95f));

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject groundObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
        groundObject.name = "Ground";
        groundObject.transform.position = new Vector3(0f, -0.2f, 0f);
        groundObject.transform.localScale = new Vector3(30f, 1f, 30f);
        groundObject.GetComponent<MeshRenderer>().sharedMaterial = ground;

        GameObject course = new GameObject("Course");
        GameObject roadRoot = new GameObject("Road");
        GameObject wallRoot = new GameObject("Walls");
        GameObject checkpointRoot = new GameObject("Checkpoints");
        GameObject gridRoot = new GameObject("StartGrid");

        roadRoot.transform.SetParent(course.transform, false);
        wallRoot.transform.SetParent(course.transform, false);
        checkpointRoot.transform.SetParent(course.transform, false);
        gridRoot.transform.SetParent(course.transform, false);

        BuildRoad(size, roadRoot.transform, wallRoot.transform, road, wall);

        List<RaceCheckpoint> checkpoints = BuildCheckpoints(size, checkpointRoot.transform);
        BuildStartLineMark(size, course.transform, line);
        List<Transform> grid = BuildStartGrid(size, gridRoot.transform);

        // ----- 進行役 -----
        GameObject managerObject = new GameObject("RaceManager");
        RaceManager manager = managerObject.AddComponent<RaceManager>();

        SerializedObject serialized = new SerializedObject(manager);
        SetObjectList(serialized.FindProperty("checkpoints"), checkpoints.ConvertAll(point => (Object)point));
        SetObjectList(serialized.FindProperty("startGrid"), grid.ConvertAll(slot => (Object)slot));
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // ----- 重力を変える係 -----
        if (withGravityShifter)
        {
            GameObject gravityObject = new GameObject("GravityShifter");
            gravityObject.AddComponent<GravityShifter>();
        }

        GameObject hudObject = new GameObject("RaceHud");
        RaceHud hud = hudObject.AddComponent<RaceHud>();

        SerializedObject hudSerialized = new SerializedObject(hud);
        hudSerialized.FindProperty("hintText").stringValue = hint;
        hudSerialized.ApplyModifiedPropertiesWithoutUndo();

        // ----- 走る人 -----
        GameObject placed = null;

        if (racerPrefab != null && grid.Count > 0)
        {
            placed = (GameObject)PrefabUtility.InstantiatePrefab(racerPrefab);
            placed.transform.SetPositionAndRotation(grid[0].position, grid[0].rotation);

            PrepareRacer(placed);
        }

        // ----- カメラ -----
        // プレハブがカメラを持っているときは置かない（2つあると何も映らない）
        if (!prefabHasCamera)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.farClipPlane = 500f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<RaceCamera>();

            if (placed != null)
            {
                cameraObject.transform.position = placed.transform.position
                    - placed.transform.forward * 8f + Vector3.up * 3.5f;
                cameraObject.transform.rotation = Quaternion.LookRotation(
                    placed.transform.position + Vector3.up - cameraObject.transform.position);
            }
        }

        EditorSceneManager.SaveScene(scene, scenePath);
    }

    /// <summary>
    /// 置いたものをレースに出られる状態にする。
    ///
    /// **カートには最初から付いている。**
    /// ロボットには付いていないので、**シーンに置いたものにだけ足す**
    /// （`RobotRig.prefab` は変えない。他のシーンに影響を出さないため）。
    /// </summary>
    private static void PrepareRacer(GameObject placed)
    {
        RaceRacer racer = placed.GetComponent<RaceRacer>();

        if (racer == null)
        {
            racer = placed.AddComponent<RaceRacer>();

            SerializedObject serialized = new SerializedObject(racer);
            serialized.FindProperty("racerName").stringValue = "ロボット";
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        RobotController robot = placed.GetComponent<RobotController>();

        if (robot != null && placed.GetComponent<RobotRacerLink>() == null)
        {
            RobotRacerLink link = placed.AddComponent<RobotRacerLink>();

            SerializedObject serialized = new SerializedObject(link);
            serialized.FindProperty("robot").objectReferenceValue = robot;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void SetObjectList(SerializedProperty property, List<Object> values)
    {
        property.arraySize = values.Count;

        for (int i = 0; i < values.Count; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }

    // ------------------------------------------------------------
    // コースの形
    // ------------------------------------------------------------

    /// <summary>
    /// **1周のうち <paramref name="distance"/> メートル進んだ地点**の、位置と進む向きを求める。
    ///
    /// 形は「陸上のトラック」と同じ。直線2本と、半円2つを繋いだもの。
    /// </summary>
    private static void SamplePath(CourseSize size, float distance,
        out Vector3 position, out Vector3 forward)
    {
        float s = Mathf.Repeat(distance, size.Perimeter);

        float arc = Mathf.PI * size.Radius;
        float half = size.Straight * 0.5f;

        if (s < size.Straight)
        {
            // 右側の直線（+Z へ進む）
            position = new Vector3(size.Radius, 0f, -half + s);
            forward = Vector3.forward;
            return;
        }

        s -= size.Straight;

        if (s < arc)
        {
            // 奥のカーブ
            float angle = s / size.Radius;
            position = new Vector3(
                size.Radius * Mathf.Cos(angle), 0f, half + size.Radius * Mathf.Sin(angle));
            forward = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            return;
        }

        s -= arc;

        if (s < size.Straight)
        {
            // 左側の直線（-Z へ進む）
            position = new Vector3(-size.Radius, 0f, half - s);
            forward = Vector3.back;
            return;
        }

        s -= size.Straight;

        // 手前のカーブ
        float back = Mathf.PI + s / size.Radius;
        position = new Vector3(
            size.Radius * Mathf.Cos(back), 0f, -half + size.Radius * Mathf.Sin(back));
        forward = new Vector3(-Mathf.Sin(back), 0f, Mathf.Cos(back));
    }

    private static void BuildRoad(CourseSize size, Transform roadRoot, Transform wallRoot,
        Material roadMaterial, Material wallMaterial)
    {
        float step = size.Perimeter / size.Segments;

        for (int i = 0; i < size.Segments; i++)
        {
            SamplePath(size, i * step, out Vector3 from, out _);
            SamplePath(size, (i + 1) * step, out Vector3 to, out _);

            Vector3 middle = (from + to) * 0.5f;
            Vector3 direction = to - from;
            float length = direction.magnitude;

            if (length < 0.001f)
            {
                continue;
            }

            direction /= length;

            Quaternion rotation = Quaternion.LookRotation(direction);
            Vector3 right = Vector3.Cross(Vector3.up, direction);

            // 道。上の面がちょうど高さ0になるようにする
            GameObject road = GameObject.CreatePrimitive(PrimitiveType.Cube);
            road.name = $"Road{i:000}";
            road.transform.SetParent(roadRoot, false);
            road.transform.SetPositionAndRotation(middle + Vector3.down * 0.25f, rotation);
            road.transform.localScale = new Vector3(size.Width, 0.5f, length * 1.08f);
            road.GetComponent<MeshRenderer>().sharedMaterial = roadMaterial;

            // 壁。左右に1枚ずつ
            CreateWall(wallRoot, $"WallOuter{i:000}",
                middle + right * (size.Width * 0.5f + 0.5f), rotation, length, wallMaterial);

            CreateWall(wallRoot, $"WallInner{i:000}",
                middle - right * (size.Width * 0.5f + 0.5f), rotation, length, wallMaterial);
        }
    }

    private static void CreateWall(Transform parent, string name, Vector3 position,
        Quaternion rotation, float length, Material material)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(parent, false);
        wall.transform.SetPositionAndRotation(position + Vector3.up * 1.25f, rotation);
        wall.transform.localScale = new Vector3(1f, 2.5f, length * 1.08f);
        wall.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static List<RaceCheckpoint> BuildCheckpoints(CourseSize size, Transform parent)
    {
        List<RaceCheckpoint> checkpoints = new List<RaceCheckpoint>();

        for (int i = 0; i < size.Checkpoints; i++)
        {
            SamplePath(size, size.Perimeter * i / size.Checkpoints,
                out Vector3 position, out Vector3 forward);

            GameObject point = new GameObject(i == 0 ? "Checkpoint00_StartLine" : $"Checkpoint{i:00}");
            point.transform.SetParent(parent, false);
            point.transform.SetPositionAndRotation(
                position + Vector3.up * 3f, Quaternion.LookRotation(forward));

            RaceCheckpoint checkpoint = point.AddComponent<RaceCheckpoint>();
            checkpoint.SetOrder(i);
            checkpoint.SetSize(size.Width + 4f, 10f);

            checkpoints.Add(checkpoint);
        }

        return checkpoints;
    }

    /// <summary>スタート／ゴールの線を、地面に白く描く（見た目だけ）。</summary>
    private static void BuildStartLineMark(CourseSize size, Transform parent, Material material)
    {
        SamplePath(size, 0f, out Vector3 position, out Vector3 forward);

        GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mark.name = "StartLineMark";
        mark.transform.SetParent(parent, false);
        mark.transform.SetPositionAndRotation(
            position + Vector3.up * 0.03f, Quaternion.LookRotation(forward));
        mark.transform.localScale = new Vector3(size.Width, 0.06f, 1.4f);
        mark.GetComponent<MeshRenderer>().sharedMaterial = material;

        // 乗り上げないように、当たり判定は外す
        Object.DestroyImmediate(mark.GetComponent<Collider>());
    }

    /// <summary>
    /// スタート位置を4つ作る。**線の手前**に置く。
    /// いまは1つしか置かないが、通信で人数が増えたときにそのまま使える。
    /// </summary>
    private static List<Transform> BuildStartGrid(CourseSize size, Transform parent)
    {
        List<Transform> grid = new List<Transform>();

        // 道幅に合わせて、横のずれと前後の間隔を決める
        float side = size.Width * 0.22f;
        float[] backOffsets = { size.Width * 0.3f, size.Width * 0.3f, size.Width * 0.7f, size.Width * 0.7f };
        float[] sideOffsets = { -side, side, -side, side };

        for (int i = 0; i < backOffsets.Length; i++)
        {
            // **コースに沿って後ろへ下がる。**
            // まっすぐ下がると、カーブの上では道からはみ出してしまう
            SamplePath(size, size.Perimeter - backOffsets[i], out Vector3 position, out Vector3 forward);
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            GameObject slot = new GameObject($"StartSlot{i + 1}");
            slot.transform.SetParent(parent, false);
            slot.transform.SetPositionAndRotation(
                position + right * sideOffsets[i] + Vector3.up * 0.2f,
                Quaternion.LookRotation(forward));

            grid.Add(slot.transform);
        }

        return grid;
    }

    // ------------------------------------------------------------
    // 道具
    // ------------------------------------------------------------

    private static Material CreateMaterial(string path, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null)
        {
            existing.color = color;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader) { color = color };
        AssetDatabase.CreateAsset(material, path);

        return material;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        string name = path.Substring(slash + 1);

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}

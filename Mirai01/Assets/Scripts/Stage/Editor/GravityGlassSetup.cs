using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **重い重力で割れるガラス床**を置く・試すためのツール。
///
/// | メニュー | すること |
/// | --- | --- |
/// | `Tools > Mirai01 > 重力で割れるガラスの床を置く` | **いま開いているシーン**に1枚置く |
/// | `Tools > Mirai01 > 割れるガラス床の検証シーンを作り直す` | 試すための専用シーンを作る |
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class GravityGlassSetup
{
    private const string MaterialFolder = "Assets/Art/Materials";
    private const string GlassMaterialPath = MaterialFolder + "/Glass.mat";
    private const string CrackMaterialPath = MaterialFolder + "/GlassCrack.mat";
    private const string GroundMaterialPath = MaterialFolder + "/TestGround.mat";

    private const string PrefabPath = "Assets/Prefabs/GravityRace/GravityGlassFloor.prefab";
    private const string RobotPrefabPath = "Assets/Prefabs/Robot/RobotRig.prefab";
    private const string ScenePath = "Assets/Scenes/Test/GravityGlassTest.unity";

    /// <summary>床1枚の大きさ。**人が乗れる広さと、ガラスと分かる厚み**にしてある。</summary>
    private static readonly Vector3 FloorSize = new Vector3(3f, 0.3f, 3f);

    // ------------------------------------------------------------
    // メニュー
    // ------------------------------------------------------------

    [MenuItem("Tools/Mirai01/重力で割れるガラスの床を置く")]
    public static void PlaceFloor()
    {
        GameObject prefab = GetOrCreatePrefab();

        if (prefab == null)
        {
            return;
        }

        GameObject placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        placed.transform.position = FindGroundPosition() + Vector3.up * (FloorSize.y * 0.5f);

        Undo.RegisterCreatedObjectUndo(placed, "割れるガラスの床を置く");
        Selection.activeGameObject = placed;
        EditorSceneManager.MarkSceneDirty(placed.scene);

        Debug.Log("割れるガラスの床を置きました。**重力が Break Gravity 以上のときに踏むと、ヒビが入って割れます。**\n" +
                  "重力を変えるには、シーンに GravityShifter が必要です。", placed);
    }

    [MenuItem("Tools/Mirai01/割れるガラス床の検証シーンを作り直す")]
    public static void CreateTestScene()
    {
        GameObject floorPrefab = GetOrCreatePrefab();
        GameObject robotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath);

        if (floorPrefab == null)
        {
            return;
        }

        Material ground = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // ----- 下の地面（落ちてもここで止まる） -----
        GameObject groundObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
        groundObject.name = "Ground";
        groundObject.transform.localScale = new Vector3(6f, 1f, 6f);
        SetMaterial(groundObject, ground);

        // ----- 高さ3mの足場：スタート → ガラスの橋 → ゴール -----
        const float deckTop = 3f;

        CreateBlock("StartDeck", new Vector3(0f, deckTop - 0.5f, 0f), new Vector3(8f, 1f, 8f), ground);
        CreateBlock("GoalDeck", new Vector3(0f, deckTop - 0.5f, 24f), new Vector3(8f, 1f, 8f), ground);

        // 落ちたあと登って戻れる坂（スタートの横）
        const float rampLength = 11f;
        float rampAngle = Mathf.Asin(deckTop / rampLength) * Mathf.Rad2Deg;
        GameObject ramp = CreateBlock("Ramp",
            new Vector3(-4f - rampLength * 0.5f * Mathf.Cos(rampAngle * Mathf.Deg2Rad), deckTop * 0.5f - 0.2f, 0f),
            new Vector3(rampLength, 0.4f, 3f), ground);
        ramp.transform.rotation = Quaternion.Euler(0f, 0f, rampAngle);

        // ガラスの床を5枚、橋のように並べる（隙間は小さく、歩いて渡れる）
        GameObject bridge = new GameObject("GlassBridge");

        for (int i = 0; i < 5; i++)
        {
            GameObject floor = (GameObject)PrefabUtility.InstantiatePrefab(floorPrefab);
            floor.name = $"GravityGlassFloor{i + 1}";
            floor.transform.SetParent(bridge.transform, false);
            floor.transform.position = new Vector3(0f, deckTop - FloorSize.y * 0.5f, 5.6f + i * 3.2f);
        }

        // ----- 重力を変える係（試しやすいよう、短い間隔で軽い⇔重いを行き来する） -----
        GameObject gravityObject = new GameObject("GravityShifter");
        GravityShifter shifter = gravityObject.AddComponent<GravityShifter>();

        SerializedObject gravity = new SerializedObject(shifter);
        SetFloatList(gravity.FindProperty("gravityChoices"), new List<float> { 3.7f, 14f });
        gravity.FindProperty("shortestInterval").floatValue = 5f;
        gravity.FindProperty("longestInterval").floatValue = 7f;
        gravity.FindProperty("warningSeconds").floatValue = 1.5f;
        gravity.FindProperty("changeSeconds").floatValue = 1f;
        gravity.ApplyModifiedPropertiesWithoutUndo();

        // ----- 画面表示（重力が出る） -----
        GameObject hudObject = new GameObject("RaceHud");
        RaceHud hud = hudObject.AddComponent<RaceHud>();

        SerializedObject hudSerialized = new SerializedObject(hud);
        hudSerialized.FindProperty("hintText").stringValue =
            "重力が 12 以上のときにガラスを踏むと、ヒビが入って割れる";
        hudSerialized.ApplyModifiedPropertiesWithoutUndo();

        // ----- ロボット（カメラ付き） -----
        if (robotPrefab != null)
        {
            GameObject robot = (GameObject)PrefabUtility.InstantiatePrefab(robotPrefab);
            robot.transform.position = new Vector3(0f, deckTop + 0.1f, -1.5f);
        }
        else
        {
            Debug.LogWarning($"{RobotPrefabPath} が見つかりません。操作するキャラクターを手で置いてください。");
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"割れるガラス床の検証シーンを作りました：{ScenePath}");
    }

    // ------------------------------------------------------------
    // プレハブ・マテリアル
    // ------------------------------------------------------------

    /// <summary>
    /// 床のプレハブを用意する。**すでにあれば、そのまま使う**（調整した値を消さないため）。
    /// </summary>
    private static GameObject GetOrCreatePrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (existing != null)
        {
            return existing;
        }

        Material glass = AssetDatabase.LoadAssetAtPath<Material>(GlassMaterialPath);

        if (glass == null)
        {
            Debug.LogError($"{GlassMaterialPath} が見つかりません。先に `ガラスの板を置く` でガラスのマテリアルを作ってください。");
            return null;
        }

        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = "GravityGlassFloor";
        root.transform.localScale = FloorSize;
        SetMaterial(root, glass);

        // ガラスなので、影は落とさない（透けているのに黒い影が出ると不自然）
        root.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        GravityGlassFloor floor = root.AddComponent<GravityGlassFloor>();

        SerializedObject serialized = new SerializedObject(floor);
        serialized.FindProperty("crackMaterial").objectReferenceValue = GetOrCreateCrackMaterial();
        serialized.ApplyModifiedPropertiesWithoutUndo();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        return saved;
    }

    /// <summary>ヒビの線のマテリアル。**光の当たり方に左右されない白**にしてある（どこから見ても見える）。</summary>
    private static Material GetOrCreateCrackMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(CrackMaterialPath);

        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            Debug.LogWarning("URP の Unlit シェーダーが見つかりません。ヒビのマテリアルは作りませんでした。");
            return null;
        }

        Material material = new Material(shader);
        material.SetColor("_BaseColor", new Color(0.95f, 0.97f, 1f, 1f));

        AssetDatabase.CreateAsset(material, CrackMaterialPath);

        return material;
    }

    // ------------------------------------------------------------
    // 道具
    // ------------------------------------------------------------

    private static GameObject CreateBlock(string name, Vector3 position, Vector3 size, Material material)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.position = position;
        block.transform.localScale = size;
        SetMaterial(block, material);

        return block;
    }

    private static void SetMaterial(GameObject target, Material material)
    {
        if (material != null)
        {
            target.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }

    private static void SetFloatList(SerializedProperty property, List<float> values)
    {
        property.arraySize = values.Count;

        for (int i = 0; i < values.Count; i++)
        {
            property.GetArrayElementAtIndex(i).floatValue = values[i];
        }
    }

    /// <summary>シーンビューが見ている場所の床を探す。見つからなければ、見ている場所をそのまま使う。</summary>
    private static Vector3 FindGroundPosition()
    {
        Vector3 pivot = SceneView.lastActiveSceneView != null
            ? SceneView.lastActiveSceneView.pivot
            : Vector3.zero;

        if (Physics.Raycast(pivot + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f))
        {
            return hit.point;
        }

        return pivot;
    }
}

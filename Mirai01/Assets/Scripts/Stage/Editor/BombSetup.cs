using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **爆弾**を置く・試すためのツール。
///
/// | メニュー | すること |
/// | --- | --- |
/// | `Tools > Mirai01 > 爆弾を置く` | **いま開いているシーン**に、宙に浮いた爆弾を1つ置く |
/// | `Tools > Mirai01 > 爆弾の検証シーンを作り直す` | 試すための専用シーンを作る |
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class BombSetup
{
    private const string MaterialFolder = "Assets/Art/Materials";
    private const string BodyMaterialPath = MaterialFolder + "/BombBody.mat";
    private const string FuseMaterialPath = MaterialFolder + "/BombFuse.mat";
    private const string BlastMaterialPath = MaterialFolder + "/BombBlast.mat";
    private const string GroundMaterialPath = MaterialFolder + "/TestGround.mat";
    private const string LineMaterialPath = MaterialFolder + "/RaceStartLine.mat";

    private const string PrefabPath = "Assets/Prefabs/GravityRace/Bomb.prefab";
    private const string RobotPrefabPath = "Assets/Prefabs/Robot/RobotRig.prefab";
    private const string ScenePath = "Assets/Scenes/Test/BombTest.unity";

    /// <summary>爆弾の見た目の直径（メートル）。</summary>
    private const float BodyDiameter = 0.8f;

    // ------------------------------------------------------------
    // メニュー
    // ------------------------------------------------------------

    [MenuItem("Tools/Mirai01/爆弾を置く")]
    public static void PlaceBomb()
    {
        GameObject prefab = GetOrCreatePrefab();

        GameObject placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

        // 宙に浮いていると分かるよう、床から少し上に置く
        placed.transform.position = FindGroundPosition() + Vector3.up * 1.2f;

        Undo.RegisterCreatedObjectUndo(placed, "爆弾を置く");
        Selection.activeGameObject = placed;
        EditorSceneManager.MarkSceneDirty(placed.scene);

        Debug.Log("爆弾を置きました。**空中に動かしても落ちません。** 触れると爆発して、触れた人を吹っ飛ばします。\n" +
                  "重力が軽いほど遠くへ飛びます（Gravity Influence で効き方を変えられます）。", placed);
    }

    [MenuItem("Tools/Mirai01/爆弾の検証シーンを作り直す")]
    public static void CreateTestScene()
    {
        GameObject bombPrefab = GetOrCreatePrefab();
        GameObject robotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath);
        Material ground = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
        Material line = AssetDatabase.LoadAssetAtPath<Material>(LineMaterialPath);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject groundObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
        groundObject.name = "Ground";
        groundObject.transform.localScale = new Vector3(10f, 1f, 10f);
        SetMaterial(groundObject, ground);

        // ----- 爆弾：地面すれすれ・腰の高さ・跳ばないと届かない高さ の3段 -----
        GameObject bombs = new GameObject("Bombs");
        float[] heights = { 0.6f, 1.3f, 2.2f };

        for (int row = 0; row < heights.Length; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                GameObject bomb = (GameObject)PrefabUtility.InstantiatePrefab(bombPrefab);
                bomb.name = $"Bomb_{heights[row]:0.0}m_{column + 1}";
                bomb.transform.SetParent(bombs.transform, false);
                bomb.transform.position = new Vector3(-4.5f + column * 3f, heights[row], 6f + row * 3f);

                // 試しやすいよう、このシーンだけ3秒で戻る
                SerializedObject serialized = new SerializedObject(bomb.GetComponent<Bomb>());
                serialized.FindProperty("respawnSeconds").floatValue = 3f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ----- 飛んだ距離が分かる目盛り（5mごとの白線。スタートの後ろ側） -----
        GameObject marks = new GameObject("DistanceMarks");

        for (int i = 0; i <= 6; i++)
        {
            GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mark.name = $"Mark_{i * 5}m";
            mark.transform.SetParent(marks.transform, false);
            mark.transform.position = new Vector3(0f, 0.01f, 6f - i * 5f);
            mark.transform.localScale = new Vector3(14f, 0.02f, 0.15f);
            SetMaterial(mark, line);

            Collider collider = mark.GetComponent<Collider>();
            Object.DestroyImmediate(collider);
        }

        // ----- 重力を変える係（月・地球・重い を行き来） -----
        GameObject gravityObject = new GameObject("GravityShifter");
        GravityShifter shifter = gravityObject.AddComponent<GravityShifter>();

        SerializedObject gravity = new SerializedObject(shifter);
        SetFloatList(gravity.FindProperty("gravityChoices"), new List<float> { 1.6f, 9.81f, 14f });
        gravity.FindProperty("shortestInterval").floatValue = 7f;
        gravity.FindProperty("longestInterval").floatValue = 9f;
        gravity.FindProperty("warningSeconds").floatValue = 1.5f;
        gravity.FindProperty("changeSeconds").floatValue = 1f;
        gravity.ApplyModifiedPropertiesWithoutUndo();

        // ----- 画面表示（重力が出る） -----
        GameObject hudObject = new GameObject("RaceHud");
        RaceHud hud = hudObject.AddComponent<RaceHud>();

        SerializedObject hudSerialized = new SerializedObject(hud);
        hudSerialized.FindProperty("hintText").stringValue =
            "爆弾に触ると吹っ飛ぶ。重力が軽いほど遠くへ飛ぶ（白線は5mごと）";
        hudSerialized.ApplyModifiedPropertiesWithoutUndo();

        // ----- ロボット -----
        if (robotPrefab != null)
        {
            GameObject robot = (GameObject)PrefabUtility.InstantiatePrefab(robotPrefab);
            robot.transform.position = new Vector3(0f, 0.1f, 0f);
        }
        else
        {
            Debug.LogWarning($"{RobotPrefabPath} が見つかりません。操作するキャラクターを手で置いてください。");
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"爆弾の検証シーンを作りました：{ScenePath}");
    }

    // ------------------------------------------------------------
    // プレハブ・マテリアル
    // ------------------------------------------------------------

    /// <summary>
    /// 爆弾のプレハブを用意する。**すでにあれば、そのまま使う**（調整した値を消さないため）。
    /// </summary>
    private static GameObject GetOrCreatePrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (existing != null)
        {
            return existing;
        }

        Material body = GetOrCreateMaterial(BodyMaterialPath, "Universal Render Pipeline/Lit",
            new Color(0.12f, 0.12f, 0.14f));
        Material fuse = GetOrCreateMaterial(FuseMaterialPath, "Universal Render Pipeline/Unlit",
            new Color(1f, 0.35f, 0.15f));
        Material blast = GetOrCreateMaterial(BlastMaterialPath, "Universal Render Pipeline/Unlit",
            new Color(1f, 0.62f, 0.2f));

        // 入れ物は空にして、見た目は子に持たせる（揺れる動きを入れ物で行うため）
        GameObject root = new GameObject("Bomb");

        CreatePart(root.transform, PrimitiveType.Sphere, "Body",
            Vector3.zero, Vector3.one * BodyDiameter, Quaternion.identity, body);

        // 導火線と、その先の火花
        CreatePart(root.transform, PrimitiveType.Cylinder, "Fuse",
            new Vector3(0f, BodyDiameter * 0.55f, 0f), new Vector3(0.08f, 0.12f, 0.08f),
            Quaternion.Euler(0f, 0f, 20f), body);

        CreatePart(root.transform, PrimitiveType.Sphere, "Spark",
            new Vector3(-0.05f, BodyDiameter * 0.7f, 0f), Vector3.one * 0.14f, Quaternion.identity, fuse);

        Bomb bomb = root.AddComponent<Bomb>();

        SerializedObject serialized = new SerializedObject(bomb);
        serialized.FindProperty("blastMaterial").objectReferenceValue = blast;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        return saved;
    }

    private static void CreatePart(Transform parent, PrimitiveType type, string name,
        Vector3 position, Vector3 scale, Quaternion rotation, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localRotation = rotation;
        part.transform.localScale = scale;
        SetMaterial(part, material);

        // **爆弾には当たり判定を付けない。** 付けると、人が押し戻されて触れる前に止まってしまう
        Object.DestroyImmediate(part.GetComponent<Collider>());
    }

    private static Material GetOrCreateMaterial(string path, string shaderName, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find(shaderName);

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        material.SetColor("_BaseColor", color);
        material.color = color;

        AssetDatabase.CreateAsset(material, path);

        return material;
    }

    // ------------------------------------------------------------
    // 道具
    // ------------------------------------------------------------

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

using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// LAN通信の検証用シーンと、通信用プレイヤーのプレハブを作り直すツール。
///
/// Unityのメニュー「Tools > Mirai01 > LAN通信の検証シーンを作り直す」から実行できる。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class LanPlayTestSetup
{
    private const string PrefabFolder = "Assets/Prefabs";
    private const string SceneFolder = "Assets/Scenes/Test";
    private const string MaterialFolder = "Assets/Art/Materials";

    private const string PrefabPath = PrefabFolder + "/NetworkPlayer.prefab";
    private const string ScenePath = SceneFolder + "/LanPlayTest.unity";
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

    [MenuItem("Tools/Mirai01/LAN通信の検証シーンを作り直す")]
    public static void CreateAll()
    {
        EnsureFolder("Assets/Art");
        EnsureFolder(MaterialFolder);
        EnsureFolder(PrefabFolder);
        EnsureFolder("Assets/Scenes");
        EnsureFolder(SceneFolder);

        Material bodyMaterial = CreateMaterial(MaterialFolder + "/NetworkPlayerBody.mat", Color.white);
        Material groundMaterial = CreateMaterial(MaterialFolder + "/TestGround.mat", new Color(0.72f, 0.72f, 0.72f));
        Material markerMaterial = CreateMaterial(MaterialFolder + "/NetworkMarker.mat", new Color(0.55f, 0.75f, 0.55f));

        GameObject prefab = CreatePlayerPrefab(bodyMaterial);
        CreateTestScene(prefab, groundMaterial, markerMaterial);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("LAN通信の検証シーンを作り直しました。\nシーン: " + ScenePath);
    }

    // ------------------------------------------------------------
    // プレイヤーのプレハブ
    // ------------------------------------------------------------

    private static GameObject CreatePlayerPrefab(Material bodyMaterial)
    {
        GameObject root = new GameObject("NetworkPlayer");

        CharacterController controller = root.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.4f;
        controller.center = new Vector3(0f, 0.9f, 0f);
        controller.slopeLimit = 50f;
        controller.stepOffset = 0.4f;

        // 通信で「同じ物」だと見分けるための印
        root.AddComponent<NetworkObject>();

        // 位置を他のPCへ送る部品。
        // Owner にすることで「動かしている本人が位置を送る」形になる
        NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
        networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        networkTransform.SyncScaleX = false;
        networkTransform.SyncScaleY = false;
        networkTransform.SyncScaleZ = false;

        // 見た目（当たり判定は CharacterController が持つので外す）
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        visual.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        MeshRenderer bodyRenderer = visual.GetComponent<MeshRenderer>();
        bodyRenderer.sharedMaterial = bodyMaterial;

        // 前を向いているのが分かる印
        GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mark.name = "FrontMark";
        mark.transform.SetParent(root.transform, false);
        mark.transform.localPosition = new Vector3(0f, 1.3f, 0.42f);
        mark.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
        Object.DestroyImmediate(mark.GetComponent<Collider>());
        mark.GetComponent<MeshRenderer>().sharedMaterial = bodyMaterial;

        NetworkPlayer player = root.AddComponent<NetworkPlayer>();

        var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        if (inputActions == null)
        {
            Debug.LogWarning(InputActionsPath + " が見つかりません。入力の設定は手で入れてください。");
        }

        SerializedObject serialized = new SerializedObject(player);
        serialized.FindProperty("inputActions").objectReferenceValue = inputActions;
        serialized.FindProperty("bodyRenderer").objectReferenceValue = bodyRenderer;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        return saved;
    }

    // ------------------------------------------------------------
    // 検証用シーン
    // ------------------------------------------------------------

    private static void CreateTestScene(GameObject prefab, Material groundMaterial, Material markerMaterial)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(3f, 1f, 3f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

        // 動いたことが分かるように、目印を四方に置く
        CreateBox("Marker_North", new Vector3(0f, 0.5f, 10f), markerMaterial);
        CreateBox("Marker_South", new Vector3(0f, 0.5f, -10f), markerMaterial);
        CreateBox("Marker_East", new Vector3(10f, 0.5f, 0f), markerMaterial);
        CreateBox("Marker_West", new Vector3(-10f, 0.5f, 0f), markerMaterial);

        // 段差（登れることの確認用）
        GameObject step = GameObject.CreatePrimitive(PrimitiveType.Cube);
        step.name = "Step";
        step.transform.position = new Vector3(5f, 0.25f, 5f);
        step.transform.localScale = new Vector3(4f, 0.5f, 4f);
        step.GetComponent<MeshRenderer>().sharedMaterial = markerMaterial;

        // カメラ
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 6f, -8f);
        cameraObject.transform.rotation = Quaternion.Euler(25f, 0f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.05f;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<LocalPlayerCamera>();

        // 通信のまとめ役
        GameObject managerObject = new GameObject("NetworkManager");
        NetworkManager manager = managerObject.AddComponent<NetworkManager>();
        UnityTransport transport = managerObject.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 7777);

        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.PlayerPrefab = prefab;

        managerObject.AddComponent<LanConnectionUi>();
        managerObject.AddComponent<LanAutoStart>();
        managerObject.AddComponent<LanConnectionLogger>();

        EditorUtility.SetDirty(manager);

        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    private static void CreateBox(string name, Vector3 position, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = position;
        box.transform.localScale = Vector3.one;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    // ------------------------------------------------------------
    // 補助
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

        int lastSlash = path.LastIndexOf('/');
        string parent = path.Substring(0, lastSlash);
        string folderName = path.Substring(lastSlash + 1);
        AssetDatabase.CreateFolder(parent, folderName);
    }
}

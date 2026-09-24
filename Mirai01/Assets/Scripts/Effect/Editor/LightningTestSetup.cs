using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// **電撃エフェクト（体のまわりのらせん・らせんをまとったビーム）**を試すためのツール。
///
/// | メニュー | すること |
/// | --- | --- |
/// | `Tools > Mirai01 > 電撃エフェクトの検証シーンを作り直す` | 試すための専用シーンを作る |
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class LightningTestSetup
{
    private const string MaterialFolder = "Assets/Art/Materials";
    private const string LightningMaterialPath = MaterialFolder + "/LightningAdditive.mat";
    private const string CharacterMaterialPath = MaterialFolder + "/LightningTestCharacter.mat";
    private const string GroundMaterialPath = MaterialFolder + "/LightningTestGround.mat";

    private const string VolumeProfilePath = "Assets/Settings/LightningTestVolume.asset";
    private const string ScenePath = "Assets/Scenes/Test/LightningTest.unity";

    [MenuItem("Tools/Mirai01/電撃エフェクトの検証シーンを作り直す")]
    public static void CreateTestScene()
    {
        Material lightningMaterial = GetOrCreateLightningMaterial();
        Material characterMaterial = GetOrCreateLitMaterial(CharacterMaterialPath, new Color(0.15f, 0.17f, 0.2f));
        Material ground = GetOrCreateLitMaterial(GroundMaterialPath, new Color(0.08f, 0.09f, 0.11f));
        VolumeProfile profile = GetOrCreateVolumeProfile();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ----- 暗めの環境（光が目立つように） -----
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.12f, 0.13f, 0.16f);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 0.4f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject groundObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
        groundObject.name = "Ground";
        groundObject.transform.localScale = new Vector3(4f, 1f, 4f);
        SetMaterial(groundObject, ground);

        // ----- カメラ（トップダウン気味の斜め上から） -----
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.03f, 0.04f, 0.06f);
        // ビーム（最大15m）が画面の左から右へ横切るのが見えるよう、少し引いた位置から
        camera.fieldOfView = 45f;
        cameraObject.transform.position = new Vector3(1.5f, 10f, -11f);
        cameraObject.transform.LookAt(new Vector3(1.5f, 0.5f, 0f));

        // ブルーム（光のにじみ）を効かせるため、カメラの後処理をオンにする
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;

        GameObject volumeObject = new GameObject("PostProcessVolume");
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;

        // ----- キャラクター（仮のカプセル）と電撃 -----
        // 画面の左に立ち、右（+X）を向く。ビームは右へ飛ぶ
        GameObject character = new GameObject("Character");
        character.transform.position = new Vector3(-6f, 0f, 0f);
        character.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(character.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        body.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
        SetMaterial(body, characterMaterial);

        GameObject lightningObject = new GameObject("HelixLightning");
        lightningObject.transform.SetParent(character.transform, false);

        GameObject glowLightObject = new GameObject("LightningGlow");
        glowLightObject.transform.SetParent(lightningObject.transform, false);
        glowLightObject.transform.localPosition = new Vector3(0f, 1f, 0f);
        Light glowLight = glowLightObject.AddComponent<Light>();
        glowLight.type = LightType.Point;
        glowLight.color = new Color(0.4f, 1f, 0.55f);
        glowLight.range = 4f;
        glowLight.shadows = LightShadows.None;

        HelixLightning lightning = lightningObject.AddComponent<HelixLightning>();

        SerializedObject serialized = new SerializedObject(lightning);
        serialized.FindProperty("lineMaterial").objectReferenceValue = lightningMaterial;
        serialized.FindProperty("flickerLight").objectReferenceValue = glowLight;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // ----- ビーム（胸の高さから前へ） -----
        GameObject beamObject = new GameObject("HelixBeam");
        beamObject.transform.SetParent(character.transform, false);
        beamObject.transform.localPosition = new Vector3(0f, 1.1f, 0.4f);

        GameObject headLightObject = new GameObject("BeamHeadLight");
        headLightObject.transform.SetParent(beamObject.transform, false);
        Light headLight = headLightObject.AddComponent<Light>();
        headLight.type = LightType.Point;
        headLight.color = new Color(0.4f, 1f, 0.55f);
        headLight.range = 5f;
        headLight.shadows = LightShadows.None;

        HelixBeam beam = beamObject.AddComponent<HelixBeam>();

        SerializedObject beamSerialized = new SerializedObject(beam);
        beamSerialized.FindProperty("lineMaterial").objectReferenceValue = lightningMaterial;
        beamSerialized.FindProperty("headLight").objectReferenceValue = headLight;
        beamSerialized.ApplyModifiedPropertiesWithoutUndo();

        // ----- 操作 -----
        GameObject driverObject = new GameObject("LightningTestDriver");
        LightningTestDriver driver = driverObject.AddComponent<LightningTestDriver>();

        SerializedObject driverSerialized = new SerializedObject(driver);
        driverSerialized.FindProperty("lightning").objectReferenceValue = lightning;
        driverSerialized.FindProperty("beam").objectReferenceValue = beam;
        driverSerialized.FindProperty("character").objectReferenceValue = character.transform;
        driverSerialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"電撃エフェクトの検証シーンを作りました：{ScenePath}\n" +
                  "再生して、F の長押しでビーム（長く押すほど遠くへ）、Space で Burst、1 で出す／消す、2 でキャラクターを動かせます。");
    }

    // ------------------------------------------------------------
    // 素材
    // ------------------------------------------------------------

    private static Material GetOrCreateLightningMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(LightningMaterialPath);

        if (existing != null)
        {
            return existing;
        }

        Material material = LightningLine.CreateAdditiveMaterial();
        AssetDatabase.CreateAsset(material, LightningMaterialPath);
        return material;
    }

    private static Material GetOrCreateLitMaterial(string path, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

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

    /// <summary>ブルームだけを入れた後処理の設定を作る。</summary>
    private static VolumeProfile GetOrCreateVolumeProfile()
    {
        VolumeProfile existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);

        if (existing != null)
        {
            return existing;
        }

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, VolumeProfilePath);

        Bloom bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(1f);
        bloom.intensity.Override(1.5f);
        bloom.scatter.Override(0.7f);
        AssetDatabase.AddObjectToAsset(bloom, profile);

        Tonemapping tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.Override(TonemappingMode.Neutral);
        AssetDatabase.AddObjectToAsset(tonemapping, profile);

        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static void SetMaterial(GameObject target, Material material)
    {
        if (material != null)
        {
            target.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>固定アンカーのプレハブと単体検証シーンを作るメニュー。</summary>
public static class FishingAnchorTestSetup
{
    private const string AnchorPrefabPath = FishingSceneBuilder.PrefabFolder + "/FishingAnchor.prefab";
    private const string AnchorMaterialPath = FishingSceneBuilder.MaterialFolder + "/FishingAnchor.mat";
    private const string ScenePath = FishingSceneBuilder.SceneFolder + "/FishingAnchorTest.unity";

    [MenuItem("Tools/Mirai01/釣りのアンカー検証シーンを作る")]
    public static void CreateScene()
    {
        FishingSceneBuilder.EnsureFolders();
        InputActionAsset inputActions = FishingSceneBuilder.LoadInputActions();
        GameObject anchorPrefab = CreateOrUpdatePrefab();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        FishingSceneBuilder.CreateLight();
        FishingSceneBuilder.CreateGround();
        FishingSceneBuilder.CreatePlainWalls();

        GameObject player = FishingSceneBuilder.CreatePlayer();
        Camera camera = FishingSceneBuilder.CreateCamera(player);
        HookProjectile hook = FishingSceneBuilder.CreateHook();
        HookLine line = FishingSceneBuilder.CreateLine(player);
        HookChargeUI ui = FishingSceneBuilder.CreateUI();

        GameObject anchor = (GameObject)PrefabUtility.InstantiatePrefab(anchorPrefab);
        anchor.name = "Anchor";
        anchor.transform.position = new Vector3(0f, 0.75f, 10f);

        FishingSceneBuilder.WirePlayer(player, camera, hook, line, ui, inputActions, null, null);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("【アンカー】検証シーンを作りました。WASDで移動し、左クリックで青いキューブへフックを飛ばしてください。アンカーは動かず、プレイヤーが引っ張られます。");
    }

    private static GameObject CreateOrUpdatePrefab()
    {
        Material material = FishingSceneBuilder.GetOrCreateMaterial(AnchorMaterialPath, new Color(0.1f, 0.38f, 0.95f));
        GameObject anchor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        anchor.name = "FishingAnchor";
        anchor.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
        anchor.GetComponent<MeshRenderer>().sharedMaterial = material;

        Rigidbody body = anchor.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeAll;

        anchor.AddComponent<HookableObject>();
        anchor.AddComponent<AnchorGimmick>();

        GameObject pullPoint = new GameObject("PullPoint");
        pullPoint.transform.SetParent(anchor.transform, false);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(anchor, AnchorPrefabPath);
        Object.DestroyImmediate(anchor);
        return prefab;
    }
}

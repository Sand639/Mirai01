using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **釣りフックの「ステージ」検証シーンを作るツール。**（新しく足したツール）
///
/// Unityのメニュー「Tools > Mirai01 > 釣りフックのステージ検証シーンを作る」から実行できる。
/// 作られるシーン：`Assets/Scenes/Test/FishingArenaTest.unity`
///
/// **遊びとして成立するかを確かめるための、盛り込んだシーン。**
///   ・見下ろしカメラ、床
///   ・**四方の壁の中央を切り欠いたポケット**（入れると点が入る）
///   ・マウス方向を向くプレイヤー（既存の PlayerRig を土台に使用）
///   ・引っ掛けられる物資8個
///   ・**爆発物2個**（釣り上げてから5秒で爆発。物資は消滅、プレイヤーはスタン）
///   ・チャージ量とスキルチェックのゲージ、点数・カウントダウン・スタンの表示
///
/// フックの操作だけを切り離して確かめたいときは、
/// 「Tools > Mirai01 > 釣りフックの検証シーンを作る」の最小構成シーンを使う
/// （<see cref="FishingHookTestSetup"/>。**こちらのシーンは上書きしない**）。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// ※ 何度実行しても作り直せる（シーンを上書きする）。
/// </summary>
public static class FishingArenaTestSetup
{
    private const string ScenePath = FishingSceneBuilder.SceneFolder + "/FishingArenaTest.unity";

    [MenuItem("Tools/Mirai01/釣りフックのステージ検証シーンを作る")]
    public static void CreateScene()
    {
        FishingSceneBuilder.EnsureFolders();
        InputActionAsset inputActions = FishingSceneBuilder.LoadInputActions();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 爆発の見た目をプレハブとして用意する（作業用オブジェクトはすぐ消す）
        ExplosionEffect explosionPrefab = FishingSceneBuilder.CreateExplosionPrefab();

        FishingSceneBuilder.CreateLight();
        FishingSceneBuilder.CreateGround();

        // ---- 点数と画面表示 ----
        GameObject hudObject = new GameObject("FishingHUD");
        ScoreBoard scoreBoard = hudObject.AddComponent<ScoreBoard>();
        FishingStatusUI statusUI = hudObject.AddComponent<FishingStatusUI>();

        // ---- 四方の壁とポケット ----
        FishingSceneBuilder.CreatePocketWalls(scoreBoard);

        GameObject player = FishingSceneBuilder.CreatePlayer();
        Camera camera = FishingSceneBuilder.CreateCamera(player);
        HookProjectile hook = FishingSceneBuilder.CreateHook();
        HookLine line = FishingSceneBuilder.CreateLine(player);

        // ---- 物資と爆発物 ----
        FishingSceneBuilder.CreateSupplyRing(8, 8f);
        FishingSceneBuilder.CreateBomb("Bomb_1", new Vector3(-5.5f, 0.5f, 5.5f), explosionPrefab);
        FishingSceneBuilder.CreateBomb("Bomb_2", new Vector3(5.5f, 0.5f, -5.5f), explosionPrefab);

        HookChargeUI ui = FishingSceneBuilder.CreateUI();

        FishingSceneBuilder.WirePlayer(player, camera, hook, line, ui, inputActions, statusUI, scoreBoard);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "【ステージ】釣りフックの検証シーンを作りました。\n" +
            "シーン: " + ScenePath + "\n" +
            "・WASDで移動、マウスで向き。左クリックを押し込んでチャージ→離してフック発射\n" +
            "・物資に当たると弧を描いて引き寄せられ、ゲージの真ん中で頭の上を通り越す\n" +
            "・前半（青）の枠で押すと後ろへ、後半（橙）の枠で押すと前へ飛ばす（枠を外すとミス）\n" +
            "・四方の壁のポケットに入れると点が入る（床が緑に光る）\n" +
            "・黒い箱は爆発物。釣り上げてから5秒で爆発し、近くの物資は消え、プレイヤーは2秒スタンする");
    }
}

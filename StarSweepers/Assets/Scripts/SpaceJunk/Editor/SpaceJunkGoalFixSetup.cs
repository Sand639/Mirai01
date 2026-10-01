using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **開いているマップに置かれた「釣りのゴール」を、「宇宙ごみのゴール」に直すツール**（2026/10/1）。
///
/// Unityのメニュー「Tools &gt; StarSweepers &gt; 開いているマップの釣りのゴールを宇宙ごみのゴールに直す」から使う。
///
/// ## なぜ要るか
///
/// `Assets/Prefabs/Fish/Online/GoalArea.prefab` は**釣りのゴール**（`FishingNetPocket`）で、
/// 宇宙ごみのゴール（`SpaceJunkGoal`）は付いていない。今あるマップは、作るツールが自動で付け替えていたので動いていた。
/// 手で GoalArea を置くと釣りのゴールのままなので、**「〇〇チームのゴールがありません」になってゲームが進まない**
/// （2026/10/1：大槻さんが作った SpaceJunkMap05 で実際に起きた）。
///
/// ## やること
///
/// 1. 開いているシーンの `FishingNetPocket` を全部探し、プレハブから切り離す
/// 2. `FishingNetPocket` を外して `SpaceJunkGoal` を付ける（床の見た目は引き継ぐ）
/// 3. **方角（Goal Index）は、ステージの中心（原点）から見た置き場所で決める**
///    （GoalArea の番号はコピーのたびにずれるので当てにしない。北＝奥（+Z）、東＝右（+X）、南＝手前、西＝左）
/// 4. 保存して、マップの一覧にゴールの方角を控え、点検を走らせる
/// </summary>
public static class SpaceJunkGoalFixSetup
{
    [MenuItem("Tools/StarSweepers/開いているマップの釣りのゴールを宇宙ごみのゴールに直す")]
    public static void FixOpenScene()
    {
        Scene scene = SceneManager.GetActiveScene();

        if (string.IsNullOrEmpty(scene.path))
        {
            Debug.LogError("[JUNK] シーンが保存されていません。先にマップのシーンを保存してから実行してください。");
            return;
        }

        if (Object.FindFirstObjectByType<SpaceJunkRound>(FindObjectsInactive.Include) == null)
        {
            Debug.LogError(
                $"[JUNK] {scene.name} には、ラウンドの進行役（SpaceJunkRound）がありません。宇宙ごみのマップを開いてから実行してください。\n" +
                "（釣りのマップを壊さないよう、宇宙ごみのマップでだけ動くようにしてある）");
            return;
        }

        FishingNetPocket[] pockets = Object.FindObjectsByType<FishingNetPocket>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (pockets.Length == 0)
        {
            Debug.Log($"[JUNK] {scene.name} に釣りのゴールはありませんでした（直すものはありません）。");
            SpaceJunkSetup.VerifyMap(scene.path);
            return;
        }

        List<string> results = new List<string>();

        foreach (FishingNetPocket pocket in pockets)
        {
            if (pocket == null)
            {
                continue;
            }

            GameObject host = pocket.gameObject;

            // プレハブの一部のままだと部品を外せないので、先に切り離す（釣りの GoalArea.prefab 自体は変わらない）
            GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(host);
            if (prefabRoot != null)
            {
                PrefabUtility.UnpackPrefabInstance(prefabRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            // 床の見た目（チームの色に塗る板）を引き継ぐ
            Object padRenderer = new SerializedObject(pocket).FindProperty("padRenderer")?.objectReferenceValue;

            Object.DestroyImmediate(pocket);

            SpaceJunkGoal goal = host.GetComponent<SpaceJunkGoal>();
            if (goal == null)
            {
                goal = host.AddComponent<SpaceJunkGoal>();
            }

            int index = DirectionIndex(host.transform.position);

            SerializedObject serialized = new SerializedObject(goal);
            serialized.FindProperty("goalIndex").intValue = index;
            serialized.FindProperty("padRenderer").objectReferenceValue = padRenderer;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            results.Add($"{host.name} → {SpaceJunkTeams.GoalPlaceName(index)}（Goal Index {index}）");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        // ロビーで候補を絞るために、ゴールの方角を一覧に控え直す
        SpaceJunkMapListSetup.RefreshGoalInfo();
        SpaceJunkSetup.VerifyMap(scene.path);

        Debug.Log(
            $"【宇宙ごみ】{scene.name} の釣りのゴール {results.Count} 個を、宇宙ごみのゴールに直して保存しました。\n・" +
            string.Join("\n・", results) +
            "\n\n方角は、ステージの中心（原点）から見た置き場所で決めました。違っていたら、ゴールの SpaceJunkGoal の Goal Index を直してください" +
            "（0＝北・1＝東・2＝南・3＝西）。**直したら『宇宙ごみのマップを点検する』をもう一度実行すること。**");
    }

    /// <summary>ステージの中心（原点）から見た方角。北＝+Z、東＝+X、南＝-Z、西＝-X。</summary>
    private static int DirectionIndex(Vector3 position)
    {
        if (Mathf.Abs(position.x) > Mathf.Abs(position.z))
        {
            return position.x >= 0f ? 1 : 3;
        }

        return position.z >= 0f ? 0 : 2;
    }
}

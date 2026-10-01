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
/// 3. **番号（Goal Index）は、中心（原点）から見て北から時計回りの順に 0, 1, 2… と振る**
///    （GoalArea の番号はコピーのたびにずれるので当てにしない。4つを四方に置けば 北0・東1・南2・西3、120度ずつ3つなら 0・1・2）
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

        int converted = 0;

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

            SerializedObject serialized = new SerializedObject(goal);
            serialized.FindProperty("padRenderer").objectReferenceValue = padRenderer;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            converted++;
        }

        // **番号は、このマップのゴール全部を「中心から見て北から時計回り」に並べて 0, 1, 2… と振り直す**
        List<string> results = RenumberClockwise();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        // ロビーで候補を絞るために、ゴールの番号を一覧に控え直す
        SpaceJunkMapListSetup.RefreshGoalInfo();
        SpaceJunkSetup.VerifyMap(scene.path);

        Debug.Log(
            $"【宇宙ごみ】{scene.name} の釣りのゴール {converted} 個を、宇宙ごみのゴールに直して保存しました。\n" +
            "ゴールの番号（Goal Index）は、中心から見て北（奥）から時計回りの順に振りました：\n・" +
            string.Join("\n・", results) +
            "\n\n4つなら 北0・東1・南2・西3 になる。4つ未満なら、**番号の小さい順に 青・赤・緑…** のゴールになる。" +
            "違っていたら Goal Index を直し、**『宇宙ごみのマップを点検する』をもう一度実行すること。**");
    }

    /// <summary>
    /// **シーンのゴールを全部、中心（原点）から見て北（+Z）から時計回りの順に並べ、0 から番号を振り直す。**
    /// 4つを北・東・南・西に置けば 0・1・2・3 になり、120度ずつ3つ置けば 0・1・2 になる（2026/10/1）。
    /// </summary>
    private static List<string> RenumberClockwise()
    {
        List<SpaceJunkGoal> goals = new List<SpaceJunkGoal>(Object.FindObjectsByType<SpaceJunkGoal>(
            FindObjectsInactive.Include, FindObjectsSortMode.None));

        goals.Sort((a, b) => ClockwiseAngle(a.transform.position).CompareTo(ClockwiseAngle(b.transform.position)));

        List<string> results = new List<string>();

        for (int i = 0; i < goals.Count; i++)
        {
            int index = Mathf.Min(i, SpaceJunkTeams.GoalCount - 1);

            SerializedObject serialized = new SerializedObject(goals[i]);
            serialized.FindProperty("goalIndex").intValue = index;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            results.Add($"{goals[i].name} → Goal Index {index}（北から時計回りに {ClockwiseAngle(goals[i].transform.position):0} 度）");
        }

        if (goals.Count > SpaceJunkTeams.GoalCount)
        {
            Debug.LogError($"[JUNK] ゴールが {goals.Count} 個あります。**{SpaceJunkTeams.GoalCount} 個まで**にしてください（番号がかぶります）。");
        }

        return results;
    }

    /// <summary>中心（原点）から見た向き。北（+Z）を 0 度として、時計回り（東＝90、南＝180、西＝270）。</summary>
    private static float ClockwiseAngle(Vector3 position)
    {
        float angle = Mathf.Atan2(position.x, position.z) * Mathf.Rad2Deg;
        return angle < 0f ? angle + 360f : angle;
    }
}

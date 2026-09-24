using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **宇宙ごみのマップの一覧（<see cref="SpaceJunkMapList"/>）を用意して、ロビーに結びつける係。**
///
/// Unityのメニュー「Tools &gt; Mirai01 &gt; 宇宙ごみのマップの一覧を開く」から使う。
///
/// ## やること
///
/// 1. 一覧が無ければ作る（`Assets/Scenes/Prototype/SpaceJunk/SpaceJunkMapList.asset`）
///    - **最初に作るときだけ**、いまある `SpaceJunkMap〜` のシーンを入れておく
///    - **Map03・Map04 は作りかけなので「ロビーに出す」を外して入れる**（2026/9/22・大槻さん）
/// 2. ロビーのシーンの画面（`SpaceJunkLobbyUI`）に、この一覧を結びつける
/// 3. 一覧を選んだ状態にして、インスペクターに出す
///
/// ほかのツール（マップを作るツール・全体固定カメラのマップを作るツール）も、
/// ここの <see cref="AddMap"/> で作ったマップを一覧に足している。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class SpaceJunkMapListSetup
{
    public const string ListPath = SpaceJunkSetup.MapSceneFolder + "/SpaceJunkMapList.asset";

    /// <summary>最初に一覧を作るとき、ロビーに出さずに入れるマップ（作りかけのもの）。</summary>
    private static readonly string[] HiddenAtFirst = { "SpaceJunkMap03", "SpaceJunkMap04" };

    [MenuItem("Tools/Mirai01/宇宙ごみのマップの一覧を開く")]
    public static void Open()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[JUNK] 再生を止めてから実行してください（ロビーのシーンを書き換えるため）。");
            return;
        }

        SpaceJunkMapList list = EnsureList();
        WireLobby(list);

        Selection.activeObject = list;
        EditorGUIUtility.PingObject(list);

        Debug.Log(
            "【宇宙ごみ】マップの一覧を開きました。\n" +
            $"場所：{ListPath}\n\n" +
            "・インスペクターの Maps に、シーンを Project からドラッグして足す（名前・置き場所は自由）\n" +
            "・Show In Lobby を外すと、ロビーの候補に出なくなる（作りかけのマップ用）\n" +
            "・Display Name を入れると、ロビーではその名前で出る\n" +
            "・入れたシーンは、ビルドの一覧にも自動で登録される");
    }

    /// <summary>
    /// 一覧を読み込む。**無ければ作る。**
    /// 作るときは、いまある `SpaceJunkMap〜` のシーンを入れておく（それまでと同じ候補になるように）。
    /// </summary>
    /// <param name="wireLobby">
    /// 新しく作ったときに、ロビーにも結びつけるか。
    /// **ロビーを作っている最中のツールは false にする**（作り終えたロビーに自分で結びつけるため）。
    /// </param>
    public static SpaceJunkMapList EnsureList(bool wireLobby = true)
    {
        SpaceJunkMapList list = AssetDatabase.LoadAssetAtPath<SpaceJunkMapList>(ListPath);

        if (list != null)
        {
            return list;
        }

        FishingSceneBuilder.EnsureFolder(SpaceJunkSetup.MapSceneFolder);

        list = ScriptableObject.CreateInstance<SpaceJunkMapList>();
        AssetDatabase.CreateAsset(list, ListPath);

        foreach (string path in FindExistingMaps())
        {
            list.EditorAdd(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), DefaultShow(path));
        }

        EditorUtility.SetDirty(list);
        AssetDatabase.SaveAssets();

        // **作ったらすぐロビーに結びつける。** 結びつけないと、ロビーは昔の見分け方
        // （名前が SpaceJunkMap で始まるもの）のままになり、一覧のチェックが効かない
        if (wireLobby)
        {
            WireLobby(list);
        }

        Debug.Log($"[JUNK] マップの一覧を作りました：{ListPath}（{list.Maps.Count} 個。Map03・Map04 はロビーに出さない設定で入れた）");
        return list;
    }

    /// <summary>
    /// 一覧に初めて入れるとき、ロビーに出すかどうかの初期値。
    /// 作りかけのマップ（<see cref="HiddenAtFirst"/>）だけ出さない。
    /// </summary>
    private static bool DefaultShow(string scenePath)
    {
        return System.Array.IndexOf(HiddenAtFirst, Path.GetFileNameWithoutExtension(scenePath)) < 0;
    }

    /// <summary>
    /// **マップを一覧に足す。** すでに入っていれば何もしない（チェックや表示名を上書きしない）。
    /// マップを作るツールから呼ばれる。ロビーに出すかどうかは <see cref="DefaultShow"/> で決まる。
    /// </summary>
    public static void AddMap(string scenePath)
    {
        AddMap(scenePath, DefaultShow(scenePath));
    }

    /// <summary>マップを一覧に足す（ロビーに出すかどうかを指定する版）。</summary>
    public static void AddMap(string scenePath, bool showInLobby)
    {
        SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);

        if (scene == null)
        {
            return;
        }

        SpaceJunkMapList list = EnsureList();

        if (list.EditorAdd(scene, showInLobby))
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"[JUNK] マップの一覧に足しました：{Path.GetFileNameWithoutExtension(scenePath)}");
        }
    }

    /// <summary>一覧に入っているシーンの置き場所（ビルドに入れるため）。</summary>
    public static List<string> ScenePaths()
    {
        List<string> paths = new List<string>();
        SpaceJunkMapList list = AssetDatabase.LoadAssetAtPath<SpaceJunkMapList>(ListPath);

        if (list == null)
        {
            return paths;
        }

        foreach (SpaceJunkMapList.Entry entry in list.Maps)
        {
            if (entry != null && entry.IsValid && File.Exists(entry.ScenePath) && !paths.Contains(entry.ScenePath))
            {
                paths.Add(entry.ScenePath);
            }
        }

        return paths;
    }

    /// <summary>
    /// **ロビーのシーンの画面に、一覧を結びつける。**
    ///
    /// ロビーが開いていなければ、裏で開いて書き換えてから閉じる（いま開いているシーンは変えない）。
    /// </summary>
    public static void WireLobby(SpaceJunkMapList list)
    {
        if (list == null || !File.Exists(SpaceJunkSetup.LobbyScenePath))
        {
            return;
        }

        Scene lobby = SceneManager.GetSceneByPath(SpaceJunkSetup.LobbyScenePath);
        bool wasOpen = lobby.isLoaded;

        if (!wasOpen)
        {
            lobby = EditorSceneManager.OpenScene(SpaceJunkSetup.LobbyScenePath, OpenSceneMode.Additive);
        }

        int wired = 0;

        foreach (GameObject root in lobby.GetRootGameObjects())
        {
            foreach (SpaceJunkLobbyUI ui in root.GetComponentsInChildren<SpaceJunkLobbyUI>(true))
            {
                FishingSceneBuilder.SetRef(ui, "mapList", list);
                wired++;
            }
        }

        if (wired > 0)
        {
            EditorSceneManager.MarkSceneDirty(lobby);
            EditorSceneManager.SaveScene(lobby);
        }
        else
        {
            Debug.LogWarning("[JUNK] ロビーのシーンに SpaceJunkLobbyUI が見つからず、マップの一覧を結びつけられませんでした。");
        }

        if (!wasOpen)
        {
            EditorSceneManager.CloseScene(lobby, true);
        }
    }

    /// <summary>`SpaceJunkMap〜` という名前の既存のシーン（一覧を最初に作るときだけ使う）。</summary>
    private static List<string> FindExistingMaps()
    {
        List<string> paths = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (Path.GetFileNameWithoutExtension(path).StartsWith("SpaceJunkMap") && !paths.Contains(path))
            {
                paths.Add(path);
            }
        }

        paths.Sort(string.CompareOrdinal);
        return paths;
    }
}

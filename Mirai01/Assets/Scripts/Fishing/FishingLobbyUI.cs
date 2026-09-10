using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **ロビーの画面。** 誰が入っているか・どのチームかを並べて、ホストが試合を始める。
///
/// つなぐ操作そのものは既存の `LanConnectionUi`（LAN／インターネット／1人）が担当する。
/// このスクリプトは**つながったあと**の「待合室」だけを受け持つ。
///
/// **ゲーム開始を押せるのはホストだけ。**
/// 押すと、`NetworkManager` のシーン管理で**全員のシーンがまとめて切り替わる**。
/// 参加者が自分でシーンを読み込む必要はない。
///
/// これは通信の中身を確かめるための仮の表示で、
/// **本番のロビー画面はあとで作り直す**前提のもの（`ネットワークの制作工程.md` の工程3）。
/// </summary>
public class FishingLobbyUI : MonoBehaviour
{
    [Header("つなぎ先のシーン")]
    [Tooltip("ゲーム開始で全員が移動するシーンの名前。**Build Settings に入っていないと読み込めない**")]
    [SerializeField] private string gameSceneName = "FishingOnline";

    [Header("表示")]
    [Tooltip("OFFにすると何も表示しない")]
    [SerializeField] private bool showUi = true;

    [Tooltip("文字の大きさ")]
    [Range(1f, 4f)]
    [SerializeField] private float uiScale = 1.5f;

    [Tooltip("画面の左からの位置（LanConnectionUi と重ならないようにずらす）")]
    [SerializeField] private float panelX = 366f;

    private GUIStyle labelStyle;

    private void OnGUI()
    {
        if (!showUi)
        {
            return;
        }

        NetworkManager manager = NetworkManager.Singleton;

        // つながっていないときは、つなぐ画面（LanConnectionUi）に任せる
        if (manager == null || (!manager.IsClient && !manager.IsServer))
        {
            return;
        }

        PrepareStyle();

        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

        GUILayout.BeginArea(new Rect(panelX, 12f, 320f, 420f), GUI.skin.box);

        DrawRoster();
        DrawTeamRule();
        DrawStartButton(manager);

        GUILayout.EndArea();
        GUI.matrix = saved;
    }

    private void PrepareStyle()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        }
    }

    /// <summary>入っている人を並べる。参加者のPCでも見えるように、生まれた本体の一覧を使う。</summary>
    private void DrawRoster()
    {
        int playerCount = FishingNetPlayer.All.Count;
        int teamCount = FishingTeams.TeamCountFor(playerCount);

        GUILayout.Label($"■ ロビー（{playerCount} / {FishingTeams.MaxPlayers} 人）", labelStyle);
        GUILayout.Space(4f);

        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player == null)
            {
                continue;
            }

            string mark = player.IsOwner ? "▶ " : "　 ";
            Color saved = GUI.color;
            GUI.color = FishingTeams.TeamColor(player.TeamIndex);

            GUILayout.Label(
                $"{mark}{player.DisplayName}　（{FishingTeams.TeamName(player.TeamIndex)}）",
                labelStyle);

            GUI.color = saved;
        }

        GUILayout.Space(6f);
        GUILayout.Label($"いまの人数だと **{teamCount} チーム** に分かれます", labelStyle);
    }

    /// <summary>いまの人数でゴールがどう割り当てられるかを出す。</summary>
    private void DrawTeamRule()
    {
        int teamCount = FishingTeams.TeamCountFor(FishingNetPlayer.All.Count);
        int[] owners = FishingTeams.PocketOwners(teamCount);

        GUILayout.Space(6f);
        GUILayout.Label("■ ゴールの割り当て", labelStyle);

        for (int i = 0; i < owners.Length; i++)
        {
            Color saved = GUI.color;
            GUI.color = FishingTeams.TeamColor(owners[i]);

            GUILayout.Label(
                $"　{FishingTeams.PocketPlaceName(i)} … {FishingTeams.TeamName(owners[i])}",
                labelStyle);

            GUI.color = saved;
        }
    }

    /// <summary>ホストだけに「ゲーム開始」を出す。</summary>
    private void DrawStartButton(NetworkManager manager)
    {
        GUILayout.Space(10f);

        if (!manager.IsServer)
        {
            GUILayout.Label("ホストが始めるのを待っています…", labelStyle);
            return;
        }

        if (!manager.NetworkConfig.EnableSceneManagement)
        {
            GUILayout.Label(
                "NetworkManager の Enable Scene Management が OFF です。" +
                "ONにしないとシーンを切り替えられません。", labelStyle);
            return;
        }

        if (GUILayout.Button("ゲーム開始（全員でシーンを移動）"))
        {
            StartGame(manager);
        }
    }

    /// <summary>
    /// 全員のシーンをまとめて切り替える。**ホストだけが呼べる。**
    /// 参加者のシーンは `NetworkManager` が自動で合わせてくれる。
    /// </summary>
    private void StartGame(NetworkManager manager)
    {
        if (string.IsNullOrEmpty(gameSceneName))
        {
            Debug.LogError("移動先のシーン名が空です。", this);
            return;
        }

        var status = manager.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);

        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError(
                $"[FISH] シーン「{gameSceneName}」を読み込めませんでした（{status}）。\n" +
                "**File > Build Settings のシーン一覧に、ロビーと釣りのシーンの両方が入っているか**確認してください。");
        }
    }
}

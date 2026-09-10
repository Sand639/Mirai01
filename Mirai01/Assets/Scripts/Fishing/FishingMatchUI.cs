using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **試合の残り時間と、決着したときの結果を出す画面。**
///
/// 対戦中 … 画面上に**残り時間**（目標点で戦う設定なら「◯点先取」）
/// 決着後 … 画面中央に**結果**（チームごとの最終得点と、勝ったチーム）と、
///          **ホストだけに「ロビーへ戻る」**
///
/// 数える／勝敗を決めるのは <see cref="FishingMatch"/>（ホスト）。
/// こちらはその値を出すだけで、判定は一切していない。
///
/// フォントの素材を用意しなくても出せるように `OnGUI` で描いている
/// （既存の `NetworkStatusHud.cs` と同じ作り）。**本番の画面はあとで作り直す前提。**
/// </summary>
public class FishingMatchUI : MonoBehaviour
{
    [Header("戻り先のシーン")]
    [Tooltip("「ロビーへ戻る」で全員が移動するシーンの名前。**Build Settings に入っていること**")]
    [SerializeField] private string lobbySceneName = "FishingLobby";

    [Header("表示")]
    [Tooltip("OFFにすると何も表示しない")]
    [SerializeField] private bool showUi = true;

    [Tooltip("文字の大きさ")]
    [Range(1f, 4f)]
    [SerializeField] private float uiScale = 1.6f;

    [Tooltip("残り時間がこの秒数を切ったら赤くする")]
    [SerializeField] private float warnSeconds = 30f;

    private GUIStyle timerStyle;
    private GUIStyle resultStyle;
    private GUIStyle lineStyle;

    private void OnGUI()
    {
        if (!showUi || FishingMatch.Current == null)
        {
            return;
        }

        PrepareStyles();

        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

        float width = Screen.width / uiScale;
        FishingMatch match = FishingMatch.Current;

        if (match.IsPlaying)
        {
            DrawTimer(match, width);
        }
        else
        {
            DrawResult(match, width);
        }

        GUI.matrix = saved;
    }

    private void PrepareStyles()
    {
        if (timerStyle == null)
        {
            timerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }

        if (resultStyle == null)
        {
            resultStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }

        if (lineStyle == null)
        {
            lineStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
        }
    }

    // ------------------------------------------------------------
    // 対戦中
    // ------------------------------------------------------------

    private void DrawTimer(FishingMatch match, float width)
    {
        if (match.EndRule == MatchEndRule.TargetScore)
        {
            GUI.color = Color.white;
            GUI.Label(new Rect(0f, 4f, width, 32f), $"{match.TargetScore} 点先取", timerStyle);
            return;
        }

        float remaining = match.RemainingSeconds;
        int minutes = Mathf.FloorToInt(remaining / 60f);
        int seconds = Mathf.FloorToInt(remaining % 60f);

        GUI.color = remaining <= warnSeconds
            ? new Color(1f, 0.3f, 0.25f)
            : Color.white;

        GUI.Label(new Rect(0f, 4f, width, 32f), $"{minutes}:{seconds:00}", timerStyle);
        GUI.color = Color.white;
    }

    // ------------------------------------------------------------
    // 決着後
    // ------------------------------------------------------------

    private void DrawResult(FishingMatch match, float width)
    {
        float height = Screen.height / uiScale;
        float y = height * 0.24f;

        // 勝ったチーム（引き分けもある）
        if (match.WinnerTeam < 0)
        {
            GUI.color = Color.white;
            GUI.Label(new Rect(0f, y, width, 40f), "引き分け", resultStyle);
        }
        else
        {
            GUI.color = FishingTeams.TeamColor(match.WinnerTeam);
            GUI.Label(new Rect(0f, y, width, 40f),
                $"{FishingTeams.TeamName(match.WinnerTeam)} の勝ち！", resultStyle);
        }

        y += 46f;

        // チームごとの最終得点
        for (int team = 0; team < match.TeamCount; team++)
        {
            GUI.color = FishingTeams.TeamColor(team);
            GUI.Label(new Rect(0f, y, width, 22f),
                $"{FishingTeams.TeamName(team)}　{match.ScoreOf(team)} 点", lineStyle);
            y += 22f;
        }

        GUI.color = Color.white;
        y += 14f;

        DrawReturnButton(width, y);
    }

    /// <summary>ホストだけに「ロビーへ戻る」を出す。</summary>
    private void DrawReturnButton(float width, float y)
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null || !manager.IsServer)
        {
            GUI.Label(new Rect(0f, y, width, 22f), "ホストがロビーへ戻すのを待っています…", lineStyle);
            return;
        }

        float buttonWidth = 260f;
        Rect rect = new Rect((width - buttonWidth) * 0.5f, y, buttonWidth, 30f);

        if (GUI.Button(rect, "ロビーへ戻る（全員で移動）"))
        {
            ReturnToLobby(manager);
        }
    }

    /// <summary>
    /// 全員のシーンをロビーへ戻す。**ホストだけが呼べる。**
    /// 戻ると点数はリセットされ、もう一度「ゲーム開始」で遊べる。
    /// </summary>
    private void ReturnToLobby(NetworkManager manager)
    {
        if (string.IsNullOrEmpty(lobbySceneName))
        {
            Debug.LogError("戻り先のシーン名が空です。", this);
            return;
        }

        var status = manager.SceneManager.LoadScene(
            lobbySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);

        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError(
                $"[FISH] シーン「{lobbySceneName}」を読み込めませんでした（{status}）。\n" +
                "**File > Build Settings のシーン一覧に、ロビーと会場の両方が入っているか**確認してください。");
        }
    }
}

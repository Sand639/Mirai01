using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **ラウンド中の画面。** マップ（ステージ）のシーンに1つ置く。
///
/// 出しているもの：
///
///   ・**いま何ラウンド目か**と、何本先取か
///   ・**残り時間**（残り10秒を切ると赤くなる）
///   ・**チームごとに、どの素材をそろえたか**（3種類ぶんの印）
///   ・**チームごとのラウンドの勝ち数**
///   ・決着後 … **このラウンドを取ったチーム**（引き分けならその旨）
///   ・試合が終わったら … **試合に勝ったチーム**
///
/// 数える／勝敗を決めるのは <see cref="SpaceJunkRound"/> と <see cref="SpaceJunkSession"/>（ホスト）。
/// こちらはその値を出すだけで、判定は一切していない。
///
/// フォントの素材を用意しなくても出せるように `OnGUI` で描いている。
/// **本番の画面はあとで作り直す前提。**
/// </summary>
public class SpaceJunkMatchUI : MonoBehaviour
{
    [Header("表示")]
    [Tooltip("OFFにすると何も表示しない")]
    [SerializeField] private bool showUi = true;

    [Tooltip("文字の大きさ。**窓が小さいときは自動で縮む**ので、これは上限として使われる")]
    [Range(1f, 4f)]
    [SerializeField] private float uiScale = 1.6f;

    [Tooltip("残り時間がこの秒数を切ったら赤くする")]
    [SerializeField] private float warnSeconds = 10f;

    /// <summary>描く基準の幅（この幅に収まるように縮める）。</summary>
    private const float BaseWidth = 520f;

    private GUIStyle titleStyle;
    private GUIStyle timerStyle;
    private GUIStyle lineStyle;
    private GUIStyle resultStyle;

    private void OnGUI()
    {
        SpaceJunkRound round = SpaceJunkRound.Current;

        if (!showUi || round == null)
        {
            return;
        }

        PrepareStyles();

        float scale = Mathf.Min(DraggableGuiPanel.FitScale(uiScale), Screen.width / BaseWidth, Screen.height / 360f);
        scale = Mathf.Max(0.6f, scale);

        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        float viewWidth = Screen.width / scale;

        DrawHeader(round, viewWidth);
        DrawTeamProgress(round, viewWidth);

        if (round.Phase == SpaceJunkRoundPhase.Result)
        {
            DrawResult(round, viewWidth, Screen.height / scale);
        }

        GUI.matrix = saved;
    }

    private void PrepareStyles()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter
            };
        }

        if (timerStyle == null)
        {
            timerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter
            };
        }

        if (lineStyle == null)
        {
            lineStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        }

        if (resultStyle == null)
        {
            resultStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
        }
    }

    /// <summary>画面の上に、ラウンド数と残り時間を出す。</summary>
    private void DrawHeader(SpaceJunkRound round, float viewWidth)
    {
        SpaceJunkSession session = SpaceJunkSession.Current;

        string title = session != null
            ? $"第 {session.CurrentRound} ラウンド　（{session.RoundsToWin} 本先取）"
            : "ラウンド中";

        GUI.Label(new Rect(0f, 8f, viewWidth, 24f), title, titleStyle);

        float remaining = round.RemainingSeconds;
        int minutes = Mathf.FloorToInt(remaining / 60f);
        int seconds = Mathf.FloorToInt(remaining % 60f);

        Color saved = GUI.color;
        GUI.color = remaining <= warnSeconds ? new Color(1f, 0.45f, 0.4f) : Color.white;

        GUI.Label(new Rect(0f, 30f, viewWidth, 40f), $"{minutes}:{seconds:00}", timerStyle);

        GUI.color = saved;
    }

    /// <summary>
    /// 画面の左に、チームごとの進み具合を出す。
    /// **そろえた種類には色がつき、まだのものは暗いまま。**
    /// </summary>
    private void DrawTeamProgress(SpaceJunkRound round, float viewWidth)
    {
        SpaceJunkSession session = SpaceJunkSession.Current;
        int teamCount = session != null ? session.TeamCount : 1;
        int myTeam = MyTeam();

        float y = 80f;

        GUILayout.BeginArea(new Rect(12f, y, 260f, 240f));

        for (int team = 0; team < teamCount; team++)
        {
            Color saved = GUI.color;
            GUI.color = SpaceJunkTeams.TeamColor(team);

            string mark = team == myTeam ? "▶ " : "　 ";
            int wins = session != null ? session.RoundWinsOf(team) : 0;

            GUILayout.Label($"{mark}{SpaceJunkTeams.TeamName(team)}　（{wins} 本）", lineStyle);

            GUI.color = saved;

            // 素材3種類ぶんの印
            GUILayout.BeginHorizontal();
            GUILayout.Space(16f);

            for (int i = 0; i < SpaceJunkMaterials.Count; i++)
            {
                SpaceJunkMaterialKind kind = SpaceJunkMaterials.FromIndex(i);
                bool has = round.HasKind(team, kind);

                Color kindSaved = GUI.color;
                GUI.color = has
                    ? SpaceJunkMaterials.Color(kind)
                    : new Color(0.35f, 0.35f, 0.38f);

                GUILayout.Label(has ? $"■ {SpaceJunkMaterials.Name(kind)}" : $"□ {SpaceJunkMaterials.Name(kind)}",
                                lineStyle);

                GUI.color = kindSaved;
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        GUILayout.EndArea();
    }

    /// <summary>決着後の表示。</summary>
    private void DrawResult(SpaceJunkRound round, float viewWidth, float viewHeight)
    {
        SpaceJunkSession session = SpaceJunkSession.Current;

        string text;
        Color color = Color.white;

        if (session != null && session.State == SpaceJunkMatchState.MatchOver)
        {
            int winner = session.MatchWinner;
            text = winner >= 0
                ? $"{SpaceJunkTeams.TeamName(winner)} の勝ち！\n（{session.RoundWinsOf(winner)} 本先取）\n\nまもなくロビーへ戻ります"
                : "試合終了";

            if (winner >= 0)
            {
                color = SpaceJunkTeams.TeamColor(winner);
            }
        }
        else
        {
            int winner = round.RoundWinner;
            text = winner >= 0
                ? $"{SpaceJunkTeams.TeamName(winner)} がラウンドを取りました\n\nまもなく次のマップへ"
                : "引き分け\nこのラウンドは誰も取りません\n\nまもなく次のマップへ";

            if (winner >= 0)
            {
                color = SpaceJunkTeams.TeamColor(winner);
            }
        }

        Rect box = new Rect(viewWidth * 0.5f - 200f, viewHeight * 0.5f - 90f, 400f, 180f);

        GUI.Box(box, GUIContent.none);

        Color saved = GUI.color;
        GUI.color = color;
        GUI.Label(box, text, resultStyle);
        GUI.color = saved;
    }

    /// <summary>このPCで操作している人のチーム。分からなければ -1。</summary>
    private static int MyTeam()
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null || SpaceJunkSession.Current == null)
        {
            return -1;
        }

        return SpaceJunkSession.Current.TeamOf(manager.LocalClientId);
    }
}

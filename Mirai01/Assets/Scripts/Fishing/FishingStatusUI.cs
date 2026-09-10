using UnityEngine;

/// <summary>
/// **確認用の文字表示。** 点数・爆発までの残り秒数・スタン中の表示を画面に出す。
///
/// フォントの素材を用意しなくても出せるように、`OnGUI` で描いている
/// （既存の `NetworkStatusHud.cs` と同じ作り）。
/// **プロトタイプ用の表示なので、本番の画面には出さない**（Show Hud を切る）。
///
/// チャージ量とスキルチェックのゲージは <see cref="HookChargeUI"/> の担当で、こちらには含まない。
/// </summary>
public class FishingStatusUI : MonoBehaviour
{
    [Header("表示するか")]
    [Tooltip("OFFにすると何も表示しない（本番では切る）")]
    [SerializeField] private bool showHud = true;

    [Tooltip("文字の大きさ")]
    [Range(1f, 4f)]
    [SerializeField] private float uiScale = 1.6f;

    [Header("参照")]
    [Tooltip("1人用の点数を持っている ScoreBoard（オンラインのシーンでは空でよい）")]
    [SerializeField] private ScoreBoard scoreBoard;

    [Tooltip("プレイヤーの PlayerStun。オンラインでは自分のプレイヤーから自動で探す")]
    [SerializeField] private PlayerStun playerStun;

    [Header("表示の設定")]
    [Tooltip("「◯◯に投入！」を出しておく秒数")]
    [SerializeField] private float eventMessageSeconds = 2f;

    [Tooltip("操作の説明を出す")]
    [SerializeField] private bool showControls = true;

    private GUIStyle labelStyle;
    private GUIStyle bigStyle;

    private void OnGUI()
    {
        if (!showHud)
        {
            return;
        }

        PrepareStyles();

        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

        float width = Screen.width / uiScale;
        DrawScore();
        DrawBombTimer(width);
        DrawStun(width);
        DrawControls(width);

        GUI.matrix = saved;
    }

    private void PrepareStyles()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        }
        if (bigStyle == null)
        {
            bigStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }
    }

    /// <summary>
    /// 左上に点数を出す。
    /// **オンラインの試合があればチームごとの点数**、無ければ1人用の点数を出す。
    /// </summary>
    private void DrawScore()
    {
        if (FishingMatch.Current != null)
        {
            DrawTeamScore(FishingMatch.Current);
            return;
        }

        if (scoreBoard == null)
        {
            return;
        }

        float y = 8f;

        for (int i = 0; i < scoreBoard.PlayerCount; i++)
        {
            GUI.color = Color.white;
            GUI.Label(new Rect(10f, y, 320f, 20f),
                $"{scoreBoard.NameOf(i)}　スコア {scoreBoard.ScoreOf(i)}", labelStyle);
            y += 18f;
        }

        if (!string.IsNullOrEmpty(scoreBoard.LastEvent)
            && Time.time - scoreBoard.LastEventTime < eventMessageSeconds)
        {
            GUI.color = new Color(0.4f, 1f, 0.5f);
            GUI.Label(new Rect(10f, y, 380f, 20f), scoreBoard.LastEvent, labelStyle);
            GUI.color = Color.white;
        }
    }

    /// <summary>チームごとの点数を出す。自分のチームには印を付ける。</summary>
    private void DrawTeamScore(FishingMatch match)
    {
        int myTeam = FindMyTeam();
        float y = 8f;

        for (int team = 0; team < match.TeamCount; team++)
        {
            GUI.color = FishingTeams.TeamColor(team);

            string mark = team == myTeam ? "▶ " : "　 ";
            GUI.Label(new Rect(10f, y, 340f, 20f),
                $"{mark}{FishingTeams.TeamName(team)}　{match.ScoreOf(team)} 点", labelStyle);

            y += 18f;
        }

        GUI.color = Color.white;

        if (!string.IsNullOrEmpty(match.LastEvent)
            && Time.time - match.LastEventTime < eventMessageSeconds)
        {
            GUI.color = new Color(0.4f, 1f, 0.5f);
            GUI.Label(new Rect(10f, y, 420f, 20f), match.LastEvent, labelStyle);
            GUI.color = Color.white;
        }
    }

    /// <summary>自分が操作しているプレイヤーのチーム。見つからなければ -1。</summary>
    private int FindMyTeam()
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.IsOwner)
            {
                return player.TeamIndex;
            }
        }
        return -1;
    }

    /// <summary>オンラインでは、自分のプレイヤーから PlayerStun を拾う。</summary>
    private PlayerStun ResolveStun()
    {
        if (playerStun != null)
        {
            return playerStun;
        }

        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.IsOwner)
            {
                return player.GetComponent<PlayerStun>();
            }
        }
        return null;
    }

    /// <summary>火のついた爆発物があれば、画面上部に残り秒数を大きく出す。</summary>
    private void DrawBombTimer(float width)
    {
        ExplosiveObject nearest = FindMostUrgentBomb();
        if (nearest == null)
        {
            return;
        }

        float remaining = Mathf.Max(0f, nearest.FuseRemaining);

        // 残りが少ないほど赤くする
        float danger = 1f - Mathf.Clamp01(remaining / Mathf.Max(0.01f, nearest.FuseSeconds));
        GUI.color = Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(1f, 0.2f, 0.15f), danger);

        GUI.Label(new Rect(0f, 26f, width, 30f),
            $"爆発まで {remaining:0.0} 秒！", bigStyle);

        GUI.color = Color.white;
    }

    /// <summary>スタン中は、画面中央に大きく出す。</summary>
    private void DrawStun(float width)
    {
        PlayerStun stun = ResolveStun();

        if (stun == null || !stun.IsStunned)
        {
            return;
        }

        float height = Screen.height / uiScale;

        GUI.color = new Color(1f, 0.3f, 0.25f);
        GUI.Label(new Rect(0f, height * 0.42f, width, 30f),
            $"スタン中！　動けない（あと {stun.Remaining:0.0} 秒）", bigStyle);
        GUI.color = Color.white;
    }

    private void DrawControls(float width)
    {
        if (!showControls)
        {
            return;
        }

        float height = Screen.height / uiScale;

        GUI.color = new Color(1f, 1f, 1f, 0.65f);
        GUI.Label(new Rect(10f, height - 26f, width - 20f, 20f),
            "WASD 移動／マウス 向き／左クリック 長押しでチャージ・離して発射／" +
            "引き寄せ中は枠の中で左クリック（前半＝後ろへ・後半＝前へ）", labelStyle);
        GUI.color = Color.white;
    }

    /// <summary>火のついた爆発物のうち、一番残り時間が短いものを返す。</summary>
    private ExplosiveObject FindMostUrgentBomb()
    {
        ExplosiveObject best = null;

        foreach (ExplosiveObject bomb in ExplosiveObject.LitBombs)
        {
            if (bomb == null || !bomb.FuseLit)
            {
                continue;
            }
            if (best == null || bomb.FuseRemaining < best.FuseRemaining)
            {
                best = bomb;
            }
        }

        return best;
    }
}

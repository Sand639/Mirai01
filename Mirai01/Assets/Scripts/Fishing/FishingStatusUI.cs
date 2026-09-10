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
    [Tooltip("点数を持っている ScoreBoard")]
    [SerializeField] private ScoreBoard scoreBoard;

    [Tooltip("プレイヤーの PlayerStun")]
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

    /// <summary>左上に、プレイヤーごとの点数を出す。</summary>
    private void DrawScore()
    {
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
        if (playerStun == null || !playerStun.IsStunned)
        {
            return;
        }

        float height = Screen.height / uiScale;

        GUI.color = new Color(1f, 0.3f, 0.25f);
        GUI.Label(new Rect(0f, height * 0.42f, width, 30f),
            $"スタン中！　動けない（あと {playerStun.Remaining:0.0} 秒）", bigStyle);
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

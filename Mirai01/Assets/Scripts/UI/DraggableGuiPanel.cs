using UnityEngine;

/// <summary>
/// **`OnGUI` で描く枠を、ドラッグで動かせて・折りたためるようにする部品。**
///
/// 確認用の表示（接続画面・通信の様子・ロビーなど）は `GUILayout.BeginArea` で
/// 位置を決め打ちしていたため、**小さい窓で並べて起動すると枠同士が重なって読めなかった。**
/// そこで、枠を次の形にそろえる。
///
///   ・**上の帯（タイトル）をつかんでドラッグすると動く**
///   ・**帯の右の「－」で折りたためる**（「＋」で開く）
///   ・**窓が小さいと自動で縮む**（1920×1080 を基準に、窓の大きさに比例）
///   ・**高さは中身に合わせる**（空いた部分まで枠が伸びない）
///
/// 使い方（MonoBehaviour の OnGUI の中で）：
/// <code>
/// private readonly DraggableGuiPanel panel = new DraggableGuiPanel("通信の様子", 0f, 12f, 300f);
/// private void OnGUI() { panel.Draw(uiScale, DrawContents); }
/// </code>
///
/// MonoBehaviour ではないので、シーンに置く必要はない。
/// </summary>
public class DraggableGuiPanel
{
    /// <summary>窓ごとに違う番号が要る（IMGUI の決まり）。他と被らないよう大きい数から始める。</summary>
    private static int nextWindowId = 0x4D4930;

    /// <summary>これより小さくは縮めない（文字が読めなくなるため）。</summary>
    private const float MinScale = 0.5f;

    /// <summary>タイトルの帯の高さ。ここをつかんでドラッグする。</summary>
    private const float TitleBarHeight = 20f;

    private readonly int windowId;
    private readonly string title;
    private readonly float anchorX;
    private readonly float offsetX;
    private readonly float defaultY;
    private readonly float width;

    /// <summary>縮める前の座標での位置と大きさ。</summary>
    private Rect rect;

    private bool placed;
    private bool collapsed;
    private GUI.WindowFunction contents;

    /// <summary>折りたたまれているか。</summary>
    public bool Collapsed
    {
        get => collapsed;
        set => collapsed = value;
    }

    /// <param name="title">上の帯に出す名前</param>
    /// <param name="anchorX">最初に出す横位置の基準。0＝左端、1＝右端、0.5＝中央</param>
    /// <param name="offsetX">基準からずらす量（右端基準なら、マイナスで内側へ）</param>
    /// <param name="y">最初に出す縦位置</param>
    /// <param name="width">枠の幅（縮める前の値）</param>
    public DraggableGuiPanel(string title, float anchorX, float offsetX, float y, float width)
    {
        windowId = nextWindowId++;
        this.title = title;
        this.anchorX = anchorX;
        this.offsetX = offsetX;
        defaultY = y;
        this.width = width;
    }

    /// <summary>
    /// 窓の大きさに合わせた縮小率を返す。
    /// **1920×1080 のときに <paramref name="uiScale"/> そのまま**、960×540 ならその半分になる。
    /// </summary>
    public static float FitScale(float uiScale)
    {
        float ratio = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
        return Mathf.Max(MinScale, uiScale * Mathf.Min(1f, ratio));
    }

    /// <summary>
    /// 枠を描く。**OnGUI の中で毎回呼ぶ。**
    /// 中身は <paramref name="drawContents"/> の中で `GUILayout` を使って描く。
    /// </summary>
    public void Draw(float uiScale, GUI.WindowFunction drawContents)
    {
        float scale = FitScale(uiScale);
        float viewWidth = Screen.width / scale;
        float viewHeight = Screen.height / scale;

        if (!placed)
        {
            placed = true;
            rect = new Rect(
                anchorX * (viewWidth - width) + offsetX,
                defaultY,
                width,
                0f);
        }

        contents = drawContents;

        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        // 高さを0に戻してから測り直すと、中身が減ったときに枠も縮む
        // （戻さないと、一度伸びた高さのまま残る）。
        // **測り直すのは Layout のときだけ**にする（描く側で0にすると枠がちらつくため）
        rect.width = width;
        if (Event.current.type == EventType.Layout)
        {
            rect.height = 0f;
        }
        rect = GUILayout.Window(windowId, rect, DrawWindow, title, GUILayout.Width(width));

        // **帯が画面の外へ出てつかめなくならない**ところまで戻す
        // （中身は画面の外へはみ出してもよい。帯さえ見えていれば引き戻せる）
        rect.x = Mathf.Clamp(rect.x, 40f - rect.width, viewWidth - 40f);
        rect.y = Mathf.Clamp(rect.y, 0f, viewHeight - TitleBarHeight);

        GUI.matrix = saved;
    }

    private void DrawWindow(int id)
    {
        // 帯の右端に、折りたたみのボタン
        if (GUI.Button(new Rect(width - 24f, 2f, 20f, TitleBarHeight - 4f), collapsed ? "+" : "-"))
        {
            collapsed = !collapsed;
        }

        if (!collapsed)
        {
            contents?.Invoke(id);
        }

        // 帯の部分だけをつかめるようにする（全体にすると、ボタンや入力欄が押しにくくなる）
        GUI.DragWindow(new Rect(0f, 0f, width - 28f, TitleBarHeight));
    }
}

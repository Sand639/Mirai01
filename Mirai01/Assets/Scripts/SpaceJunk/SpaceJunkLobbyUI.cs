using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **ロビーの画面。** 誰が入っているかを並べ、ホストが端末を開いたら詳細設定を出す。
///
/// つなぐ操作そのものは既存の `LanConnectionUi`（LAN／インターネット／1人）が担当する。
/// このスクリプトは**つながったあと**の「待合室」だけを受け持つ。
///
/// ## ふだん出ているもの
///
/// 参加者の一覧と、いまの設定の要約だけ。**設定そのものは出っぱなしにしない。**
///
/// ## 詳細設定（ホストだけ）
///
/// ロビーに置いた端末（<see cref="SpaceJunkLobbyTerminal"/>）に近づいて `E` を押すと開く。
/// 中でできること：
///
///   ・**チーム数を決める**（1〜4）
///   ・**誰をどのチームに入れるか**を、チームの枠のボタンで移す
///   ・**チーム数を指定してランダムに割り振る**
///   ・**何本先取か**（1〜5。初期値 2先）
///   ・**1ラウンドの最大時間**（初期値 60秒）
///   ・**この試合で使うマップ**をチェックで選ぶ（ラウンドごとにこの中からランダム）
///   ・**ゲーム開始**
///
/// 決めた中身は <see cref="SpaceJunkSession"/> が持ち、全員へ配られる。
///
/// フォントの素材を用意しなくても出せるように `OnGUI` で描いている
/// （既存の `NetworkStatusHud.cs` と同じ作り）。**本番の画面はあとで作り直す前提。**
/// </summary>
public class SpaceJunkLobbyUI : MonoBehaviour
{
    [Header("マップの候補")]
    [Tooltip("名前がこれで始まるシーンを、**マップの候補として自動で並べる**（ビルドの一覧に入っているものだけ）")]
    [SerializeField] private string mapScenePrefix = "SpaceJunkMap";

    [Header("表示")]
    [Tooltip("OFFにすると何も表示しない")]
    [SerializeField] private bool showUi = true;

    [Tooltip("文字の大きさ。**窓が小さいときは自動で縮む**ので、これは上限として使われる")]
    [Range(1f, 4f)]
    [SerializeField] private float uiScale = 1.5f;

    [Tooltip("最初に出す、画面の左からの位置（LanConnectionUi と重ならないようにずらす）")]
    [SerializeField] private float panelX = 366f;

    /// <summary>設定の枠の幅（縮める前の基準）。</summary>
    private const float SettingsWidth = 620f;

    /// <summary>一覧の枠の幅。</summary>
    private const float RosterWidth = 320f;

    /// <summary>「この人を移す」で選んでいる人。<see cref="NoSelection"/> なら誰も選んでいない。</summary>
    private ulong selectedClientId = NoSelection;

    private const ulong NoSelection = ulong.MaxValue;

    private GUIStyle labelStyle;
    private GUIStyle headerStyle;

    private DraggableGuiPanel panel;

    /// <summary>つなぐ画面。試合が始まったら隠すために持っておく。</summary>
    private LanConnectionUi connectionUi;

    /// <summary>「ゲーム開始」を押した結果。うまくいかなかった理由を画面に出すために持つ。</summary>
    private string startMessage = string.Empty;

    private void Awake()
    {
        connectionUi = GetComponent<LanConnectionUi>();
    }

    private void Update()
    {
        // **ラウンドが始まったら、つなぐ画面も隠す。**
        // NetworkManager はシーンをまたいで生き残るため、
        // 隠さないとプレイ画面に「ホストとして動作中／切断する」が出っぱなしになる
        if (connectionUi != null)
        {
            connectionUi.enabled = SpaceJunkRound.Current == null;
        }
    }

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

        // **マップへ移ったあとは、この画面を出さない。**
        if (SpaceJunkRound.Current != null)
        {
            return;
        }

        PrepareStyles();

        if (panel == null)
        {
            panel = new DraggableGuiPanel("ロビー", 0f, panelX, 12f, RosterWidth);
        }

        panel.Draw(uiScale, windowId =>
        {
            DrawRoster();
            GUILayout.Space(6f);
            DrawSummary();
            GUILayout.Space(6f);
            DrawTerminalHint(manager);
        });

        DrawSettingsWindow(manager);
    }

    private void PrepareStyles()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        }

        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        }
    }

    // ------------------------------------------------------------
    // ふだん出ているもの
    // ------------------------------------------------------------

    /// <summary>入っている人を、チームの色つきで並べる。</summary>
    private void DrawRoster()
    {
        SpaceJunkSession session = SpaceJunkSession.Current;

        if (session == null)
        {
            GUILayout.Label("試合の係を待っています…", labelStyle);
            return;
        }

        IReadOnlyList<SpaceJunkPlayerSlot> slots = session.Slots;

        GUILayout.Label($"■ ロビー（{slots.Count} / {SpaceJunkTeams.MaxPlayers} 人）", headerStyle);
        GUILayout.Space(4f);

        foreach (SpaceJunkPlayerSlot slot in slots)
        {
            Color saved = GUI.color;
            GUI.color = SpaceJunkTeams.TeamColor(slot.Team);

            string mark = IsLocalPlayer(slot.ClientId) ? "▶ " : "　 ";
            GUILayout.Label($"{mark}{NameOf(slot.ClientId)}　（{SpaceJunkTeams.TeamName(slot.Team)}）", labelStyle);

            GUI.color = saved;
        }
    }

    /// <summary>いまの設定の要約。参加者にも見える。</summary>
    private void DrawSummary()
    {
        SpaceJunkSession session = SpaceJunkSession.Current;

        if (session == null)
        {
            return;
        }

        GUILayout.Label("■ いまの設定", headerStyle);
        GUILayout.Label($"　チーム数 … {session.TeamCount}", labelStyle);
        GUILayout.Label($"　{session.RoundsToWin} 本先取", labelStyle);
        GUILayout.Label($"　1ラウンド 最大 {Mathf.RoundToInt(session.RoundSeconds)} 秒", labelStyle);
        GUILayout.Label($"　使うマップ … {session.SelectedMaps.Count} 個", labelStyle);
    }

    /// <summary>端末の案内。**ホストが端末のそばにいるときだけ出す。**</summary>
    private void DrawTerminalHint(NetworkManager manager)
    {
        if (!manager.IsServer)
        {
            GUILayout.Label("ホストが始めるのを待っています…", labelStyle);
            return;
        }

        SpaceJunkLobbyTerminal terminal = SpaceJunkLobbyTerminal.Current;

        if (terminal == null)
        {
            GUILayout.Label("設定端末がこのシーンにありません。", labelStyle);
            return;
        }

        if (terminal.IsOpen)
        {
            GUILayout.Label($"設定を開いています（{terminal.InteractKeyName} か Escape で閉じる）", labelStyle);
            return;
        }

        GUILayout.Label(terminal.IsHostNearby
            ? $"**{terminal.InteractKeyName} キー** で詳細設定を開く"
            : "設定端末に近づくと、詳細設定を開けます", labelStyle);
    }

    // ------------------------------------------------------------
    // 詳細設定（ホストだけ）
    // ------------------------------------------------------------

    private void DrawSettingsWindow(NetworkManager manager)
    {
        SpaceJunkLobbyTerminal terminal = SpaceJunkLobbyTerminal.Current;
        SpaceJunkSession session = SpaceJunkSession.Current;

        if (terminal == null || !terminal.IsOpen || session == null || !manager.IsServer)
        {
            return;
        }

        float scale = Mathf.Min(DraggableGuiPanel.FitScale(uiScale), Screen.width / (SettingsWidth + 40f));
        scale = Mathf.Max(0.5f, scale);

        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        float viewWidth = Screen.width / scale;
        float viewHeight = Screen.height / scale;
        Rect area = new Rect((viewWidth - SettingsWidth) * 0.5f, 24f, SettingsWidth, viewHeight - 48f);

        GUILayout.BeginArea(area, GUI.skin.window);

        GUILayout.Label("■ 詳細設定（ホストだけが変えられます）", headerStyle);
        GUILayout.Space(6f);

        DrawStartButton(session);
        GUILayout.Space(8f);

        DrawTeamCount(session);
        GUILayout.Space(6f);

        DrawTeamBoxes(session);
        GUILayout.Space(6f);

        DrawRoundsToWin(session);
        GUILayout.Space(4f);

        DrawRoundSeconds(session);
        GUILayout.Space(6f);

        DrawMapChoice(session);

        GUILayout.EndArea();

        GUI.matrix = saved;
    }

    /// <summary>チーム数を決める。</summary>
    private void DrawTeamCount(SpaceJunkSession session)
    {
        GUILayout.Label("■ チーム数", headerStyle);

        GUILayout.BeginHorizontal();

        for (int i = 1; i <= SpaceJunkTeams.MaxTeams; i++)
        {
            bool isSelected = session.TeamCount == i;

            if (GUILayout.Button(isSelected ? $"● {i}" : $"{i}"))
            {
                session.ServerSetTeamCount(i);
            }
        }

        if (GUILayout.Button("この数でランダムに割り振る"))
        {
            session.ServerRandomAssign();
        }

        GUILayout.EndHorizontal();
    }

    /// <summary>
    /// **チームごとの枠。** 人を選んでから、入れたいチームの「ここへ入れる」を押す。
    /// </summary>
    private void DrawTeamBoxes(SpaceJunkSession session)
    {
        GUILayout.Label("■ チーム分け（人を選んでから、入れたいチームのボタンを押す）", headerStyle);

        // 誰を動かすかを先に選ぶ
        GUILayout.BeginHorizontal();
        foreach (SpaceJunkPlayerSlot slot in session.Slots)
        {
            bool isSelected = selectedClientId == slot.ClientId;

            Color saved = GUI.color;
            GUI.color = SpaceJunkTeams.TeamColor(slot.Team);

            if (GUILayout.Button(isSelected ? $"● {NameOf(slot.ClientId)}" : NameOf(slot.ClientId)))
            {
                selectedClientId = isSelected ? NoSelection : slot.ClientId;
            }

            GUI.color = saved;
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(4f);

        // チームの枠を横に並べる
        GUILayout.BeginHorizontal();

        for (int team = 0; team < session.TeamCount; team++)
        {
            GUILayout.BeginVertical(GUI.skin.box);

            Color saved = GUI.color;
            GUI.color = SpaceJunkTeams.TeamColor(team);
            GUILayout.Label(SpaceJunkTeams.TeamName(team), headerStyle);
            GUI.color = saved;

            int members = 0;
            foreach (SpaceJunkPlayerSlot slot in session.Slots)
            {
                if (slot.Team == team)
                {
                    GUILayout.Label($"　{NameOf(slot.ClientId)}", labelStyle);
                    members++;
                }
            }

            if (members == 0)
            {
                GUILayout.Label("　（いません）", labelStyle);
            }

            GUI.enabled = selectedClientId != NoSelection;
            if (GUILayout.Button("ここへ入れる"))
            {
                session.ServerSetTeamOf(selectedClientId, team);
                selectedClientId = NoSelection;
            }
            GUI.enabled = true;

            GUILayout.EndVertical();
        }

        GUILayout.EndHorizontal();

        GUILayout.Label("　※ 人数の偏り（3人対1人など）も許しています。ホストの判断で決めてください。", labelStyle);
    }

    /// <summary>何本先取かを決める。</summary>
    private void DrawRoundsToWin(SpaceJunkSession session)
    {
        GUILayout.Label("■ 何本先取か", headerStyle);

        GUILayout.BeginHorizontal();

        for (int i = 1; i <= 5; i++)
        {
            bool isSelected = session.RoundsToWin == i;

            if (GUILayout.Button(isSelected ? $"● {i} 本先取" : $"{i} 本先取"))
            {
                session.ServerSetRoundsToWin(i);
            }
        }

        GUILayout.EndHorizontal();
    }

    /// <summary>1ラウンドの最大時間を決める。</summary>
    private void DrawRoundSeconds(SpaceJunkSession session)
    {
        GUILayout.Label("■ 1ラウンドの最大時間", headerStyle);

        int[] choices = { 30, 45, 60, 90, 120 };

        GUILayout.BeginHorizontal();

        foreach (int seconds in choices)
        {
            bool isSelected = Mathf.RoundToInt(session.RoundSeconds) == seconds;

            if (GUILayout.Button(isSelected ? $"● {seconds} 秒" : $"{seconds} 秒"))
            {
                session.ServerSetRoundSeconds(seconds);
            }
        }

        GUILayout.EndHorizontal();

        GUILayout.Label("　※ 3種類そろえたチームが出たら、その時点でラウンドは終わります。", labelStyle);
    }

    /// <summary>この試合で使うマップを選ぶ。**ラウンドごとに、この中からランダムに選ばれる。**</summary>
    private void DrawMapChoice(SpaceJunkSession session)
    {
        GUILayout.Label("■ 使うマップ（この中からラウンドごとにランダム）", headerStyle);

        List<string> maps = GetAvailableMaps();

        if (maps.Count == 0)
        {
            GUI.color = new Color(1f, 0.5f, 0.4f);
            GUILayout.Label(
                $"「{mapScenePrefix}〜」という名前のシーンが、ビルドの一覧に入っていません。\n" +
                "File > Build Profiles を開いて、遊びたいマップのシーンを入れてください。", labelStyle);
            GUI.color = Color.white;
            return;
        }

        foreach (string map in maps)
        {
            bool isSelected = session.IsMapSelected(map);

            if (GUILayout.Button(isSelected ? $"☑ {map}" : $"☐ {map}"))
            {
                session.ServerToggleMap(map);
            }
        }

        if (session.SelectedMaps.Count > 1)
        {
            GUILayout.Label("　※ 直前と同じマップは選ばれません。", labelStyle);
        }
    }

    /// <summary>ゲーム開始。**押せるのはホストだけ。**</summary>
    private void DrawStartButton(SpaceJunkSession session)
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (!manager.NetworkConfig.EnableSceneManagement)
        {
            GUI.color = new Color(1f, 0.5f, 0.4f);
            GUILayout.Label(
                "NetworkManager の Enable Scene Management が OFF です。" +
                "ONにしないとシーンを切り替えられません。", labelStyle);
            GUI.color = Color.white;
            return;
        }

        if (session.SelectedMaps.Count == 0)
        {
            GUI.color = new Color(1f, 0.5f, 0.4f);
            GUILayout.Label("使うマップを1つ以上選んでください。", labelStyle);
            GUI.color = Color.white;
            return;
        }

        // **一番多い原因を先に出す。** ビルドの一覧に入っていないシーンは読み込めない
        foreach (string map in session.SelectedMaps)
        {
            if (!IsSceneInBuildList(map))
            {
                GUI.color = new Color(1f, 0.5f, 0.4f);
                GUILayout.Label(
                    $"シーン「{map}」がビルドの一覧に入っていません。\n" +
                    "File > Build Profiles を開いて入れてください。", labelStyle);
                GUI.color = Color.white;
                return;
            }
        }

        if (GUILayout.Button($"ゲーム開始（{session.RoundsToWin} 本先取）"))
        {
            session.ServerStartMatch();
            startMessage = string.Empty;
        }

        if (!string.IsNullOrEmpty(startMessage))
        {
            GUI.color = new Color(1f, 0.5f, 0.4f);
            GUILayout.Label(startMessage, labelStyle);
            GUI.color = Color.white;
        }
    }

    // ------------------------------------------------------------
    // 細かい道具
    // ------------------------------------------------------------

    /// <summary>その接続番号の人の、画面に出す名前。</summary>
    private static string NameOf(ulong clientId)
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.OwnerClientId == clientId)
            {
                return player.DisplayName;
            }
        }

        return $"参加者 {clientId}";
    }

    /// <summary>その人が、このPCで操作している人か。</summary>
    private static bool IsLocalPlayer(ulong clientId)
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == clientId;
    }

    /// <summary>ビルドの一覧から、マップの候補を拾う。</summary>
    private List<string> GetAvailableMaps()
    {
        List<string> maps = new List<string>();
        int count = UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings;

        for (int i = 0; i < count; i++)
        {
            string path = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i);
            string name = Path.GetFileNameWithoutExtension(path);

            if (!string.IsNullOrEmpty(mapScenePrefix) && name.StartsWith(mapScenePrefix) && !maps.Contains(name))
            {
                maps.Add(name);
            }
        }

        return maps;
    }

    /// <summary>そのシーンが**ビルドのシーン一覧に入っているか**。入っていないと切り替えられない。</summary>
    private static bool IsSceneInBuildList(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            return false;
        }

        int count = UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings;

        for (int i = 0; i < count; i++)
        {
            string path = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i);

            if (Path.GetFileNameWithoutExtension(path) == sceneName)
            {
                return true;
            }
        }

        return false;
    }
}

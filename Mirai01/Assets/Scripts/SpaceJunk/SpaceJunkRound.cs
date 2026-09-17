using Unity.Netcode;
using UnityEngine;

/// <summary>ラウンドがいまどの段階か。</summary>
public enum SpaceJunkRoundPhase
{
    /// <summary>集めている最中</summary>
    Playing,

    /// <summary>決着がついて結果を出している</summary>
    Result
}

/// <summary>
/// **1ラウンドぶんの進行役。** マップ（ステージ）のシーンに1つ置く。
///
/// ## ラウンドの決まり（2026/9/17・大槻さん）
///
/// - **自陣のゴールに素材3種類を1個ずつ入れたチームが、その場でラウンド勝利**
/// - 誰もそろえられないまま最大時間に達したら、**集めた種類が多いチームがラウンドを取る**
/// - **1位が同数（引き分け）なら、そのラウンドは誰も取らずに次へ行く**
/// - 同じ種類を2個目入れても**何も起きない**（消えるだけ）
///
/// ## 誰が判定するか
///
/// **ホストだけ。** 参加者のPCは結果を受け取って出すだけで、一切数えていない
/// （それぞれが数えると、同じ素材で何度も数えてしまう）。
///
/// ## 勝ち数はここに持たない
///
/// ラウンドの勝ち数と設定は <see cref="SpaceJunkSession"/> が持っている。
/// こちらは**シーンと一緒に消える**ので、覚えておきたいものは置かない。
/// </summary>
public class SpaceJunkRound : NetworkBehaviour
{
    /// <summary>いま動いているラウンド。ゴールや画面表示から探すために持っている。</summary>
    public static SpaceJunkRound Current { get; private set; }

    /// <summary>
    /// いま動いてよいか。**結果が出たあとは動かせなくする**ために使う。
    /// ラウンドの進行役がいないシーン（釣りなど）では常に true。
    /// </summary>
    public static bool PlayAllowed => Current == null || Current.IsPlaying;

    [Header("最大時間（係がいないときの予備）")]
    [Tooltip("SpaceJunkSession が見つからないときに使う秒数。" +
             "**ふだんはホストがロビーで決めた値が使われる**ので、ここは効かない")]
    [SerializeField] private float fallbackSeconds = 60f;

    /// <summary>
    /// チームごとの、集めた素材の種類。**1ビットが1種類**（装甲板=1 / 回路基板=2 / 燃料タンク=4）。
    /// 長さは常に <see cref="SpaceJunkTeams.MaxTeams"/>。
    /// </summary>
    private readonly NetworkList<int> collected = new NetworkList<int>();

    /// <summary>いまの段階。**ホストが決めて全員に配る。**</summary>
    private readonly NetworkVariable<SpaceJunkRoundPhase> phase =
        new NetworkVariable<SpaceJunkRoundPhase>(SpaceJunkRoundPhase.Playing);

    /// <summary>
    /// ラウンドが終わる時刻。**全員で同期された時計（ServerTime）で持つ**ので、
    /// どのPCでも同じ残り時間が出る。
    /// </summary>
    private readonly NetworkVariable<double> endServerTime = new NetworkVariable<double>(0d);

    /// <summary>このラウンドを取ったチーム。-1 なら引き分け。</summary>
    private readonly NetworkVariable<int> roundWinner = new NetworkVariable<int>(-1);

    /// <summary>集めている最中か。</summary>
    public bool IsPlaying => phase.Value == SpaceJunkRoundPhase.Playing;

    /// <summary>いまの段階。</summary>
    public SpaceJunkRoundPhase Phase => phase.Value;

    /// <summary>このラウンドを取ったチーム。-1 なら引き分け。</summary>
    public int RoundWinner => roundWinner.Value;

    /// <summary>残り秒数。どのPCでも同じ値になる。</summary>
    public float RemainingSeconds
    {
        get
        {
            if (NetworkManager == null)
            {
                return 0f;
            }

            return Mathf.Max(0f, (float)(endServerTime.Value - NetworkManager.ServerTime.Time));
        }
    }

    /// <summary>そのチームが、その種類をもう持っているか。</summary>
    public bool HasKind(int team, SpaceJunkMaterialKind kind)
    {
        if (team < 0 || team >= collected.Count)
        {
            return false;
        }

        return (collected[team] & KindBit(kind)) != 0;
    }

    /// <summary>そのチームが集めた種類の数（0〜3）。</summary>
    public int KindCountOf(int team)
    {
        if (team < 0 || team >= collected.Count)
        {
            return 0;
        }

        int mask = collected[team];
        int count = 0;

        for (int i = 0; i < SpaceJunkMaterials.Count; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>種類をビットに直す。</summary>
    private static int KindBit(SpaceJunkMaterialKind kind)
    {
        return 1 << (int)kind;
    }

    /// <summary>3種類そろった状態のビット（0b111）。</summary>
    private static int FullMask
    {
        get
        {
            int mask = 0;
            for (int i = 0; i < SpaceJunkMaterials.Count; i++)
            {
                mask |= 1 << i;
            }
            return mask;
        }
    }

    // ------------------------------------------------------------
    // 出入り
    // ------------------------------------------------------------

    public override void OnNetworkSpawn()
    {
        Current = this;

        if (IsServer)
        {
            collected.Clear();
            for (int i = 0; i < SpaceJunkTeams.MaxTeams; i++)
            {
                collected.Add(0);
            }

            phase.Value = SpaceJunkRoundPhase.Playing;
            roundWinner.Value = -1;

            float seconds = SpaceJunkSession.Current != null
                ? SpaceJunkSession.Current.RoundSeconds
                : fallbackSeconds;

            endServerTime.Value = NetworkManager.ServerTime.Time + seconds;
        }

        phase.OnValueChanged += OnPhaseChanged;

        // **前のラウンドで止めた操作を、ここで戻す。**
        // プレイヤーはシーンをまたいで生き続けるので、止めたままだと次のラウンドで動けなくなる
        SetLocalPlayerControlEnabled(true);

        ApplyGoalOwners();
    }

    public override void OnNetworkDespawn()
    {
        phase.OnValueChanged -= OnPhaseChanged;

        if (Current == this)
        {
            Current = null;
        }
    }

    private void OnPhaseChanged(SpaceJunkRoundPhase before, SpaceJunkRoundPhase after)
    {
        // 結果が出たら動けなくする（結果を見る時間になる）
        SetLocalPlayerControlEnabled(after == SpaceJunkRoundPhase.Playing);
    }

    private void Update()
    {
        if (!IsServer || phase.Value != SpaceJunkRoundPhase.Playing)
        {
            return;
        }

        // 最大時間に達した
        if (NetworkManager.ServerTime.Time >= endServerTime.Value)
        {
            FinishByCount();
        }
    }

    // ------------------------------------------------------------
    // 素材を受け取る（**ホストだけが呼ぶ**）
    // ------------------------------------------------------------

    /// <summary>
    /// **ゴールに素材が入ったことを受け取る。** <see cref="SpaceJunkGoal"/> から呼ばれる。
    ///
    /// すでに持っている種類だったときは何もしない（素材は消えるだけ）。
    /// </summary>
    /// <returns>新しい種類として数えたら true。</returns>
    public bool ServerCollect(int team, SpaceJunkMaterialKind kind)
    {
        if (!IsServer || phase.Value != SpaceJunkRoundPhase.Playing)
        {
            return false;
        }

        if (team < 0 || team >= collected.Count)
        {
            return false;
        }

        int bit = KindBit(kind);

        if ((collected[team] & bit) != 0)
        {
            // もう持っている種類。**何も起きない**
            return false;
        }

        collected[team] = collected[team] | bit;

        Debug.Log($"[JUNK] {SpaceJunkTeams.TeamName(team)} が {SpaceJunkMaterials.Name(kind)} を集めました" +
                  $"（{KindCountOf(team)} / {SpaceJunkMaterials.Count} 種類）。");

        // 3種類そろったら、その場でラウンド勝利
        if (collected[team] == FullMask)
        {
            FinishRound(team);
        }

        return true;
    }

    // ------------------------------------------------------------
    // 決着（**ホストだけが判定する**）
    // ------------------------------------------------------------

    /// <summary>
    /// 最大時間に達したときの決着。**集めた種類が多いチームがラウンドを取る。**
    /// 1位が同数なら引き分けで、誰も取らない。
    /// </summary>
    private void FinishByCount()
    {
        int bestTeam = -1;
        int bestCount = -1;
        bool tied = false;

        int teams = SpaceJunkSession.Current != null
            ? SpaceJunkSession.Current.TeamCount
            : SpaceJunkTeams.MaxTeams;

        for (int team = 0; team < teams; team++)
        {
            int count = KindCountOf(team);

            if (count > bestCount)
            {
                bestCount = count;
                bestTeam = team;
                tied = false;
            }
            else if (count == bestCount)
            {
                tied = true;
            }
        }

        // 1位が同数なら引き分け。**誰も集めていない（全員0種類）ときも引き分け**
        FinishRound(tied || bestCount <= 0 ? -1 : bestTeam);
    }

    /// <summary>ラウンドを終わらせて、試合の係へ結果を渡す。</summary>
    private void FinishRound(int winnerTeam)
    {
        if (phase.Value != SpaceJunkRoundPhase.Playing)
        {
            return;
        }

        roundWinner.Value = winnerTeam;
        phase.Value = SpaceJunkRoundPhase.Result;

        Debug.Log(winnerTeam < 0
            ? "[JUNK] ラウンド終了。引き分け（誰もラウンドを取りません）。"
            : $"[JUNK] ラウンド終了。{SpaceJunkTeams.TeamName(winnerTeam)} がラウンドを取りました。");

        if (SpaceJunkSession.Current != null)
        {
            SpaceJunkSession.Current.ServerReportRoundResult(winnerTeam);
        }
        else
        {
            Debug.LogWarning("[JUNK] 試合の係（SpaceJunkSession）がいないので、次のラウンドへ進めません。");
        }
    }

    // ------------------------------------------------------------
    // ゴールの持ち主と、操作の止め方
    // ------------------------------------------------------------

    /// <summary>
    /// **そのゴールが、どのチームのものか。**
    ///
    /// 全員が同じ計算（チーム数 → 割り当て）をするので、通信で送る必要はない。
    /// ゴールの側からも呼べるようにしてある（**生まれる順番がどちらでも取りこぼさないため**）。
    /// </summary>
    public int OwnerTeamOfGoal(int goalIndex)
    {
        int teamCount = SpaceJunkSession.Current != null ? SpaceJunkSession.Current.TeamCount : 1;
        int[] owners = SpaceJunkTeams.GoalOwners(teamCount);

        return owners[Mathf.Clamp(goalIndex, 0, owners.Length - 1)];
    }

    /// <summary>シーンにあるゴールへ、どのチームのものかをまとめて配る。</summary>
    private void ApplyGoalOwners()
    {
        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal != null)
            {
                goal.ApplyOwnerTeam(OwnerTeamOfGoal(goal.GoalIndex));
            }
        }
    }

    /// <summary>
    /// **このPCで操作しているプレイヤーの、移動とフックを止める／戻す。**
    ///
    /// 釣りでは <c>FishingMatch.PlayAllowed</c> がこの役目をしているが、
    /// 宇宙ごみのシーンには <c>FishingMatch</c> を置かない（釣りの時間制ルールが動いてしまうため）。
    /// **釣り側のファイルを一切変えずに済ませるために、ここで直接止めている。**
    /// </summary>
    private void SetLocalPlayerControlEnabled(bool enabledState)
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player == null || !player.IsOwner)
            {
                continue;
            }

            FishingPlayerController move = player.GetComponent<FishingPlayerController>();
            if (move != null)
            {
                move.enabled = enabledState;
            }

            HookController hook = player.GetComponent<HookController>();
            if (hook != null)
            {
                hook.enabled = enabledState;
            }
        }
    }
}

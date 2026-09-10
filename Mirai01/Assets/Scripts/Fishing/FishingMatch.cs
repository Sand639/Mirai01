using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **オンラインの試合のまとめ役。** 釣りのオンラインシーンに1つ置く。
///
/// ホストだけが中身を決めて、結果を全員へ配る
/// （`ネットワークの制作工程.md` の「動きは本人、勝敗と点数はホスト」の形）。
///
/// 持っているもの：
///   ・**チーム数**（人数から自動で決まる。<see cref="FishingTeams"/> のルール）
///   ・**チームごとの点数**
///   ・**4つのゴールを、どのチームのものにするか**（人数が変わると割り当て直す）
///
/// 点を足すのは <see cref="FishingNetPocket"/> から。表示は <see cref="FishingStatusUI"/>。
/// </summary>
public class FishingMatch : NetworkBehaviour
{
    /// <summary>シーンにある試合のまとめ役。ゴールや表示から探すために持っている。</summary>
    public static FishingMatch Current { get; private set; }

    /// <summary>いま何チームに分かれているか。**ホストが決めて全員に配る。**</summary>
    private readonly NetworkVariable<int> teamCount = new NetworkVariable<int>(1);

    /// <summary>
    /// チームごとの点数。**書き換えられるのはホストだけ**（初期設定のまま）。
    /// 長さは常に <see cref="FishingTeams.MaxTeams"/>。
    /// </summary>
    private readonly NetworkList<int> teamScores = new NetworkList<int>();

    /// <summary>
    /// 4つのゴールの持ち主。中身はチーム番号か <see cref="FishingTeams.NeutralTeam"/>。
    /// 並び順はゴールの Pocket Index に対応する。
    /// </summary>
    private readonly NetworkList<int> pocketOwners = new NetworkList<int>();

    /// <summary>直前に起きたこと（「北のゴールに 青チーム +1」など）。表示用。</summary>
    private readonly NetworkVariable<FixedString128Bytes> lastEvent =
        new NetworkVariable<FixedString128Bytes>();

    /// <summary>いま何チームに分かれているか。</summary>
    public int TeamCount => teamCount.Value;

    /// <summary>直前に起きたことの文章。</summary>
    public string LastEvent => lastEvent.Value.ToString();

    /// <summary>直前のことが起きた時刻（このPCの時計）。少しの間だけ表示するために使う。</summary>
    public float LastEventTime { get; private set; } = -999f;

    private int lastKnownPlayerCount = -1;

    public override void OnNetworkSpawn()
    {
        Current = this;

        if (IsServer)
        {
            // 点数の枠をチームの最大数ぶん用意する（0で埋める）
            teamScores.Clear();
            for (int i = 0; i < FishingTeams.MaxTeams; i++)
            {
                teamScores.Add(0);
            }

            pocketOwners.Clear();
            for (int i = 0; i < FishingTeams.PocketCount; i++)
            {
                pocketOwners.Add(0);
            }

            RebuildTeams();
        }

        lastEvent.OnValueChanged += OnLastEventChanged;
        pocketOwners.OnListChanged += OnPocketOwnersChanged;

        ApplyPocketOwnersToScene();
    }

    public override void OnNetworkDespawn()
    {
        lastEvent.OnValueChanged -= OnLastEventChanged;
        pocketOwners.OnListChanged -= OnPocketOwnersChanged;

        if (Current == this)
        {
            Current = null;
        }
    }

    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        // 人が入ったり抜けたりしたら、チーム数とゴールの割り当てを作り直す
        int playerCount = FishingNetPlayer.All.Count;
        if (playerCount != lastKnownPlayerCount)
        {
            RebuildTeams();
        }
    }

    // ------------------------------------------------------------
    // チーム数とゴールの割り当て（ホストだけが行う）
    // ------------------------------------------------------------

    /// <summary>いまの人数から、チーム数とゴールの持ち主を決め直す。</summary>
    private void RebuildTeams()
    {
        int playerCount = FishingNetPlayer.All.Count;
        lastKnownPlayerCount = playerCount;

        int teams = FishingTeams.TeamCountFor(playerCount);
        teamCount.Value = teams;

        int[] owners = FishingTeams.PocketOwners(teams);
        for (int i = 0; i < pocketOwners.Count && i < owners.Length; i++)
        {
            pocketOwners[i] = owners[i];
        }

        Debug.Log($"[FISH] 人数 {playerCount} 人 → {teams} チーム。" +
                  $"ゴールの持ち主：{string.Join(" / ", owners)}");
    }

    private void OnPocketOwnersChanged(NetworkListEvent<int> changeEvent)
    {
        ApplyPocketOwnersToScene();
    }

    /// <summary>ゴールの持ち主を、シーンにあるゴールへ伝える（色を変えるため）。</summary>
    private void ApplyPocketOwnersToScene()
    {
        foreach (FishingNetPocket pocket in FishingNetPocket.All)
        {
            pocket.ApplyOwnerTeam(OwnerTeamOfPocket(pocket.PocketIndex));
        }
    }

    /// <summary>そのゴールを持っているチーム。共通ゴールなら <see cref="FishingTeams.NeutralTeam"/>。</summary>
    public int OwnerTeamOfPocket(int pocketIndex)
    {
        if (pocketIndex < 0 || pocketIndex >= pocketOwners.Count)
        {
            return FishingTeams.NeutralTeam;
        }
        return pocketOwners[pocketIndex];
    }

    // ------------------------------------------------------------
    // 点数
    // ------------------------------------------------------------

    /// <summary>チームの点数。</summary>
    public int ScoreOf(int team)
    {
        if (team < 0 || team >= teamScores.Count)
        {
            return 0;
        }
        return teamScores[team];
    }

    /// <summary>
    /// 点を足す。**ホストだけが呼べる。**
    /// <see cref="FishingNetPocket"/> のホスト側の判定から呼ばれる。
    /// </summary>
    public void AddTeamScore(int team, int amount, string where)
    {
        if (!IsServer || team < 0 || team >= teamScores.Count)
        {
            return;
        }

        teamScores[team] += amount;
        lastEvent.Value = $"{where} のゴールに投入！　{FishingTeams.TeamName(team)} +{amount}";
        LastEventTime = Time.time;
    }

    private void OnLastEventChanged(FixedString128Bytes before, FixedString128Bytes after)
    {
        // 参加者側でも「いま入った」と分かるように、受け取った時刻を控える
        LastEventTime = Time.time;
    }
}

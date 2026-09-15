using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **オンラインのゴール（ポケット）1つ分。**
///
/// 判定するのは**ホストだけ**。参加者のPCでは何も数えない
/// （それぞれが数えると、同じ物資で何度も点が入ってしまう）。
///
/// ## 誰の点になるか（<see cref="FishingTeams"/> のルール）
///
/// | このゴールの種類 | 点が入るチーム |
/// | --- | --- |
/// | **チームのゴール** | **そのゴールを持つチーム**（誰が投げ入れても） |
/// | **共通ゴール** | **投げ入れた人のチーム** |
///
/// 持ち主は人数によって変わるので、<see cref="FishingMatch"/> から
/// <see cref="ApplyOwnerTeam"/> で伝えられる。色もそれに合わせて変わる。
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(NetworkObject))]
public class FishingNetPocket : NetworkBehaviour
{
    /// <summary>シーンにあるゴール全部。<see cref="FishingMatch"/> が持ち主を配るために使う。</summary>
    public static readonly List<FishingNetPocket> All = new List<FishingNetPocket>();

    [Header("このゴールの場所")]
    [Tooltip("ゴールの並び順の番号（0=北 / 1=東 / 2=南 / 3=西）。持ち主の割り当てに使う")]
    [SerializeField] private int pocketIndex = 0;

    [Header("見た目")]
    [Tooltip("チームの色に塗る床の Renderer")]
    [SerializeField] private Renderer padRenderer;

    [Tooltip("物資が入ったときに一瞬光らせる色")]
    [SerializeField] private Color flashColor = Color.white;

    [Tooltip("光っている秒数")]
    [SerializeField] private float flashSeconds = 0.4f;

    /// <summary>ゴールの並び順の番号。</summary>
    public int PocketIndex => pocketIndex;

    /// <summary>このゴールを持っているチーム。共通ゴールなら <see cref="FishingTeams.NeutralTeam"/>。</summary>
    public int OwnerTeam { get; private set; } = FishingTeams.NeutralTeam;

    private Color teamColor = Color.white;
    private float flashUntil = -999f;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnEnable()
    {
        if (!All.Contains(this))
        {
            All.Add(this);
        }

        // すでに試合が始まっていれば、その時点の持ち主をもらう
        if (FishingMatch.Current != null)
        {
            ApplyOwnerTeam(FishingMatch.Current.OwnerTeamOfPocket(pocketIndex));
        }
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    /// <summary>持ち主のチームを受け取り、床の色を変える。<see cref="FishingMatch"/> から呼ばれる。</summary>
    public void ApplyOwnerTeam(int team)
    {
        OwnerTeam = team;
        teamColor = FishingTeams.TeamColor(team);

        if (padRenderer != null)
        {
            padRenderer.material.color = teamColor;
        }
    }

    private void Update()
    {
        if (padRenderer == null)
        {
            return;
        }

        bool lit = Time.time < flashUntil;
        padRenderer.material.color = lit ? flashColor : teamColor;
    }

    private void OnTriggerEnter(Collider other)
    {
        // **数えるのはホストだけ。**
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        HookableObject supply = other.GetComponentInParent<HookableObject>();

        if (supply == null || supply.IsHooked || supply.IsVanished || supply.ScoreValue <= 0)
        {
            return;
        }

        FishingNetSupply netSupply = supply.GetComponent<FishingNetSupply>();

        // 引っ掛けたまま持ち込まれたものは数えない（引きずり込みで稼げないようにするため）
        if (netSupply != null && netSupply.IsClaimed)
        {
            return;
        }

        int scoringTeam = DecideScoringTeam(netSupply);

        if (scoringTeam >= 0 && FishingMatch.Current != null)
        {
            FishingMatch.Current.AddTeamScore(
                scoringTeam, supply.ScoreValue, FishingTeams.PocketPlaceName(pocketIndex));
        }

        netSupply?.ClearLastThrower();

        FlashClientRpc();
        supply.Vanish();
    }

    /// <summary>
    /// どのチームに点を入れるかを決める。
    ///
    /// ・チームのゴール … **そのゴールを持つチーム**
    /// ・共通ゴール … **投げ入れた人のチーム**（分からなければ点を入れない）
    /// </summary>
    private int DecideScoringTeam(FishingNetSupply netSupply)
    {
        if (OwnerTeam != FishingTeams.NeutralTeam)
        {
            return OwnerTeam;
        }

        if (netSupply == null || netSupply.LastThrownByPlayerIndex < 0)
        {
            return -1;
        }

        return FishingTeams.TeamOf(netSupply.LastThrownByPlayerIndex);
    }

    /// <summary>全員の画面で床を一瞬光らせる。</summary>
    [ClientRpc]
    private void FlashClientRpc()
    {
        flashUntil = Time.time + flashSeconds;
    }
}

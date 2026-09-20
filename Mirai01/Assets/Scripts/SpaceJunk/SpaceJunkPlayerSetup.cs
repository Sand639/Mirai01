using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **宇宙ごみ用に、プレイヤーへ上乗せする部品。**
///
/// フックの操作そのものは釣りの部品（<c>FishingNetPlayer</c> / <c>HookController</c> など）を
/// **そのまま共用している。** ただし、そのままでは次の2つが合わない。
///
/// ### ① マップに着いても、位置とカメラが合わない
///
/// <c>FishingNetPlayer</c> は「釣り会場に着いたか」を **<c>FishingMatch</c> がいるかどうか**で判断している。
/// 宇宙ごみのマップには <c>FishingMatch</c> を置かない（釣りの時間制ルールが動いてしまうため）ので、
/// **着いたことに気づかず、ロビーの位置のまま・カメラも追ってこない。**
/// → ここで <see cref="SpaceJunkRound"/> が現れたかどうかで同じことをやり直す。
///
/// ### ② チームの色が食い違う
///
/// <c>FishingNetPlayer</c> は**参加した順に2人ずつ**でチームの色を塗る。
/// 宇宙ごみは**ホストが自由にチームを決める**ので、そのままでは色が合わない。
/// → ここで <see cref="SpaceJunkSession"/> が持っているチームの色に塗り直す。
///
/// ### 出てくる場所
///
/// **自分のチームのゴールの近く**に出る。見つからなければ、接続番号ごとに円周上へ散らす。
///
/// 使い方：宇宙ごみ用のプレイヤーのプレハブに、釣りの部品と一緒に付ける。
/// **釣りのプレハブには付けないこと**（釣りの動きが変わってしまう）。
/// </summary>
public class SpaceJunkPlayerSetup : MonoBehaviour
{
    [Header("見た目")]
    [Tooltip("チームの色に塗る見た目。**釣りのプレハブと同じものを入れる**")]
    [SerializeField] private Renderer[] teamRenderers;

    [Header("出てくる場所")]
    [Tooltip("自分のゴールから、ステージの中心へ向かって何m離れた場所に出るか")]
    [SerializeField] private float spawnInset = 4f;

    [Tooltip("重ならないように、どれだけばらけさせるか（メートル）")]
    [SerializeField] private float spawnScatter = 1.5f;

    [Tooltip("ゴールが見つからないときに使う、中心からの距離")]
    [SerializeField] private float fallbackRadius = 6f;

    private FishingNetPlayer netPlayer;
    private CharacterController characterController;

    /// <summary>マップに着いて、場所とカメラを合わせ終わったか。</summary>
    private bool placedInRound;

    /// <summary>直前に塗ったチーム。変わったときだけ塗り直すために持つ。</summary>
    private int lastAppliedTeam = -999;

    private void Awake()
    {
        netPlayer = GetComponent<FishingNetPlayer>();
        characterController = GetComponent<CharacterController>();
    }

    private void Update()
    {
        // チームの色は、**自分のぶんも他の人のぶんも**塗る（誰が味方か分かるように）
        ApplyTeamColor();

        if (netPlayer == null || !netPlayer.IsOwner)
        {
            return;
        }

        bool inRound = SpaceJunkRound.Current != null;

        if (inRound && !placedInRound)
        {
            placedInRound = true;
            MoveToSpawnPoint();
            FollowWithCamera();
        }
        else if (!inRound && placedInRound)
        {
            // ロビーへ戻ったとき（次にまたマップへ入れるようにしておく）
            placedInRound = false;
        }
    }

    /// <summary>このプレイヤーのチーム。係がいなければ 0。</summary>
    private int MyTeam()
    {
        if (SpaceJunkSession.Current == null)
        {
            return 0;
        }

        return SpaceJunkSession.Current.TeamOf(netPlayer != null ? netPlayer.OwnerClientId : 0UL);
    }

    /// <summary>ホストが決めたチームの色に塗り直す。</summary>
    private void ApplyTeamColor()
    {
        if (teamRenderers == null || SpaceJunkSession.Current == null)
        {
            return;
        }

        int team = MyTeam();

        if (team == lastAppliedTeam)
        {
            return;
        }

        lastAppliedTeam = team;
        Color color = SpaceJunkTeams.TeamColor(team);

        foreach (Renderer renderer in teamRenderers)
        {
            if (renderer != null)
            {
                renderer.material.color = color;
            }
        }
    }

    /// <summary>**自分のチームのゴールの近く**へ移す。見つからなければ円周上へ散らす。</summary>
    private void MoveToSpawnPoint()
    {
        Vector3 position = FindSpawnPosition();
        Quaternion rotation = Quaternion.LookRotation(
            new Vector3(-position.x, 0f, -position.z).sqrMagnitude > 0.01f
                ? new Vector3(-position.x, 0f, -position.z)
                : Vector3.forward);

        // CharacterController が付いたまま動かすと位置が戻されるので、いったん切る
        if (characterController != null)
        {
            characterController.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            characterController.enabled = true;
        }
        else
        {
            transform.SetPositionAndRotation(position, rotation);
        }
    }

    private Vector3 FindSpawnPosition()
    {
        int team = MyTeam();
        Vector2 scatter = Random.insideUnitCircle * spawnScatter;

        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal == null || goal.OwnerTeam != team)
            {
                continue;
            }

            // ゴールから、ステージの中心へ向かって少し内側
            Vector3 goalPosition = goal.transform.position;
            Vector3 toCenter = new Vector3(-goalPosition.x, 0f, -goalPosition.z);

            if (toCenter.sqrMagnitude < 0.01f)
            {
                toCenter = Vector3.forward;
            }

            Vector3 spot = goalPosition + toCenter.normalized * spawnInset;
            return new Vector3(spot.x + scatter.x, goalPosition.y, spot.z + scatter.y);
        }

        // ゴールが見つからないとき（まだ持ち主が配られていないなど）
        ulong id = netPlayer != null ? netPlayer.OwnerClientId : 0UL;
        float angle = (id % (ulong)SpaceJunkTeams.MaxPlayers) * (360f / SpaceJunkTeams.MaxPlayers);
        Vector3 fallback = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * fallbackRadius);

        return new Vector3(fallback.x + scatter.x, 0f, fallback.z + scatter.y);
    }

    /// <summary>
    /// **シーンに置いてあるもの（カメラ・ゲージ）と自分を結びつける。**
    /// プレイヤーはプレハブから生まれるので、シーンの中身をあらかじめ入れておけない。
    /// </summary>
    private void FollowWithCamera()
    {
        TopDownCameraFollow follow = FindFirstObjectByType<TopDownCameraFollow>();
        if (follow != null)
        {
            follow.SetTarget(transform);
        }

        PlayerAimController aim = GetComponent<PlayerAimController>();
        if (aim != null && Camera.main != null)
        {
            aim.SetCamera(Camera.main);
        }

        HookController hook = GetComponent<HookController>();
        if (hook != null)
        {
            HookChargeUI ui = FindFirstObjectByType<HookChargeUI>();
            if (ui != null)
            {
                hook.SetUI(ui);
            }
        }
    }
}

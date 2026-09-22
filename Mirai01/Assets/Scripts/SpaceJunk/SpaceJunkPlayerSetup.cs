using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

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

    [Header("落ちたときの立て直し")]
    [Tooltip("押すと、その場から**出てくる場所へ戻る**キー。落ちて戻れなくなったとき用")]
    [SerializeField] private Key resetKey = Key.R;

    [Tooltip("同じことをする、コントローラーのボタン（North＝Xbox の Y）")]
    [SerializeField] private GamepadButton resetButton = GamepadButton.North;

    [Tooltip("この高さより下まで落ちたら、**自動で出てくる場所へ戻す**（メートル）")]
    [SerializeField] private float fallResetHeight = -8f;

    [Header("レーダー（自分のぶんにだけ付く）")]
    [Tooltip("レーダーの外枠の画像（Assets/Art/Sprites/Radar）")]
    [SerializeField] private Sprite radarFrame;

    [Tooltip("レーダーの方角（N/E/S/W）の輪の画像。自分の向きに合わせて回る")]
    [SerializeField] private Sprite radarCompass;

    [Tooltip("レーダーの真ん中に出す自分の印の画像")]
    [SerializeField] private Sprite radarSelf;

    /// <summary>
    /// このPCで操作している人の「戻る」キーの名前。画面の案内に使う。
    /// 自分のぶんが動き出すまでは空。
    /// </summary>
    public static string LocalResetKeyName { get; private set; } = string.Empty;

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

        LocalResetKeyName = $"{resetKey}／{GamepadInput.Label(resetButton)}";

        EnsureRadar();

        TickPlacement();

        bool inRound = SpaceJunkRound.Current != null;

        if (inRound && !placedInRound)
        {
            placedInRound = true;
            MoveToSpawnPoint();
            FollowWithCamera();
        }
        else if (!inRound && placedInRound)
        {
            // ロビーへ戻った。**ここでは置き直さない。**
            //
            // この瞬間はまだシーンの入れ替わりの途中で、前のマップの物が残っている。
            // 置き直すのは、ロビーのシーンが読み込み終わってから
            // （<see cref="OnSceneLoaded"/> が数えるフレームのあと）
            placedInRound = false;
        }

        CheckReset();
    }

    // ------------------------------------------------------------
    // シーンが切り替わったときの置き直し
    // ------------------------------------------------------------

    /// <summary>
    /// シーンが読み込まれてから、置き直すまでに待つフレーム数。
    /// 0 より大きい間は数えている途中。
    /// </summary>
    private int placementCountdown;

    /// <summary>
    /// シーンの中身が出そろうまで待つフレーム数。
    /// **読み込んだその場で置き直すと、まだ前のシーンの物が残っている。**
    /// </summary>
    private const int PlacementDelayFrames = 2;

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>
    /// **シーンが読み込まれたら、少し待ってから場所を置き直す。**
    ///
    /// プレイヤーはシーンをまたいで生き続けるので、**移った直後は
    /// 「前のシーンで立っていた場所」のまま**になる。
    /// ロビーの地面（20m四方）はマップ（40m四方）より狭いため、
    /// マップの端にいた人は**ロビーの地面の外に出て落ちてしまう**
    /// （2026/9/20・大槻さんの報告）。
    ///
    /// **その場で置き直すのではなく、数フレーム待つ。**
    /// 読み込んだ直後はまだ中身が出そろっておらず、
    /// 前のマップのゴールをつかんで**さらに遠くへ飛ばしてしまう**ため。
    /// </summary>
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                               UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (netPlayer == null || !netPlayer.IsOwner)
        {
            return;
        }

        placementCountdown = PlacementDelayFrames;
    }

    /// <summary>待ち終わったら置き直す。</summary>
    private void TickPlacement()
    {
        if (placementCountdown <= 0)
        {
            return;
        }

        placementCountdown--;

        if (placementCountdown == 0)
        {
            MoveToSpawnPoint();
            FollowWithCamera();
        }
    }

    /// <summary>
    /// **落ちて戻れなくなったときの立て直し。**
    ///
    /// ・自分で押して戻る（<see cref="resetKey"/>）
    /// ・下まで落ちたら自動で戻す（<see cref="fallResetHeight"/>）
    ///
    /// 穴に落ちても待っていれば戻るが、**引っかかって落ちきらないこともある**ので、
    /// 自分で戻せるキーも用意してある。
    /// </summary>
    private void CheckReset()
    {
        // 下まで落ちたら、押されなくても戻す
        if (transform.position.y < fallResetHeight)
        {
            MoveToSpawnPoint();
            FollowWithCamera();
            return;
        }

        // ポーズ中（と閉じたフレーム）と、設定の入力欄に打ち込んでいる最中は入力を読まない
        if (GamePause.BlocksInput || SpaceJunkLobbyUI.IsEditingText)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;

        if ((keyboard != null && keyboard[resetKey].wasPressedThisFrame) || GamepadInput.WasPressed(resetButton))
        {
            MoveToSpawnPoint();
            FollowWithCamera();
        }
    }

    /// <summary>
    /// **自分のぶんにだけ、レーダーを付ける。**（2026/9/22）
    /// 他の人のぶんに付けると、その人を中心にしたレーダーがもう1枚出てしまう。
    /// レーダーはマップにいる間だけ出る（<see cref="SpaceJunkRadar"/>）。
    /// </summary>
    private void EnsureRadar()
    {
        if (GetComponent<SpaceJunkRadar>() != null)
        {
            return;
        }

        SpaceJunkRadar radar = gameObject.AddComponent<SpaceJunkRadar>();
        radar.Setup(radarFrame, radarCompass, radarSelf);
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

        // **ゴールを当てにするのは、ラウンドが動いているときだけ。**
        //
        // ロビーにはゴールが無い。それなのに `SpaceJunkGoal.All` を見に行くと、
        // **シーンの入れ替わりの途中でまだ消えていない「前のマップのゴール」**を
        // つかんでしまい、ロビーの地面のはるか外（中心から約21m）に置かれる。
        // ロビーの地面は20m四方（中心から10m）なので、そのまま落ちる
        // （2026/9/20・大槻さんの報告）
        if (SpaceJunkRound.Current == null)
        {
            return FallbackSpawnPosition(scatter);
        }

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
        return FallbackSpawnPosition(scatter);
    }

    /// <summary>
    /// **ゴールを当てにしないときの出てくる場所。** ロビーでもここを使う。
    ///
    /// 中心のまわりに、接続番号ごとに角度をずらして並べる。
    /// 全員が同じ場所に重なって出てこないようにするため。
    /// </summary>
    private Vector3 FallbackSpawnPosition(Vector2 scatter)
    {
        ulong id = netPlayer != null ? netPlayer.OwnerClientId : 0UL;
        float angle = (id % (ulong)SpaceJunkTeams.MaxPlayers) * (360f / SpaceJunkTeams.MaxPlayers);
        Vector3 spot = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * fallbackRadius);

        return new Vector3(spot.x + scatter.x, 0f, spot.z + scatter.y);
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

        // 自陣が手前に来るように回るカメラ（SpaceJunkMap01TeamCam など）
        SpaceJunkTeamFollowCamera teamCamera = FindFirstObjectByType<SpaceJunkTeamFollowCamera>();
        if (teamCamera != null)
        {
            teamCamera.SetTarget(transform);
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

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **フック操作の中枢。** チャージ→発射→帰還→物資への接続、の流れを管理する。
///
/// 左クリックの流れ（仕様2）：
///   1. 押す           → 構える（チャージ開始）
///   2. 押している長さ  → チャージ量が 0〜1 まで増える
///   3. 離す           → マウス方向へフックを発射。飛距離はチャージ量で決まる
///   4. 物資に当たる    → フックした状態にして、引き寄せ・投げを ThrowController に渡す
///   5. 何も当たらない  → 最大距離まで伸びて手元へ戻る
///
/// フックの実際の飛び方の計算はここに置き、HookProjectile は当たり判定だけにしている。
/// 引き寄せ・投げは ThrowController、糸の見た目は HookLine に分けてある。
/// </summary>
public class HookController : MonoBehaviour
{
    /// <summary>フックがいまどの段階にいるか。</summary>
    public enum HookPhase
    {
        Idle,       // 手元で待機
        Charging,   // 構えてチャージ中
        Flying,     // 発射して前へ伸びている
        Returning,  // 手元へ戻っている
        Attached    // 物資に刺さっている（引き寄せ・投げ中）
    }

    [Header("参照")]
    [Tooltip("狙う方向の計算元。プレイヤーに付いている PlayerAimController")]
    [SerializeField] private PlayerAimController aim;

    [Tooltip("フックが出てくる手元の位置。プレイヤーの子に空オブジェクトを作って入れる")]
    [SerializeField] private Transform handPoint;

    [Tooltip("シーンに置いたフック先端（HookProjectile）")]
    [SerializeField] private HookProjectile hook;

    [Tooltip("糸の見た目（HookLine）")]
    [SerializeField] private HookLine line;

    [Tooltip("引き寄せ・投げを担当する ThrowController")]
    [SerializeField] private ThrowController throwController;

    [Tooltip("チャージ量やタイミングを表示する UI（無くても動く）")]
    [SerializeField] private HookChargeUI ui;

    [Tooltip("スタン（動けない状態）の管理。スタン中はフックを使えなくなる")]
    [SerializeField] private PlayerStun stun;

    [Tooltip("オンライン用の部品。**1人用のシーンでは空のままでよい**（入っていればオンラインとして動く）")]
    [SerializeField] private FishingNetPlayer netPlayer;

    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("チャージ")]
    [Tooltip("押しっぱなしでチャージが最大になるまでの秒数")]
    [SerializeField] private float chargeTime = 1.0f;

    [Header("飛距離（メートル）")]
    [Tooltip("チャージ 0 のときの飛距離")]
    [SerializeField] private float minRange = 4f;

    [Tooltip("チャージ最大のときの飛距離（最大値）")]
    [SerializeField] private float maxRange = 16f;

    [Header("フックの速さ（メートル毎秒）")]
    [Tooltip("発射して前へ伸びる速さ")]
    [SerializeField] private float flySpeed = 30f;

    [Tooltip("手元へ戻る速さ")]
    [SerializeField] private float returnSpeed = 45f;

    [Tooltip("この距離まで戻ったら「回収完了」とみなす")]
    [SerializeField] private float catchDistance = 0.5f;

    [Header("当たり判定")]
    [Tooltip("飛んでいるフックの先の、当たりを見る球の半径。大きいほど引っ掛けやすい")]
    [SerializeField] private float hookCastRadius = 0.35f;

    [Tooltip("フックが引っ掛かる相手のレイヤー。物資が乗っているレイヤーだけに絞ってもよい")]
    [SerializeField] private LayerMask hookableMask = ~0;

    private InputActionMap playerMap;
    private InputAction attackAction;

    private HookPhase phase = HookPhase.Idle;
    private float charge;
    private float traveled;
    private float targetDistance;
    private Vector3 launchDirection = Vector3.forward;
    private Vector3 hookPosition;
    private HookableObject attached;

    // ---- 他のスクリプトが読む用 ----

    /// <summary>フックが出てくる手元の位置。</summary>
    public Transform HandPoint => handPoint;

    /// <summary>狙う方向の計算元。</summary>
    public PlayerAimController Aim => aim;

    /// <summary>最後にフックを発射した向き（水平、長さ1）。フックが飛ぶ向き。</summary>
    public Vector3 LaunchDirection => launchDirection;

    /// <summary>
    /// **いまプレイヤーが向いている向き**（水平、長さ1）。
    /// 投げる方向はこちらを基準にする（引っ張っている間に振り向いたら行き先も変わる）。
    /// </summary>
    public Vector3 CurrentAimDirection
    {
        get
        {
            if (aim != null && aim.HasAim)
            {
                Vector3 flat = aim.AimDirection;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.0001f)
                {
                    return flat.normalized;
                }
            }

            Vector3 forward = transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }
    }

    /// <summary>プレイヤー本体。引き寄せの行き先の基準になる。</summary>
    public Transform PlayerRoot => transform;

    /// <summary>チャージ・タイミング表示の UI。</summary>
    public HookChargeUI UI => ui;

    /// <summary>
    /// 表示を後から結びつける。
    /// **オンラインではプレイヤーがプレハブから生まれるため、
    /// シーンに置いた UI をあらかじめ入れておけない。**
    /// 自分のプレイヤーが生まれた時点で <see cref="FishingNetPlayer"/> から渡される。
    /// </summary>
    public void SetUI(HookChargeUI newUi)
    {
        ui = newUi;

        if (ui != null)
        {
            ui.ShowCharge(false);
            ui.ShowTiming(false);
        }
    }

    /// <summary>プレイヤーがスタン中か。<see cref="ThrowController"/> も参照する。</summary>
    public bool IsStunned => stun != null && stun.IsStunned;

    /// <summary>
    /// **オンラインとして動いているか。**
    /// 通信部品が入っていて、実際に同期が始まっているときだけ true。
    /// 1人用のシーンでは常に false になり、これまでどおり手元で完結する。
    /// </summary>
    public bool IsOnline => netPlayer != null && netPlayer.IsSpawned;

    /// <summary>自分の参加番号。オンラインでないときは0。</summary>
    public int LocalPlayerIndex => netPlayer != null ? Mathf.Max(0, netPlayer.PlayerIndex) : 0;

    /// <summary>いまの段階。UI などが参照する。</summary>
    public HookPhase Phase => phase;

    private void Awake()
    {
        if (aim == null)
        {
            aim = GetComponentInParent<PlayerAimController>();
        }

        if (stun == null)
        {
            stun = GetComponent<PlayerStun>();
        }

        if (netPlayer == null)
        {
            netPlayer = GetComponent<FishingNetPlayer>();
        }

        if (inputActions == null)
        {
            Debug.LogError($"{name}: 入力の設定（InputSystem_Actions）が入っていません。", this);
            enabled = false;
            return;
        }

        playerMap = inputActions.FindActionMap("Player", true);
        attackAction = playerMap.FindAction("Attack", true);
    }

    private void OnEnable()
    {
        playerMap?.Enable();

        if (hook != null)
        {
            hook.HookableTouched += OnHookableTouched;
        }
    }

    private void OnDisable()
    {
        playerMap?.Disable();

        if (hook != null)
        {
            hook.HookableTouched -= OnHookableTouched;
        }
    }

    private void Start()
    {
        hookPosition = handPoint != null ? handPoint.position : transform.position;
        UpdateHookVisual();

        if (ui != null)
        {
            ui.ShowCharge(false);
            ui.ShowTiming(false);
        }
    }

    private void Update()
    {
        if (GamePause.IsPaused)
        {
            return;
        }

        switch (phase)
        {
            case HookPhase.Idle:
                TickIdle();
                break;
            case HookPhase.Charging:
                TickCharging();
                break;
            case HookPhase.Flying:
                TickFlying();
                break;
            case HookPhase.Returning:
                TickReturning();
                break;
            case HookPhase.Attached:
                TickAttached();
                break;
        }

        UpdateHookVisual();
    }

    private void TickIdle()
    {
        // フックは手元にくっついている
        hookPosition = handPoint.position;

        // スタン中は釣りができない。試合が終わったあとも操作を受け付けない
        if (IsStunned || !FishingMatch.PlayAllowed)
        {
            return;
        }

        if (attackAction.WasPressedThisFrame())
        {
            phase = HookPhase.Charging;
            charge = 0f;
            if (ui != null)
            {
                ui.ShowCharge(true);
                ui.SetCharge(0f);
            }
        }
    }

    private void TickCharging()
    {
        hookPosition = handPoint.position;

        charge = Mathf.Clamp01(charge + Time.deltaTime / Mathf.Max(0.01f, chargeTime));
        if (ui != null)
        {
            ui.SetCharge(charge);
        }

        if (attackAction.WasReleasedThisFrame())
        {
            Fire();
        }
    }

    private void Fire()
    {
        launchDirection = aim != null && aim.HasAim ? aim.AimDirection : transform.forward;
        launchDirection.y = 0f;
        launchDirection.Normalize();

        targetDistance = Mathf.Lerp(minRange, maxRange, charge);
        traveled = 0f;
        hookPosition = handPoint.position;
        phase = HookPhase.Flying;

        if (ui != null)
        {
            ui.ShowCharge(false);
        }
    }

    private void TickFlying()
    {
        float step = flySpeed * Time.deltaTime;

        // 動いた区間に物資があれば、その手前で引っ掛ける（速いフックがすり抜けないように）
        if (Physics.SphereCast(hookPosition, hookCastRadius, launchDirection,
                out RaycastHit hit, step, hookableMask, QueryTriggerInteraction.Collide))
        {
            HookableObject touched = hit.collider.GetComponentInParent<HookableObject>();
            if (touched != null && !touched.IsHooked && !touched.IsVanished)
            {
                OnHookableTouched(touched);
                return;
            }
        }

        hookPosition += launchDirection * step;
        traveled += step;

        if (traveled >= targetDistance)
        {
            phase = HookPhase.Returning;
        }
    }

    private void TickReturning()
    {
        hookPosition = Vector3.MoveTowards(
            hookPosition, handPoint.position, returnSpeed * Time.deltaTime);

        if (Vector3.Distance(hookPosition, handPoint.position) <= catchDistance)
        {
            phase = HookPhase.Idle;
        }
    }

    private void TickAttached()
    {
        // ThrowController が物資を引き寄せている。フックと糸の先は物資の結び目に貼り付く。
        // 投げ終わると ThrowController が NotifyThrowFinished() を呼ぶ

        // 引き寄せ中に物資が消えた（爆発した）場合は、ここで取り残されないよう戻る
        if (attached == null || attached.IsVanished)
        {
            attached = null;
            phase = HookPhase.Returning;
            return;
        }

        hookPosition = attached.AnchorPoint;
    }

    /// <summary>飛んでいるフックが物資に触れたときに呼ばれる。</summary>
    private void OnHookableTouched(HookableObject hookable)
    {
        if (phase != HookPhase.Flying || hookable == null || hookable.IsHooked || hookable.IsVanished)
        {
            return;
        }

        // オンラインでは、**早い者勝ちの判定をホストが行う。**
        // ここでは先に手元で引っ掛けた形にしておき（待たせると操作が重くなる）、
        // すでに取られていた場合はホストから断りが来て CancelAttachBecauseTaken() で戻す
        if (IsOnline)
        {
            FishingNetSupply netSupply = hookable.GetComponent<FishingNetSupply>();

            if (netSupply != null)
            {
                if (netSupply.IsClaimed)
                {
                    return;
                }

                netSupply.RequestClaim(LocalPlayerIndex);
            }
        }

        hookable.SetHooked(true);
        attached = hookable;
        hookPosition = hookable.AnchorPoint;
        phase = HookPhase.Attached;

        // **爆発物なら、ここで導火線に火がつく**（釣り上げられた瞬間から数え始める）
        ExplosiveObject bomb = hookable.GetComponent<ExplosiveObject>();
        if (bomb != null)
        {
            // オンラインでは、火がついたことをホスト経由で全員に配る。
            // 手元だけで火をつけると、他の人には突然爆発したように見えてしまう
            FishingNetBomb netBomb = hookable.GetComponent<FishingNetBomb>();

            if (IsOnline && netBomb != null)
            {
                netBomb.RequestLightFuse();
            }
            else
            {
                bomb.LightFuse();
            }
        }

        if (throwController != null)
        {
            throwController.Begin(hookable);
        }
        else
        {
            // 投げる係がいなければ、その場で解除して戻る
            hookable.SetHooked(false);
            attached = null;
            phase = HookPhase.Returning;
        }
    }

    /// <summary>ThrowController が投げ終わったら呼ぶ。フックを手元へ戻し始める。</summary>
    public void NotifyThrowFinished()
    {
        attached = null;
        phase = HookPhase.Returning;
    }

    /// <summary>
    /// **オンラインで、ホストから「その物資はもう取られている」と返ってきたとき。**
    /// 手元で先に引っ掛けた形にしていたのを取り消して、フックを戻す。
    /// </summary>
    public void CancelAttachBecauseTaken()
    {
        if (phase != HookPhase.Attached)
        {
            return;
        }

        if (attached != null)
        {
            attached.SetHooked(false);
        }
        attached = null;

        if (throwController != null)
        {
            throwController.AbandonBecauseLost();
        }

        phase = HookPhase.Returning;
        Debug.Log("[FISH] その物資は先に取られていました。フックを戻します。");
    }

    /// <summary>糸とフックの見た目を、いまの段階に合わせて更新する。</summary>
    private void UpdateHookVisual()
    {
        if (hook != null)
        {
            hook.MoveTo(hookPosition);
        }

        if (line == null)
        {
            return;
        }

        bool showLine = phase == HookPhase.Flying
                     || phase == HookPhase.Returning
                     || phase == HookPhase.Attached;

        line.Show(showLine);
        if (showLine)
        {
            line.SetEnds(handPoint.position, hookPosition);
        }
    }
}

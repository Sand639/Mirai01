using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>投げの強さの段階。タイミングの正確さで決まる（仕様5）。</summary>
public enum ThrowTier
{
    Normal,   // 通常成功 … 普通の力
    Good,     // 良いタイミング … 強い力
    Perfect   // 最適なタイミング … 非常に強い力
}

/// <summary>投げる方向の決め方（仕様6・7）。あとから種類を足せるよう enum にしている。</summary>
public enum ThrowDirectionMode
{
    Backward,     // プレイヤーの後方（フックを撃った向きの反対）
    Forward,      // プレイヤーの前方（フックを撃った向き）
    AimAtCursor,  // 投げる瞬間のマウスカーソル方向
    WorldCustom   // 下の Custom Direction で指定した固定方向
}

/// <summary>
/// **フックした物資を引き寄せて、タイミング入力で投げる係。**
///
/// 流れ（仕様4・5）：
///   1. HookController から Begin() で物資を受け取る
///   2. 物資をプレイヤーの方へ引き寄せる
///   3. その間、タイミングのゲージが左右に振れる
///   4. 左クリックした瞬間のゲージ位置で、投げる強さが決まる
///        中心から遠い          → 通常（普通の力）
///        中心に近い（Good枠）   → 強い力
///        ほぼ中心（Perfect枠）  → 非常に強い力
///   5. 決めた方向へ物理で飛ばす
///
/// 力・タイミングの枠・方向は、すべて Inspector から調整できる。
/// 投げる方向の種類は <see cref="ThrowDirectionMode"/> に足すだけで増やせる。
/// </summary>
public class ThrowController : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("同じプレイヤーの HookController")]
    [SerializeField] private HookController hook;

    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("引き寄せ")]
    [Tooltip("物資をプレイヤーへ引き寄せる速さ（メートル毎秒）")]
    [SerializeField] private float reelSpeed = 7f;

    [Tooltip("プレイヤーにこの距離まで近づいたら、引き寄せを止めて手元に留める")]
    [SerializeField] private float holdDistance = 2f;

    [Header("タイミング")]
    [Tooltip("ゲージが端から端まで動くのにかかる秒数。短いほど難しい")]
    [SerializeField] private float timingCycle = 1.1f;

    [Tooltip("ゲージ上の“当たり”の中心。0で左端、1で右端、0.5で真ん中")]
    [Range(0f, 1f)]
    [SerializeField] private float targetCenter = 0.75f;

    [Tooltip("『強い力』になる範囲（中心からの片側の広さ。0〜1）")]
    [SerializeField] private float goodWindow = 0.16f;

    [Tooltip("『非常に強い力』になる範囲（中心からの片側の広さ。0〜1）")]
    [SerializeField] private float perfectWindow = 0.05f;

    [Header("投げる力")]
    [Tooltip("通常成功のときの力")]
    [SerializeField] private float throwForceNormal = 8f;

    [Tooltip("良いタイミングのときの力")]
    [SerializeField] private float throwForceGood = 14f;

    [Tooltip("最適なタイミングのときの力")]
    [SerializeField] private float throwForcePerfect = 22f;

    [Tooltip("投げるときに少し上向きに加える力（0で真横に飛ぶ）")]
    [SerializeField] private float throwLift = 2f;

    [Header("方向")]
    [Tooltip("投げる方向の決め方")]
    [SerializeField] private ThrowDirectionMode directionMode = ThrowDirectionMode.Backward;

    [Tooltip("方向が WorldCustom のときに使う固定方向")]
    [SerializeField] private Vector3 customDirection = Vector3.zero;

    private InputActionMap playerMap;
    private InputAction attackAction;

    private HookableObject target;
    private bool active;
    private float armDelay;   // Begin 直後の1入力を誤爆しないための短い待ち
    private float timer;
    private bool targetGravityWas;

    private void Awake()
    {
        if (hook == null)
        {
            hook = GetComponent<HookController>();
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
    }

    private void OnDisable()
    {
        playerMap?.Disable();
    }

    /// <summary>HookController から呼ばれる。引き寄せとタイミングを開始する。</summary>
    public void Begin(HookableObject hookable)
    {
        target = hookable;
        active = true;
        timer = 0f;
        armDelay = 0.15f;

        if (target != null)
        {
            // 引き寄せ中は重力を切る。近くで手元に留めるときに沈まないようにするため
            targetGravityWas = target.Body.useGravity;
            target.Body.useGravity = false;
        }

        if (hook != null && hook.UI != null)
        {
            hook.UI.ShowTiming(true);
        }
    }

    private void Update()
    {
        if (!active || GamePause.IsPaused)
        {
            return;
        }

        timer += Time.deltaTime;
        if (armDelay > 0f)
        {
            armDelay -= Time.deltaTime;
        }

        float cyclePos = Mathf.Repeat(timer / Mathf.Max(0.05f, timingCycle), 1f);

        if (hook != null && hook.UI != null)
        {
            hook.UI.SetTiming(cyclePos, targetCenter, goodWindow, perfectWindow);
        }

        if (armDelay <= 0f && attackAction.WasPressedThisFrame())
        {
            Release(cyclePos);
        }
    }

    private void FixedUpdate()
    {
        if (!active || target == null || hook == null)
        {
            return;
        }

        Vector3 toPlayer = hook.HandPoint.position - target.transform.position;
        float distance = toPlayer.magnitude;

        if (distance > holdDistance)
        {
            target.Body.linearVelocity = toPlayer.normalized * reelSpeed;
        }
        else
        {
            // 近づいたら止める（急に0にせず、少しずつ落ち着かせる）
            target.Body.linearVelocity = Vector3.Lerp(
                target.Body.linearVelocity, Vector3.zero, 0.25f);
        }
    }

    /// <summary>左クリックされた瞬間のゲージ位置で、強さと方向を決めて投げる。</summary>
    private void Release(float cyclePos)
    {
        ThrowTier tier = JudgeTiming(cyclePos);
        Vector3 direction = ResolveDirection(directionMode, tier);
        float force = ForceOf(tier);

        Rigidbody body = target.Body;
        body.useGravity = targetGravityWas;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.AddForce(direction * force + Vector3.up * throwLift, ForceMode.Impulse);

        target.SetHooked(false);
        target = null;
        active = false;

        if (hook.UI != null)
        {
            hook.UI.ShowTiming(false);
        }

        hook.NotifyThrowFinished();

        Debug.Log($"投げ：{tier}（力 {force}）");
    }

    /// <summary>ゲージ位置が“当たり”からどれだけ近いかで段階を返す。</summary>
    private ThrowTier JudgeTiming(float cyclePos)
    {
        float raw = Mathf.Abs(cyclePos - targetCenter);

        // ゲージは端でつながっているので、反対回りの距離も見て近いほうを採る
        float distance = Mathf.Min(raw, 1f - raw);

        if (distance <= perfectWindow)
        {
            return ThrowTier.Perfect;
        }
        if (distance <= goodWindow)
        {
            return ThrowTier.Good;
        }
        return ThrowTier.Normal;
    }

    private float ForceOf(ThrowTier tier)
    {
        switch (tier)
        {
            case ThrowTier.Perfect: return throwForcePerfect;
            case ThrowTier.Good: return throwForceGood;
            default: return throwForceNormal;
        }
    }

    /// <summary>
    /// 投げる方向を決める。
    /// いまは <paramref name="tier"/> は方向に影響しないが、
    /// 「タイミングで方向を変える」（仕様7）を足すときに、ここで分岐できるよう受け取っている。
    /// </summary>
    private Vector3 ResolveDirection(ThrowDirectionMode mode, ThrowTier tier)
    {
        Vector3 direction;

        switch (mode)
        {
            case ThrowDirectionMode.Forward:
                direction = hook.LaunchDirection;
                break;
            case ThrowDirectionMode.AimAtCursor:
                direction = hook.Aim != null && hook.Aim.HasAim
                    ? hook.Aim.AimDirection
                    : hook.LaunchDirection;
                break;
            case ThrowDirectionMode.WorldCustom:
                direction = customDirection.sqrMagnitude > 0.001f
                    ? customDirection.normalized
                    : -hook.LaunchDirection;
                break;
            case ThrowDirectionMode.Backward:
            default:
                direction = -hook.LaunchDirection;
                break;
        }

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = -hook.LaunchDirection;
        }
        return direction.normalized;
    }
}

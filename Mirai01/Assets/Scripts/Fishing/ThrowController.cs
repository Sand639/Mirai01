using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>投げの強さの段階。スキルチェックの正確さで決まる（仕様5）。</summary>
public enum ThrowTier
{
    Normal,   // 通常成功 … 普通の力
    Good,     // 良いタイミング … 強い力
    Perfect   // 最適なタイミング … 非常に強い力
}

/// <summary>投げる方向の決め方（仕様6・7）。あとから種類を足せるよう enum にしている。</summary>
public enum ThrowDirectionMode
{
    Backward,     // プレイヤーの後方（いま向いている方向の反対）
    Forward,      // プレイヤーの前方（いま向いている方向＝進行方向）
    WorldCustom   // 下の Custom Direction で指定した固定方向
}

/// <summary>
/// **スキルチェックの枠1つぶんの設定。**
///
/// ゲージの上のどこにあるか、どれくらいの広さか、成功したらどっちへ投げるか、をまとめて持つ。
/// **枠を増やしたいときは、ThrowController の Skill Check Zones に足すだけでよい。**
/// </summary>
[System.Serializable]
public class SkillCheckZone
{
    [Tooltip("画面とログに出す名前")]
    public string label = "前半";

    [Tooltip("ゲージ上の中心。0で左端、0.5で真ん中、1で右端")]
    [Range(0f, 1f)]
    public float center = 0.27f;

    [Tooltip("『通常成功』になる範囲（中心からの片側の広さ）。ここを外すとミス")]
    public float hitWindow = 0.11f;

    [Tooltip("『強い力』になる範囲（中心からの片側の広さ）")]
    public float goodWindow = 0.06f;

    [Tooltip("『非常に強い力』になる範囲（中心からの片側の広さ）")]
    public float perfectWindow = 0.022f;

    [Tooltip("この枠で成功したときに物資を飛ばす方向")]
    public ThrowDirectionMode direction = ThrowDirectionMode.Backward;
}

/// <summary>
/// **フックした物資を引き寄せて、スキルチェックの入力で投げる係。**
///
/// 引き寄せの動き（仕様改善1）：
///   物資はプレイヤーへ向かって弧を描いて上がり、**ゲージの真ん中（0.5）でプレイヤーの真上を通過**し、
///   そのまま後方へ抜けていく。「釣り竿で引っこ抜く」動きになる。
///
/// スキルチェック（仕様追加1）：
///   ゲージは**1往復だけ**動く。枠は2つあり、
///     ・前半の枠（真上を通る前）で押す → **後方**へ飛ばす
///     ・後半の枠（真上を通った後）で押す → **進行方向（前方）**へ飛ばす
///   枠の中心に近いほど強く投げる（通常／強い／非常に強い）。
///
/// ミス（仕様改善2）：
///   **ゲージが通り過ぎた**か、**枠の外で押した**場合はミス。
///   ほんの少しだけプレイヤー側へ引き寄せるだけで、引っ張りを終了する。
///
/// 投げる方向は、**投げた瞬間にプレイヤーが向いている方向**を基準にする（仕様改善3）。
/// 釣り上げたときの向きではないので、引っ張っている間に振り向けば行き先も変わる。
/// </summary>
public class ThrowController : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("同じプレイヤーの HookController")]
    [SerializeField] private HookController hook;

    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("引き寄せの動き")]
    [Tooltip("引き寄せ〜スキルチェックが終わるまでの秒数。短いほど難しい")]
    [SerializeField] private float skillCheckDuration = 1.6f;

    [Tooltip("プレイヤーの足元から、物資が通り越す高さ（メートル）")]
    [SerializeField] private float overheadHeight = 2.6f;

    [Tooltip("真上を通ったあと、後方どれだけまで抜けていくか（メートル）")]
    [SerializeField] private float passDistance = 3.5f;

    [Tooltip("弧の膨らみ。大きいほど高く跳ね上げてから来る")]
    [SerializeField] private float arcLift = 0.8f;

    [Tooltip("引き寄せ中に物資を回す速さ（1秒あたりの度）。0で回さない")]
    [SerializeField] private float reelSpinSpeed = 200f;

    [Header("スキルチェックの枠")]
    [Tooltip("枠は上から順に判定される。**増やせばそのまま増える**")]
    [SerializeField]
    private SkillCheckZone[] skillCheckZones =
    {
        new SkillCheckZone
        {
            label = "前半",
            center = 0.27f,
            hitWindow = 0.11f,
            goodWindow = 0.06f,
            perfectWindow = 0.022f,
            direction = ThrowDirectionMode.Backward
        },
        new SkillCheckZone
        {
            label = "後半",
            center = 0.73f,
            hitWindow = 0.11f,
            goodWindow = 0.06f,
            perfectWindow = 0.022f,
            direction = ThrowDirectionMode.Forward
        }
    };

    [Header("投げる力")]
    [Tooltip("通常成功のときの力")]
    [SerializeField] private float throwForceNormal = 8f;

    [Tooltip("良いタイミングのときの力")]
    [SerializeField] private float throwForceGood = 14f;

    [Tooltip("最適なタイミングのときの力")]
    [SerializeField] private float throwForcePerfect = 22f;

    [Tooltip("投げるときに少し上向きに加える力（0で真横に飛ぶ）")]
    [SerializeField] private float throwLift = 2f;

    [Tooltip("方向が WorldCustom の枠で使う固定方向")]
    [SerializeField] private Vector3 customDirection = Vector3.zero;

    [Header("ミスしたとき")]
    [Tooltip("ミスしたときに、プレイヤー側へほんの少し引き寄せる力")]
    [SerializeField] private float missPullForce = 2.5f;

    private InputActionMap playerMap;
    private InputAction attackAction;

    private HookableObject target;
    private bool active;
    private float armDelay;   // Begin 直後の1入力を誤爆しないための短い待ち
    private float timer;
    private Vector3 reelStart;
    private bool targetGravityWas;
    private bool targetKinematicWas;

    /// <summary>いま引き寄せ中か。UI などが参照する。</summary>
    public bool IsPulling => active;

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

    /// <summary>HookController から呼ばれる。引き寄せとスキルチェックを開始する。</summary>
    public void Begin(HookableObject hookable)
    {
        target = hookable;
        active = true;
        timer = 0f;
        armDelay = 0.12f;

        if (target != null)
        {
            reelStart = target.transform.position;

            // 引き寄せ中は決まった軌道を通らせたいので、物理を一時的に止める。
            // （物理で引っ張ると、ゲージの真ん中で真上に来るように揃えられない）
            targetGravityWas = target.Body.useGravity;
            targetKinematicWas = target.Body.isKinematic;
            target.Body.linearVelocity = Vector3.zero;
            target.Body.angularVelocity = Vector3.zero;
            target.Body.useGravity = false;
            target.Body.isKinematic = true;
        }

        if (hook != null && hook.UI != null)
        {
            hook.UI.ShowTiming(true);
            ApplyZonesToUI();
        }
    }

    private void Update()
    {
        if (!active)
        {
            return;
        }

        // 途中で物資が消えた（爆発した・ポケットに入った）場合は、物理をいじらずに終わる
        if (target == null || target.IsVanished)
        {
            AbandonPull();
            return;
        }

        if (GamePause.IsPaused)
        {
            return;
        }

        // スタンさせられたら引っ張りは続けられない。ミス扱いで終了する
        if (hook != null && hook.IsStunned)
        {
            FinishAsMiss("スタンしたため");
            return;
        }

        timer += Time.deltaTime;
        if (armDelay > 0f)
        {
            armDelay -= Time.deltaTime;
        }

        float t = Mathf.Clamp01(timer / Mathf.Max(0.1f, skillCheckDuration));

        UpdateReelPosition(t);

        if (hook != null && hook.UI != null)
        {
            hook.UI.SetMarker(t);
        }

        if (armDelay <= 0f && attackAction.WasPressedThisFrame())
        {
            Resolve(t);
            return;
        }

        // ゲージが1往復して通り過ぎたらミス（仕様改善2）
        if (timer >= skillCheckDuration)
        {
            FinishAsMiss("ゲージが通り過ぎた");
        }
    }

    // ------------------------------------------------------------
    // 引き寄せの軌道
    // ------------------------------------------------------------

    /// <summary>
    /// ゲージの進み具合 <paramref name="t"/>（0〜1）から、物資の位置を決める。
    ///
    /// 0〜0.5 … 釣り上げた場所から、プレイヤーの真上へ弧を描いて上がってくる
    /// 0.5    … **プレイヤーの真上**
    /// 0.5〜1 … そのまま後方へ抜けていく
    ///
    /// プレイヤーは動くので、行き先は毎フレーム取り直している。
    /// </summary>
    private void UpdateReelPosition(float t)
    {
        Vector3 root = hook.PlayerRoot.position;
        Vector3 aimDirection = hook.CurrentAimDirection;

        Vector3 overhead = root + Vector3.up * overheadHeight;
        Vector3 behind = root - aimDirection * passDistance + Vector3.up * (overheadHeight * 0.45f);

        Vector3 position;

        if (t <= 0.5f)
        {
            float u = Mathf.Clamp01(t / 0.5f);
            position = Vector3.Lerp(reelStart, overhead, Mathf.SmoothStep(0f, 1f, u));
            position.y += Mathf.Sin(u * Mathf.PI) * arcLift;
        }
        else
        {
            float u = Mathf.Clamp01((t - 0.5f) / 0.5f);
            position = Vector3.Lerp(overhead, behind, u);
            position.y += Mathf.Sin(u * Mathf.PI) * (arcLift * 0.4f);
        }

        target.Body.position = position;
        target.transform.position = position;

        if (reelSpinSpeed != 0f)
        {
            target.transform.Rotate(Vector3.right, reelSpinSpeed * Time.deltaTime, Space.Self);
        }
    }

    // ------------------------------------------------------------
    // スキルチェックの判定
    // ------------------------------------------------------------

    /// <summary>押された瞬間のゲージ位置を、どの枠に入ったかで判定する。</summary>
    private void Resolve(float t)
    {
        if (skillCheckZones != null)
        {
            foreach (SkillCheckZone zone in skillCheckZones)
            {
                if (zone == null)
                {
                    continue;
                }

                float distance = Mathf.Abs(t - zone.center);

                if (distance <= zone.perfectWindow)
                {
                    FinishAsThrow(zone, ThrowTier.Perfect);
                    return;
                }
                if (distance <= zone.goodWindow)
                {
                    FinishAsThrow(zone, ThrowTier.Good);
                    return;
                }
                if (distance <= zone.hitWindow)
                {
                    FinishAsThrow(zone, ThrowTier.Normal);
                    return;
                }
            }
        }

        FinishAsMiss("枠の外で押した");
    }

    private void FinishAsThrow(SkillCheckZone zone, ThrowTier tier)
    {
        Vector3 direction = ResolveDirection(zone.direction, tier);
        float force = ForceOf(tier);

        Rigidbody body = target.Body;
        RestorePhysics(body);
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.AddForce(direction * force + Vector3.up * throwLift, ForceMode.Impulse);

        Debug.Log($"スキルチェック成功：{zone.label} / {tier} / {zone.direction} へ（力 {force}）");
        EndPull();
    }

    private void FinishAsMiss(string reason)
    {
        Rigidbody body = target.Body;
        RestorePhysics(body);

        // ほんの少しだけプレイヤー側へ引き寄せて終わる（仕様改善2）
        Vector3 toPlayer = hook.PlayerRoot.position - target.transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f)
        {
            toPlayer = -hook.CurrentAimDirection;
        }

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.AddForce(toPlayer.normalized * missPullForce, ForceMode.Impulse);

        Debug.Log($"スキルチェック失敗（{reason}）：少しだけ引き寄せて終了");
        EndPull();
    }

    /// <summary>引き寄せ中に物資が消えた場合。物理には触らず、フックだけ戻す。</summary>
    private void AbandonPull()
    {
        target = null;
        active = false;

        if (hook != null)
        {
            if (hook.UI != null)
            {
                hook.UI.ShowTiming(false);
            }
            hook.NotifyThrowFinished();
        }
    }

    private void EndPull()
    {
        target.SetHooked(false);
        target = null;
        active = false;

        if (hook.UI != null)
        {
            hook.UI.ShowTiming(false);
        }

        hook.NotifyThrowFinished();
    }

    /// <summary>引き寄せのために止めていた物理を元に戻す。</summary>
    private void RestorePhysics(Rigidbody body)
    {
        body.isKinematic = targetKinematicWas;
        body.useGravity = targetGravityWas;
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
    ///
    /// **基準は「投げた瞬間にプレイヤーが向いている方向」**（仕様改善3）。
    /// 釣り上げたときの向きを使っていると、引っ張っている間に振り向いたときに
    /// 思っていた方向と逆へ飛んでしまうため。
    ///
    /// いまは <paramref name="tier"/> は方向に影響しないが、
    /// 「タイミングの良さで方向も変える」を足すときに、ここで分岐できるよう受け取っている。
    /// </summary>
    private Vector3 ResolveDirection(ThrowDirectionMode mode, ThrowTier tier)
    {
        Vector3 facing = hook.CurrentAimDirection;
        Vector3 direction;

        switch (mode)
        {
            case ThrowDirectionMode.Forward:
                direction = facing;
                break;
            case ThrowDirectionMode.WorldCustom:
                direction = customDirection.sqrMagnitude > 0.001f
                    ? customDirection.normalized
                    : -facing;
                break;
            case ThrowDirectionMode.Backward:
            default:
                direction = -facing;
                break;
        }

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = -facing;
        }
        return direction.normalized;
    }

    /// <summary>枠の位置と広さを UI に伝える。開始時に1回だけ呼ぶ。</summary>
    private void ApplyZonesToUI()
    {
        if (skillCheckZones == null)
        {
            return;
        }

        hook.UI.SetZoneCount(skillCheckZones.Length);

        for (int i = 0; i < skillCheckZones.Length; i++)
        {
            SkillCheckZone zone = skillCheckZones[i];
            if (zone == null)
            {
                continue;
            }
            hook.UI.SetZone(i, zone.center, zone.hitWindow, zone.goodWindow, zone.perfectWindow);
        }
    }
}

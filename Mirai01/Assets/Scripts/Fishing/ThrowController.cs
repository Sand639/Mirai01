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
/// **引っ掛けたあとの操作のしかた。** プレイヤーのプレハブの ThrowController の Style で切り替える。
/// </summary>
public enum ThrowStyle
{
    /// <summary>
    /// **釣り式（もとの仕様）。** 引き寄せながらゲージが1回だけ動き、
    /// 前半の枠で押す＝後ろへ、後半の枠で押す＝前へ投げる。
    /// </summary>
    SweepOnce,

    /// <summary>
    /// **宇宙ごみ式（2026/9/22・大槻さんの依頼）。** 撃つボタンで流れが分かれる。
    /// 左クリック（LT）で撃つ → くっついた場所で止まってゲージ → 左で引っ張る（頭上を越えて後ろへ飛ぶ）。
    /// 右クリック（RT）で撃つ → 頭上まで来て止まってゲージ → 右で投げる。
    /// ゲージは往復し、真ん中に近いほど強い。
    /// </summary>
    TwoButtons
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
///
/// ## 宇宙ごみ式（Style が TwoButtons のとき。2026/9/22）
///
/// 上は釣り式（SweepOnce）の説明。宇宙ごみのプレイヤーは **TwoButtons** にしてあり、流れが違う：
///
///   **フックを撃つボタンで、引っ掛けたあとの流れが決まる**（ゲージは往復し、真ん中に1つの枠。真ん中ほど強い）
///
///   ・左クリック（LT）で撃つ … くっついた場所で**止まって**ゲージ → 左で押すと、釣り式と同じ軌道で
///     **頭上を越えて後ろへ抜け**、そのまま後ろへ飛ぶ（強いほど遠く）
///   ・右クリック（RT）で撃つ … 頭上まで来て**止まって**ゲージ → 右で押すと、
///     プレイヤー→くっついていた場所の向きに**投げる**（強いほど遠く）
///
/// 押すまでずっと待つ（時間切れなし）。**ほかの人のフックが当たる**か、爆風を受けると外れる。
/// 釣り式に戻したいときは、プレハブの Style を SweepOnce にするだけでよい。
/// </summary>
public class ThrowController : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("同じプレイヤーの HookController")]
    [SerializeField] private HookController hook;

    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("操作のしかた")]
    [Tooltip("SweepOnce＝釣り式（もとの仕様）。TwoButtons＝宇宙ごみ式（左で撃つと引っ張る・右で撃つと投げる）。\n" +
             "**いつでもここを切り替えれば、もとの仕様に戻せる**")]
    [SerializeField] private ThrowStyle style = ThrowStyle.SweepOnce;

    [Header("宇宙ごみ式（Style が TwoButtons のとき）")]
    [Tooltip("ゲージのマーカーが、1秒間に端から端まで何回動くか。大きいほど速くて難しい")]
    [Min(0.1f)]
    [SerializeField] private float barSweepsPerSecond = 1.2f;

    [Tooltip("引っ張るとき、真ん中から一番遠い（端で押した）ときの力")]
    [SerializeField] private float pullForceMin = 4f;

    [Tooltip("引っ張るとき、真ん中ちょうどで押したときの力。頭上を越えたあと、この力で後ろへ飛ぶ")]
    [SerializeField] private float pullForceMax = 16f;

    [Tooltip("引っ張るときに少し上向きに加える力")]
    [SerializeField] private float pullLift = 3f;

    [Tooltip("引っ張ってから、頭上を越えて後ろへ抜けるまでの秒数")]
    [Min(0.05f)]
    [SerializeField] private float pullTravelSeconds = 0.6f;

    [Tooltip("右クリックで撃って引っ掛けてから、頭上まで持ち上がるまでの秒数")]
    [Min(0.05f)]
    [SerializeField] private float liftSeconds = 0.45f;

    [Tooltip("投げるとき、真ん中から一番遠い（端で押した）ときの力")]
    [SerializeField] private float twoButtonThrowForceMin = 6f;

    [Tooltip("投げるとき、真ん中ちょうどで押したときの力")]
    [SerializeField] private float twoButtonThrowForceMax = 22f;

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

    // ---- 宇宙ごみ式の途中経過 ----

    /// <summary>宇宙ごみ式の段階。</summary>
    private enum TwoButtonStage
    {
        AimPull,     // 【左で撃った】くっついた場所で止まってゲージ。左＝引っ張る
        PullTravel,  // 【左で撃った】引っ張って、頭上を越えて後ろへ抜けている途中（ゲージは出さない）
        Lifting,     // 【右で撃った】頭上へ持ち上げている途中（ゲージは出さない）
        AimThrow     // 【右で撃った】頭上で止まってゲージ。右＝投げる
    }

    private TwoButtonStage stage;
    private float barTimer;
    private float liftTimer;
    private float pullAccuracy;

    /// <summary>次に引っ掛けたときに「投げる」ほうか（右で撃った）。false なら「引っ張る」ほう（左で撃った）。</summary>
    private bool nextIsThrow;

    /// <summary>いま引き寄せ中か。UI などが参照する。</summary>
    public bool IsPulling => active;

    /// <summary>いまの操作のしかた。</summary>
    public ThrowStyle Style => style;

    /// <summary>
    /// **ほかの人のフックが当たったら、その人の引っ掛けを外すか。**
    /// 宇宙ごみ式だけ（2026/9/22・大槻さん「他の人のフックが当たったら失敗」）。釣りでは今までどおり素通りする。
    /// </summary>
    public bool BreaksOthersGrab => style == ThrowStyle.TwoButtons;

    /// <summary>
    /// **宇宙ごみ式で、どちらのボタンでフックを撃ったかを伝える。** <see cref="HookController"/> が撃つときに呼ぶ。
    /// true＝右（投げる）、false＝左（引っ張る）。
    /// </summary>
    public void SetNextIsThrow(bool isThrow)
    {
        nextIsThrow = isThrow;
    }

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

        // **入力の設定は、この体専用の複製を使う**（理由は FishingPlayerController.Awake と同じ。
        // 共有したままだと、他の人のぶんを止めたときに自分の入力まで止まる）
        inputActions = Instantiate(inputActions);

        playerMap = inputActions.FindActionMap("Player", true);
        attackAction = playerMap.FindAction("Attack", true);
    }

    private void OnDestroy()
    {
        if (inputActions != null)
        {
            Destroy(inputActions);
        }
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

        if (style == ThrowStyle.TwoButtons)
        {
            BeginTwoButtons();
            return;
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

        if (GamePause.BlocksInput)
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

        if (style == ThrowStyle.TwoButtons)
        {
            UpdateTwoButtons();
            return;
        }

        float t =Mathf.Clamp01(timer / Mathf.Max(0.1f, skillCheckDuration));

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
    // 宇宙ごみ式（Style が TwoButtons のとき）
    // ------------------------------------------------------------

    /// <summary>
    /// 引っ掛けた直後。**どちらのボタンで撃ったかで流れが分かれる。**
    /// 左＝くっついた場所で止まってゲージ。右＝まず頭上へ持ち上げる。
    /// </summary>
    private void BeginTwoButtons()
    {
        if (nextIsThrow)
        {
            stage = TwoButtonStage.Lifting;
            liftTimer = 0f;

            if (hook != null && hook.UI != null)
            {
                hook.UI.ShowTiming(false);
            }
        }
        else
        {
            stage = TwoButtonStage.AimPull;
            StartBar();
        }
    }

    /// <summary>ゲージを左端から動かし始める。枠は真ん中に1つだけ。</summary>
    private void StartBar()
    {
        barTimer = 0f;
        armDelay = Mathf.Max(armDelay, 0.12f);

        if (hook != null && hook.UI != null)
        {
            hook.UI.ShowTiming(true);

            // 真ん中に1つ。見た目は「外側＝通常・内側＝強い・中心＝最も強い」の3段だが、
            // 実際の強さは**真ん中からの近さでなめらかに**変わる（段ではない）
            hook.UI.SetZoneCount(1);
            hook.UI.SetZone(0, 0.5f, 0.5f, 0.28f, 0.07f);
            hook.UI.SetMarker(0f);
        }
    }

    /// <summary>
    /// **ゲージのマーカーの位置（0〜1）。端まで行ったら跳ね返って往復する。**
    /// </summary>
    private float BarPosition()
    {
        return Mathf.PingPong(barTimer * barSweepsPerSecond, 1f);
    }

    /// <summary>
    /// **真ん中にどれだけ近いか（0〜1）。** 真ん中ちょうどで 1、端で 0。
    /// </summary>
    private static float Accuracy(float barPosition)
    {
        return 1f - Mathf.Clamp01(Mathf.Abs(barPosition - 0.5f) / 0.5f);
    }

    private void UpdateTwoButtons()
    {
        switch (stage)
        {
            case TwoButtonStage.AimPull:
                TickBar();

                if (armDelay <= 0f && PullPressed())
                {
                    // 強さを覚えて、釣り式と同じく**頭上を越えて後ろへ抜ける**動きを始める
                    pullAccuracy = Accuracy(BarPosition());
                    stage = TwoButtonStage.PullTravel;
                    liftTimer = 0f;

                    if (hook != null && hook.UI != null)
                    {
                        hook.UI.ShowTiming(false);
                    }
                }
                break;

            case TwoButtonStage.PullTravel:
                liftTimer += Time.deltaTime;
                float travel = Mathf.Clamp01(liftTimer / pullTravelSeconds);

                // 釣り式と同じ軌道（ゲージ 0〜1 ぶん）：弧を描いて頭上を通り、後ろへ抜ける
                UpdateReelPosition(travel);

                if (travel >= 1f)
                {
                    FinishAsPull(pullAccuracy);
                }
                break;

            case TwoButtonStage.Lifting:
                liftTimer += Time.deltaTime;
                float u = Mathf.Clamp01(liftTimer / liftSeconds);

                // 釣り式の前半と同じ、弧を描いて頭上へ上がる動き（ゲージ 0〜0.5 の部分）
                UpdateReelPosition(u * 0.5f);

                if (u >= 1f)
                {
                    stage = TwoButtonStage.AimThrow;
                    StartBar();
                }
                break;

            case TwoButtonStage.AimThrow:
                // 頭上で止めておく（プレイヤーが動いたらついてくる）
                UpdateReelPosition(0.5f);
                TickBar();

                if (armDelay <= 0f && ThrowPressed())
                {
                    FinishAsTwoButtonThrow(Accuracy(BarPosition()));
                }
                break;
        }
    }

    private void TickBar()
    {
        barTimer += Time.deltaTime;

        if (hook != null && hook.UI != null)
        {
            hook.UI.SetMarker(BarPosition());
        }
    }

    /// <summary>「引っ張る」ボタンが押された瞬間か。左クリックか、コントローラーの LT。</summary>
    public static bool PullPressed()
    {
        Mouse mouse = Mouse.current;
        Gamepad pad = Gamepad.current;

        return (mouse != null && mouse.leftButton.wasPressedThisFrame)
            || (pad != null && pad.leftTrigger.wasPressedThisFrame);
    }

    /// <summary>「引っ張る」ボタンが離された瞬間か。</summary>
    public static bool PullReleased()
    {
        Mouse mouse = Mouse.current;
        Gamepad pad = Gamepad.current;

        return (mouse != null && mouse.leftButton.wasReleasedThisFrame)
            || (pad != null && pad.leftTrigger.wasReleasedThisFrame);
    }

    /// <summary>「投げる」ボタンが離された瞬間か。</summary>
    public static bool ThrowReleased()
    {
        Mouse mouse = Mouse.current;
        Gamepad pad = Gamepad.current;

        return (mouse != null && mouse.rightButton.wasReleasedThisFrame)
            || (pad != null && pad.rightTrigger.wasReleasedThisFrame);
    }

    /// <summary>「投げる」ボタンが押された瞬間か。右クリックか、コントローラーの RT。</summary>
    public static bool ThrowPressed()
    {
        Mouse mouse = Mouse.current;
        Gamepad pad = Gamepad.current;

        return (mouse != null && mouse.rightButton.wasPressedThisFrame)
            || (pad != null && pad.rightTrigger.wasPressedThisFrame);
    }

    /// <summary>
    /// **引っ張る（の仕上げ）。** 釣り式と同じ軌道で頭上を越え、後ろへ抜けたところで手を離し、
    /// **そのまま後ろへ飛ばす**（プレイヤー→物資の向き＝物資が来た方の反対）。
    /// 真ん中に近いほど強く、強いほど後ろの遠くまで飛んでいく。
    /// </summary>
    private void FinishAsPull(float accuracy)
    {
        Vector3 away = target.transform.position - hook.PlayerRoot.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
        {
            away = -hook.CurrentAimDirection;
        }

        float force = Mathf.Lerp(pullForceMin, pullForceMax, accuracy);

        Debug.Log($"引っ張った：真ん中への近さ {accuracy:0.00}（力 {force:0.0}）");

        Launch(away.normalized, force, pullLift);
    }

    /// <summary>
    /// **投げる。** プレイヤーから「物資がくっついていた場所」へ向かって飛ばす（来た方へ投げ返す）。
    /// 真ん中に近いほど強い。
    /// </summary>
    private void FinishAsTwoButtonThrow(float accuracy)
    {
        Vector3 toOrigin = reelStart - hook.PlayerRoot.position;
        toOrigin.y = 0f;
        if (toOrigin.sqrMagnitude < 0.0001f)
        {
            toOrigin = hook.CurrentAimDirection;
        }

        float force = Mathf.Lerp(twoButtonThrowForceMin, twoButtonThrowForceMax, accuracy);

        Debug.Log($"投げた：真ん中への近さ {accuracy:0.00}（力 {force:0.0}）");

        Launch(toOrigin.normalized, force, throwLift);
    }

    /// <summary>
    /// 物資に力を加えて手を離す。**オンラインではホストが力を加える**（どこへ飛んだかを全員でそろえるため）。
    /// </summary>
    private void Launch(Vector3 direction, float force, float lift)
    {
        Rigidbody body = target.Body;
        RestorePhysics(body);

        FishingNetSupply netSupply = GetNetSupply();

        if (netSupply != null)
        {
            netSupply.RequestThrow(direction, force, lift, hook.LocalPlayerIndex);
        }
        else
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.AddForce(direction * force + Vector3.up * lift, ForceMode.Impulse);
        }

        EndPull();
    }

    /// <summary>宇宙ごみ式のとき、いま押せるボタンを案内する（ゲージの少し上）。</summary>
    private void OnGUI()
    {
        if (!active || style != ThrowStyle.TwoButtons || stage == TwoButtonStage.Lifting || stage == TwoButtonStage.PullTravel || GamePause.IsPaused)
        {
            return;
        }

        string text = stage == TwoButtonStage.AimPull
            ? "左クリック（LT）：引っ張る　真ん中ほど強い"
            : "右クリック（RT）：投げる　真ん中ほど強い";

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height / 1080f, 0.6f, 2f) * 22f),
            fontStyle = FontStyle.Bold
        };

        Rect rect = new Rect(0f, Screen.height * 0.70f, Screen.width, labelStyle.fontSize * 1.6f);

        Color saved = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, labelStyle);
        GUI.color = Color.white;
        GUI.Label(rect, text, labelStyle);
        GUI.color = saved;
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

        Debug.Log($"スキルチェック成功：{zone.label} / {tier} / {zone.direction} へ（力 {force}）");

        // **オンラインでは、力を加えるのはホスト。**
        // 手元で加えると、どこへ飛んだかが人によって変わってしまう
        FishingNetSupply netSupply = GetNetSupply();

        if (netSupply != null)
        {
            netSupply.RequestThrow(direction, force, throwLift, hook.LocalPlayerIndex);
        }
        else
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.AddForce(direction * force + Vector3.up * throwLift, ForceMode.Impulse);
        }

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

        Debug.Log($"スキルチェック失敗（{reason}）：少しだけ引き寄せて終了");

        FishingNetSupply netSupply = GetNetSupply();

        if (netSupply != null)
        {
            netSupply.RequestRelease(toPlayer.normalized, missPullForce, hook.LocalPlayerIndex);
        }
        else
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.AddForce(toPlayer.normalized * missPullForce, ForceMode.Impulse);
        }

        EndPull();
    }

    /// <summary>
    /// オンラインで、引っ掛けが**ホストに認められなかった**とき。
    /// 物理には触らず（持ち主はホストのまま）、引っ張りだけをやめる。
    /// </summary>
    public void AbandonBecauseLost()
    {
        if (!active)
        {
            return;
        }

        target = null;
        active = false;

        if (hook != null && hook.UI != null)
        {
            hook.UI.ShowTiming(false);
        }
    }

    /// <summary>
    /// いま引っ張っている物資のオンライン部品。
    /// **オンラインでないとき、または部品が無いときは null**（そのときは手元で処理する）。
    /// </summary>
    private FishingNetSupply GetNetSupply()
    {
        if (hook == null || !hook.IsOnline || target == null)
        {
            return null;
        }

        return target.GetComponent<FishingNetSupply>();
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

    /// <summary>爆風で物資を引き離す。投げ・ミスの通信や力を追加せず、糸とゲージを戻す。</summary>
    public static void ReleaseTargetForExplosion(HookableObject item)
    {
        foreach (ThrowController puller in FindObjectsByType<ThrowController>(FindObjectsSortMode.None))
        {
            if (!puller.active || puller.target != item)
            {
                continue;
            }

            FishingNetSupply netSupply = item.GetComponent<FishingNetSupply>();
            // 持ち主を失ったPCでは物理を再開しない（ホストが吹き飛ばす）。
            if (netSupply == null || !netSupply.IsSpawned || netSupply.IsOwner)
            {
                puller.RestorePhysics(item.Body);
            }
            puller.EndPull();
        }
        item.SetHooked(false);
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

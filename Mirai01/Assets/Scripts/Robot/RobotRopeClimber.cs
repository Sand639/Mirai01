using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **ロープにつかまって、上下に移動する。**
/// （VALORANT のマップにある、上下に移動できる紐と同じ遊び方）
///
/// 遊び方：
///   ロープに近づくと、ロープの**色が変わる**（つかまれる合図）
///   F を押すと**つかまる**
///   W / S で**上下に移動する**
///   もう一度 F を押すと**手を離す**（そのまま落ちる）
///   Space を押すと**見ている方向へ飛び降りる**
///
/// **つかまれるのは「手のある体」だけ。**
/// 合体中と上半身はつかまれるが、**下半身だけのときはつかまれない。**
/// 物を持つとき（<see cref="RobotGrabber"/>）と同じ決まりで、
/// **体に「手の位置」が入っているかどうか**で決まる（<see cref="RobotBody.HoldPoint"/>）。
///
/// ## 動かし方
///
/// つかまっている間は、<see cref="RobotController.MovementSuspended"/> をONにして
/// **普段の移動を止め、この部品が体を直接動かしている。**
/// 両方が同時に動かすと、引っ張り合いになってガタつくため。
///
/// ## F キーはロープが優先
///
/// 物を持つのと同じ F キーを使っている。
/// **ロープを狙っているとき・つかまっているときはロープが優先**され、
/// それ以外のときは <see cref="RobotGrabber"/> が受け取る。
/// （ただし、すでに物を持っているときは、物を離すほうが優先）
/// </summary>
[RequireComponent(typeof(RobotController))]
public class RobotRopeClimber : MonoBehaviour
{
    [Header("届く範囲")]
    [Tooltip("この距離まで近づくとつかまれる（メートル）")]
    [Range(0.5f, 6f)]
    [SerializeField] private float reach = 2.5f;

    [Tooltip("正面からどれだけ横にずれていてもつかまれるか（度）")]
    [Range(10f, 180f)]
    [SerializeField] private float maxAngle = 90f;

    [Header("上り下り")]
    [Tooltip("上り下りする速さ（1秒あたりのメートル）")]
    [Range(0.5f, 12f)]
    [SerializeField] private float climbSpeed = 3.5f;

    [Tooltip("ロープの位置まで引き寄せられる速さ。大きいほど一瞬でくっつく")]
    [Range(1f, 30f)]
    [SerializeField] private float snapSpeed = 8f;

    [Tooltip("ONにすると、一番下まで下りたときに自動で手を離す")]
    [SerializeField] private bool releaseAtBottom = true;

    [Header("飛び降りる")]
    [Tooltip("Space で飛び降りるときに、前へ飛ぶ勢い。0にすると真下に落ちる")]
    [Range(0f, 15f)]
    [SerializeField] private float jumpOffSpeed = 4f;

    [Tooltip("Space で飛び降りるときに、上へ跳ねる勢い")]
    [Range(0f, 15f)]
    [SerializeField] private float jumpOffUp = 3f;

    [Header("キーの割り当て")]
    [Tooltip("つかまる／手を離すキー。物を持つキーと同じにしてよい")]
    [SerializeField] private Key grabKey = Key.F;

    [Header("見た目")]
    [Tooltip("画面中央のレティクル。つかまれるときに色が変わる")]
    [SerializeField] private Reticle reticle;

    [Tooltip("つかまれるロープを、この色に近づける")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.35f);

    [Tooltip("どれくらい色を混ぜるか")]
    [Range(0f, 1f)]
    [SerializeField] private float highlightStrength = 0.6f;

    private RobotController controller;

    /// <summary>物を持つ側。F キーをどちらが受け取るか決めるために見ている（無くてもよい）。</summary>
    private RobotGrabber grabber;

    private readonly InteractHighlight highlight = new InteractHighlight();

    /// <summary>いまつかまっているロープ。つかまっていなければ null。</summary>
    public RobotRope AttachedRope { get; private set; }

    /// <summary>いま狙えているロープ。無ければ null。</summary>
    public RobotRope AimedRope { get; private set; }

    /// <summary>いまロープにつかまっているか。</summary>
    public bool IsAttached => AttachedRope != null;

    /// <summary>
    /// **このフレームの F キーを、ロープ側が受け取るか。**
    /// <see cref="RobotGrabber"/> がこれを見て、物を持つ処理を止める。
    /// </summary>
    public bool WantsInteract => IsAttached || (AimedRope != null && !GrabberIsHolding);

    /// <summary>いま操作している体が、ロープにつかまれるか（手のある体か）。</summary>
    public bool CanClimbNow =>
        controller != null && controller.ActiveBody != null && controller.ActiveBody.CanHold;

    private bool GrabberIsHolding => grabber != null && grabber.Held != null;

    /// <summary>つかまっている体。</summary>
    private RobotBody attachedBody;

    /// <summary>いまつかまっている高さ（ロープの下端からの距離）。</summary>
    private float holdHeight;

    /// <summary>ロープのどちら側にぶら下がっているか（水平の向き）。反対側へ回り込まないように覚えておく。</summary>
    private Vector3 hangSide;

    private void Awake()
    {
        controller = GetComponent<RobotController>();
        grabber = GetComponent<RobotGrabber>();
    }

    private void OnEnable()
    {
        if (controller != null)
        {
            controller.ControlSwitched += HandleControlSwitched;
        }
    }

    private void OnDisable()
    {
        if (controller != null)
        {
            controller.ControlSwitched -= HandleControlSwitched;
        }

        Detach();
    }

    private void Update()
    {
        if (IsAttached)
        {
            UpdateAttached();
        }
        else
        {
            UpdateAim();
        }

        if (WasGrabKeyPressed())
        {
            ToggleHold();
        }
    }

    // ------------------------------------------------------------
    // 狙う
    // ------------------------------------------------------------

    /// <summary>正面の近くにあるロープを探して、色を変える。</summary>
    private void UpdateAim()
    {
        RobotRope found = CanClimbNow ? FindNearestRope() : null;

        if (found == AimedRope)
        {
            return;
        }

        AimedRope = found;

        highlight.Clear();

        if (AimedRope != null)
        {
            highlight.Apply(AimedRope.gameObject, highlightColor, highlightStrength);
        }

        if (reticle != null)
        {
            reticle.SetHighlight(AimedRope != null ? highlightColor : (Color?)null);
        }
    }

    /// <summary>
    /// 正面に近くて一番近いロープを返す。
    ///
    /// 距離は**ロープ全体との距離ではなく、一番近い点との距離**で測る。
    /// 長いロープでも、目の前の部分を見ていればつかまれるようにするため。
    /// </summary>
    private RobotRope FindNearestRope()
    {
        RobotBody body = controller.ActiveBody;
        Transform hand = body.HoldPoint;

        RobotRope nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (RobotRope rope in RobotRope.All)
        {
            if (rope == null)
            {
                continue;
            }

            Vector3 point = rope.ClosestPoint(hand.position);
            Vector3 toRope = point - hand.position;
            float distance = toRope.magnitude;

            if (distance > reach || distance >= nearestDistance)
            {
                continue;
            }

            // 正面から大きく外れているものは無視する
            if (Vector3.Angle(body.transform.forward, toRope) > maxAngle)
            {
                continue;
            }

            nearest = rope;
            nearestDistance = distance;
        }

        return nearest;
    }

    // ------------------------------------------------------------
    // つかまる・手を離す
    // ------------------------------------------------------------

    /// <summary>つかまっていなければつかまり、つかまっていれば手を離す。</summary>
    public void ToggleHold()
    {
        if (IsAttached)
        {
            Detach();
            return;
        }

        if (AimedRope != null && CanClimbNow)
        {
            Attach(AimedRope);
        }
    }

    /// <summary>ロープにつかまる。</summary>
    public void Attach(RobotRope rope)
    {
        RobotBody body = controller.ActiveBody;

        if (rope == null || body == null || !body.CanHold)
        {
            return;
        }

        AttachedRope = rope;
        attachedBody = body;

        holdHeight = Mathf.Clamp(rope.HeightOf(body.transform.position), rope.MinHold, rope.MaxHold);

        // **近づいた側にぶら下がる。** 反対側へ勝手に回り込むと、
        // 足場から落ちたように見えてしまうため
        Vector3 side = body.transform.position - rope.PointAt(holdHeight);
        side -= Vector3.Project(side, rope.Direction);

        hangSide = side.sqrMagnitude > 0.0001f
            ? side.normalized
            : -Flat(body.transform.forward);

        highlight.Clear();
        AimedRope = null;

        // 普段の移動を止める。ここから先はこの部品が体を動かす
        controller.MovementSuspended = true;

        Debug.Log($"[ROBOT] {body.name} が {rope.name} につかまりました");
    }

    /// <summary>手を離す。そのまま落ちる。</summary>
    public void Detach()
    {
        if (!IsAttached)
        {
            return;
        }

        RobotRope rope = AttachedRope;

        AttachedRope = null;
        attachedBody = null;

        if (controller != null)
        {
            controller.MovementSuspended = false;
        }

        if (reticle != null)
        {
            reticle.SetHighlight(null);
        }

        Debug.Log($"[ROBOT] {(rope != null ? rope.name : "ロープ")} から手を離しました");
    }

    /// <summary>見ている方向へ飛び降りる。</summary>
    private void JumpOff()
    {
        RobotBody body = attachedBody;

        Vector3 forward = Flat(controller.LookForward);

        Detach();

        if (body != null)
        {
            body.Launch(forward * jumpOffSpeed + Vector3.up * jumpOffUp);
        }
    }

    // ------------------------------------------------------------
    // つかまっている間
    // ------------------------------------------------------------

    private void UpdateAttached()
    {
        RobotRope rope = AttachedRope;
        RobotBody body = attachedBody;

        // ロープが消えた・体が引っ込んだ場合の保険
        if (rope == null || body == null || !body.gameObject.activeInHierarchy)
        {
            Detach();
            return;
        }

        if (controller.JumpPressedThisFrame)
        {
            JumpOff();
            return;
        }

        // W で上、S で下。カメラの向きは関係しない
        float input = controller.MoveInput.y;

        holdHeight = Mathf.Clamp(
            holdHeight + input * climbSpeed * Time.deltaTime, rope.MinHold, rope.MaxHold);

        // 一番下まで下りたら、自動で手を離して立たせる
        if (releaseAtBottom && input < 0f && holdHeight <= rope.MinHold + 0.01f)
        {
            Detach();
            return;
        }

        Vector3 target = rope.PointAt(holdHeight) + hangSide * rope.HangDistance;
        Vector3 delta = target - body.transform.position;

        // 一度に動ける距離を決めておく。
        // つかまった瞬間の引き寄せも、上り下りも、この1本で足りる
        float step = snapSpeed * Time.deltaTime;

        if (delta.sqrMagnitude > step * step)
        {
            delta = delta.normalized * step;
        }

        body.MoveWithoutGravity(delta, GetFacing());

        // 壁に阻まれて動けなかったぶんを、つかまっている高さにも反映する。
        // ずれたままにすると、あとで一気に引き寄せられてしまう
        holdHeight = Mathf.Clamp(rope.HeightOf(body.transform.position), rope.MinHold, rope.MaxHold);
    }

    /// <summary>
    /// つかまっている間、体をどちらへ向けるか。
    ///
    /// - 見ている方向を向く作り（一人称／フォートナイト型）なら、カメラの向き
    /// - そうでなければ、**ロープのほうを向く**
    /// </summary>
    private Vector3? GetFacing()
    {
        if (controller.FaceLookDirection)
        {
            return Flat(controller.LookForward);
        }

        return -hangSide;
    }

    // ------------------------------------------------------------
    // 体が切り替わったとき
    // ------------------------------------------------------------

    /// <summary>
    /// 分離・合体・操作の切り替えが起きたら、**手を離す。**
    ///
    /// つかまったまま体が入れ替わると、
    /// 出てきた体が空中で止まったように見えてしまうため。
    /// </summary>
    private void HandleControlSwitched(RobotController.RobotState state)
    {
        Detach();
    }

    // ------------------------------------------------------------
    // 補助
    // ------------------------------------------------------------

    /// <summary>上下の成分を落として、水平の向きにする。</summary>
    private static Vector3 Flat(Vector3 direction)
    {
        direction.y = 0f;

        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }

    private bool WasGrabKeyPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[grabKey].wasPressedThisFrame;
    }
}

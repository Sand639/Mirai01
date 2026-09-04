using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ロボットが**目の前の物を持ったり離したりする**。
///
/// 遊び方：
///   目の前の持てる物に近づくと、その物の**色が変わる**（狙えている合図）
///   F を押すと**持つ**
///   もう一度 F を押すと**離す**
///
/// **持てるのは「手のある体」だけ。**
/// 合体中と上半身は持てるが、**下半身だけのときは持てない。**
/// これは状態を見て分岐しているのではなく、
/// **体に「手の位置」が入っているかどうか**で決まる（<see cref="RobotBody.HoldPoint"/>）。
///
/// ## 探し方は「距離と向き」
///
/// 細い物や小さい物でも拾えるように、**線を飛ばして当てる**のではなく、
/// **近くにある持てる物の中から、正面に近くて一番近いもの**を選んでいる。
/// （通信の実装でも、当たり判定より距離のほうが確実だという教訓があった）
/// </summary>
[RequireComponent(typeof(RobotController))]
public class RobotGrabber : MonoBehaviour
{
    [Header("届く範囲")]
    [Tooltip("この距離まで近づくと持てる（メートル）")]
    [Range(0.5f, 5f)]
    [SerializeField] private float reach = 2f;

    [Tooltip("正面からどれだけ横にずれていても持てるか（度）")]
    [Range(10f, 180f)]
    [SerializeField] private float maxAngle = 70f;

    [Tooltip("一度に調べる物の数の上限。増やすと重くなる")]
    [SerializeField] private int maxCandidates = 16;

    [Header("キーの割り当て")]
    [Tooltip("持つ／離すを切り替えるキー")]
    [SerializeField] private Key grabKey = Key.F;

    [Header("見た目")]
    [Tooltip("画面中央のレティクル。狙えているときに色が変わる")]
    [SerializeField] private Reticle reticle;

    [Tooltip("狙えている物を、この色に近づける")]
    [SerializeField] private Color highlightColor = new Color(0.4f, 0.9f, 1f);

    [Tooltip("どれくらい色を混ぜるか")]
    [Range(0f, 1f)]
    [SerializeField] private float highlightStrength = 0.5f;

    [Header("離すとき")]
    [Tooltip("離すときに前へ押し出す強さ。0なら、その場に落とすだけ")]
    [Range(0f, 10f)]
    [SerializeField] private float releasePush = 0f;

    private RobotController controller;

    /// <summary>ロープ側。同じ F キーをどちらが受け取るか決めるために見ている（無くてもよい）。</summary>
    private RobotRopeClimber ropeClimber;

    private readonly Collider[] candidates = new Collider[32];
    private readonly InteractHighlight highlight = new InteractHighlight();

    /// <summary>いま狙えている物。無ければ null。</summary>
    public Grabbable Aimed { get; private set; }

    /// <summary>いま持っている物。無ければ null。</summary>
    public Grabbable Held { get; private set; }

    /// <summary>
    /// いま物を持てる状態か（手のある体を操作しているか）。
    /// **ロープにつかまっている間は持てない。**
    /// </summary>
    public bool CanGrabNow =>
        controller != null && controller.ActiveBody != null && controller.ActiveBody.CanHold
        && (ropeClimber == null || !ropeClimber.IsAttached);

    // 持つ前の状態。離すときに元へ戻すために control しておく
    private Transform heldOriginalParent;
    private bool heldOriginalKinematic;
    private bool heldOriginalGravity;
    private Rigidbody heldBody;
    private Collider heldCollider;
    private Collider ignoredBodyCollider;

    /// <summary>いまどの体が持っているか。切り替えのときに、持ち替えるか判断するのに使う。</summary>
    private RobotBody heldByBody;

    /// <summary>いま操作している体が、物を持っているか。</summary>
    private bool ActiveBodyIsHolding =>
        Held != null && heldByBody != null && heldByBody == controller.ActiveBody;

    private void Awake()
    {
        controller = GetComponent<RobotController>();
        ropeClimber = GetComponent<RobotRopeClimber>();
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
    }

    private void Update()
    {
        // いま操作している体が何も持っていなければ、狙える物を探す
        if (ActiveBodyIsHolding)
        {
            if (Aimed != null)
            {
                ClearHighlight();
                Aimed = null;
                UpdateReticle();
            }
        }
        else
        {
            UpdateAim();
        }

        // ロープを狙っている・つかまっているときは、F キーはロープに譲る
        if (WasGrabKeyPressed() && !RopeHandlesInteract)
        {
            ToggleGrab();
        }
    }

    /// <summary>このフレームの F キーを、ロープ側が受け取るか。</summary>
    private bool RopeHandlesInteract => ropeClimber != null && ropeClimber.WantsInteract;

    // ------------------------------------------------------------
    // 狙う
    // ------------------------------------------------------------

    /// <summary>正面の近くにある「持てる物」を探して、色を変える。</summary>
    private void UpdateAim()
    {
        Grabbable found = CanGrabNow ? FindNearestGrabbable() : null;

        if (found == Aimed)
        {
            return;
        }

        ClearHighlight();
        Aimed = found;

        if (Aimed != null)
        {
            ApplyHighlight(Aimed.gameObject);
        }

        // レティクルの色も変えて、狙えていることを画面中央でも伝える
        UpdateReticle();
    }

    /// <summary>レティクルの見た目を、いまの状況に合わせる。</summary>
    private void UpdateReticle()
    {
        if (reticle == null)
        {
            return;
        }

        reticle.SetHighlight(Aimed != null ? highlightColor : (Color?)null);
    }

    /// <summary>正面に近くて一番近い「持てる物」を返す。</summary>
    private Grabbable FindNearestGrabbable()
    {
        RobotBody body = controller.ActiveBody;
        Transform hand = body.HoldPoint;

        int count = Physics.OverlapSphereNonAlloc(
            hand.position, reach, candidates, ~0, QueryTriggerInteraction.Ignore);

        Grabbable nearest = null;
        float nearestDistance = float.MaxValue;
        int checkedCount = 0;

        for (int i = 0; i < count && checkedCount < maxCandidates; i++)
        {
            Collider hit = candidates[i];

            if (hit == null)
            {
                continue;
            }

            Grabbable grabbable = hit.GetComponentInParent<Grabbable>();

            // 持てない設定の物と、すでに誰かが持っている物は対象外
            if (grabbable == null || grabbable.IsHeld || !grabbable.CanBeCarried)
            {
                continue;
            }

            // 自分の体は対象外
            if (grabbable.transform.IsChildOf(transform))
            {
                continue;
            }

            checkedCount++;

            Vector3 toTarget = grabbable.transform.position - hand.position;

            // 正面から大きく外れているものは無視する
            if (Vector3.Angle(body.transform.forward, toTarget) > maxAngle)
            {
                continue;
            }

            float distance = toTarget.magnitude;

            if (distance < nearestDistance)
            {
                nearest = grabbable;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    // ------------------------------------------------------------
    // 持つ・離す
    // ------------------------------------------------------------

    /// <summary>
    /// 持っていなければ持ち、持っていれば離す。
    ///
    /// **離せるのは、自分で持っている体だけ。**
    /// 上半身が持っている物を、下半身を操作しながら離すことはできない。
    /// </summary>
    public void ToggleGrab()
    {
        if (ActiveBodyIsHolding)
        {
            Release();
            return;
        }

        if (Aimed != null && CanGrabNow)
        {
            Grab(Aimed);
        }
    }

    /// <summary>物を持つ。</summary>
    private void Grab(Grabbable target)
    {
        RobotBody body = controller.ActiveBody;
        Transform hand = body.HoldPoint;

        heldBody = target.GetComponent<Rigidbody>();
        heldCollider = target.GetComponentInChildren<Collider>();

        if (heldBody == null)
        {
            Debug.LogWarning($"{target.name}: Rigidbody が無いので持てません。", target);
            return;
        }

        // 離すときに元へ戻せるよう、いまの状態を控えておく
        heldOriginalParent = target.transform.parent;
        heldOriginalKinematic = heldBody.isKinematic;
        heldOriginalGravity = heldBody.useGravity;

        // 持っている間は物理を止める。動かすのは手の位置なので、
        // 物理に任せると引っ張り合いになる
        heldBody.isKinematic = true;
        heldBody.useGravity = false;

        // 持った物が自分の体を押して、動けなくなるのを防ぐ
        IgnoreCollisionWithBody(body, true);

        ClearHighlight();

        target.transform.SetParent(hand, false);
        target.transform.localPosition = target.HoldOffset;

        if (target.StraightenWhenHeld)
        {
            target.transform.localRotation = Quaternion.identity;
        }

        target.SetHeld(true);
        Held = target;
        heldByBody = body;
        Aimed = null;

        Debug.Log($"[ROBOT] {body.name} が {target.name} を持ちました");
    }

    /// <summary>持っている物を離す。</summary>
    public void Release()
    {
        if (Held == null)
        {
            return;
        }

        Grabbable target = Held;

        target.transform.SetParent(heldOriginalParent, true);

        if (heldBody != null)
        {
            heldBody.isKinematic = heldOriginalKinematic;
            heldBody.useGravity = heldOriginalGravity;

            if (!heldBody.isKinematic && releasePush > 0f)
            {
                Vector3 forward = controller.ActiveBody != null
                    ? controller.ActiveBody.transform.forward
                    : transform.forward;

                heldBody.AddForce(forward * releasePush, ForceMode.Impulse);
            }
        }

        IgnoreCollisionWithBody(null, false);

        target.SetHeld(false);
        Held = null;
        heldByBody = null;
        heldBody = null;
        heldCollider = null;

        Debug.Log($"[ROBOT] {target.name} を離しました");
    }

    /// <summary>持った物と、体の当たり判定をぶつけないようにする（または戻す）。</summary>
    private void IgnoreCollisionWithBody(RobotBody body, bool ignore)
    {
        if (heldCollider == null)
        {
            return;
        }

        if (ignore)
        {
            ignoredBodyCollider = body != null ? body.BodyCollider : null;
        }

        if (ignoredBodyCollider != null)
        {
            Physics.IgnoreCollision(heldCollider, ignoredBodyCollider, ignore);
        }

        if (!ignore)
        {
            ignoredBodyCollider = null;
        }
    }

    // ------------------------------------------------------------
    // 体が切り替わったとき
    // ------------------------------------------------------------

    /// <summary>
    /// 分離・合体・操作の切り替えが起きたときに呼ばれる。
    ///
    /// 判断は2通り。
    ///
    /// - **持っている体がまだ出ているなら、そのまま持たせておく。**
    ///   （上半身に持たせたまま、下半身を操作しに行ける）
    /// - **持っている体が消えたなら**（合体・分離で入れ替わったとき）、
    ///   新しい体に手があれば持ち替え、無ければその場に落とす。
    ///   放っておくと、消えた体の手に付いたまま**物まで見えなくなる**ため
    /// </summary>
    private void HandleControlSwitched(RobotController.RobotState state)
    {
        if (Held == null)
        {
            return;
        }

        // 持っている体がまだ出ているなら、何もしなくてよい
        if (heldByBody != null && heldByBody.gameObject.activeInHierarchy)
        {
            return;
        }

        RobotBody body = controller.ActiveBody;

        if (body == null || !body.CanHold)
        {
            Debug.Log("[ROBOT] 持てる体がいなくなったので、持っていた物を離しました");
            Release();
            return;
        }

        // 新しい体の手に持ち替える
        IgnoreCollisionWithBody(null, false);
        IgnoreCollisionWithBody(body, true);

        Held.transform.SetParent(body.HoldPoint, false);
        Held.transform.localPosition = Held.HoldOffset;

        if (Held.StraightenWhenHeld)
        {
            Held.transform.localRotation = Quaternion.identity;
        }

        heldByBody = body;

        Debug.Log($"[ROBOT] 持っていた物を {body.name} に持ち替えました");
    }

    // ------------------------------------------------------------
    // 色を変える
    //
    // 中身は <see cref="InteractHighlight"/> にまとめてある（ロープと共通）
    // ------------------------------------------------------------

    private void ApplyHighlight(GameObject target)
    {
        highlight.Apply(target, highlightColor, highlightStrength);
    }

    private void ClearHighlight()
    {
        highlight.Clear();
    }

    private bool WasGrabKeyPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[grabKey].wasPressedThisFrame;
    }
}

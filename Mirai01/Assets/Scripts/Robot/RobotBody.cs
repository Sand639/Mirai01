using UnityEngine;

/// <summary>
/// ロボットの体1つ分の動き。
///
/// 合体した体・上半身・下半身の**それぞれに1つずつ**付ける。
/// 「どう動くか」だけを持っていて、「今どれを操作しているか」は
/// <see cref="RobotController"/> が決める。
///
/// 物理演算（Rigidbody）ではなく **CharacterController** で動かしている。
/// 揺れたり弾かれたりせず、指示したとおりに動くため。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class RobotBody : MonoBehaviour
{
    [Header("動き")]
    [Tooltip("進む速さ（1秒あたりのメートル）")]
    [Range(1f, 15f)]
    [SerializeField] private float moveSpeed = 5f;

    [Tooltip("進む方向へ向き直る速さ（1秒あたりの角度）")]
    [Range(90f, 1440f)]
    [SerializeField] private float turnSpeed = 720f;

    [Tooltip("落ちる強さ。マイナスの値にすること")]
    [SerializeField] private float gravity = -20f;

    [Header("向きによる速さの違い")]
    [Tooltip("横に動くときの速さ（前を1としたときの割合）。0.5なら50%減")]
    [Range(0.1f, 1f)]
    [SerializeField] private float sideSpeedRate = 0.5f;

    [Tooltip("後ろに動くときの速さ（前を1としたときの割合）。0.7なら30%減")]
    [Range(0.1f, 1f)]
    [SerializeField] private float backSpeedRate = 0.7f;

    [Header("ジャンプ")]
    [Tooltip("ジャンプで上がる高さ（メートル）。0にするとその場で跳ねなくなる")]
    [Range(0f, 5f)]
    [SerializeField] private float jumpHeight = 1.2f;

    [Header("物を持つ")]
    [Tooltip("持った物を置く場所。**手のある体だけ**に入れる（下半身には入れない）")]
    [SerializeField] private Transform holdPoint;

    [Header("一人称のとき")]
    [Tooltip("足元から目の高さまで（メートル）。一人称のとき、ここにカメラが来る")]
    [SerializeField] private float eyeHeight = 1.5f;

    /// <summary>この体の目の高さ。一人称のカメラ位置に使う。</summary>
    public float EyeHeight => eyeHeight;

    /// <summary>この体自身の見た目。持った物は含まない（起動時に控えておく）。</summary>
    private Renderer[] ownVisuals;

    [Header("外から与えられた勢い")]
    [Tooltip("勢いが弱まる速さ。大きいほど早く止まる")]
    [Range(0.5f, 20f)]
    [SerializeField] private float launchDamping = 3f;

    private CharacterController characterController;
    private float verticalVelocity;

    /// <summary>
    /// 切り離しなどで外から与えられた勢い（水平方向）。
    /// 自分で歩く速さとは別に足され、だんだん弱まる。
    /// </summary>
    private Vector3 launchVelocity;

    /// <summary>この体の進む速さ。</summary>
    public float MoveSpeed => moveSpeed;

    /// <summary>
    /// 持った物を置く場所。**入っていなければ、この体は物を持てない。**
    ///
    /// 「合体中と上半身は持てる、下半身は持てない」という決まりを、
    /// 状態を見て分岐するのではなく**体そのものに持たせている。**
    /// あとから体を増やしても、ここを入れるかどうかだけで決まる。
    /// </summary>
    public Transform HoldPoint => holdPoint;

    /// <summary>この体は物を持てるか。</summary>
    public bool CanHold => holdPoint != null;

    /// <summary>当たり判定。持った物とぶつからないようにするために使う。</summary>
    public Collider BodyCollider => characterController;

    /// <summary>いま地面に足が付いているか。UIの表示などに使える。</summary>
    public bool IsGrounded => characterController != null && characterController.isGrounded;

    /// <summary>
    /// この体の高さ（メートル）。
    /// **切り離したときに、上半身をどの高さに出すかを計算するのに使う。**
    /// 決め打ちの数字を書かずに済む。
    /// </summary>
    public float Height
    {
        get
        {
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }

            return characterController != null ? characterController.height : 0f;
        }
    }

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        // **物を持つ前に**控えておくのが大事。
        // 持った物は手の下にぶら下がるので、あとから数えると一緒に消えてしまう
        ownVisuals = GetComponentsInChildren<Renderer>(true);
    }

    /// <summary>
    /// この体の見た目を出したり隠したりする。
    /// **一人称のときに、自分の体で視界がふさがるのを防ぐ**ために使う。
    ///
    /// 持っている物は隠さない（手に持った物は見えていてよい）。
    /// </summary>
    public void SetVisualVisible(bool visible)
    {
        if (ownVisuals == null)
        {
            ownVisuals = GetComponentsInChildren<Renderer>(true);
        }

        foreach (Renderer renderer in ownVisuals)
        {
            if (renderer != null)
            {
                renderer.enabled = visible;
            }
        }
    }

    /// <summary>
    /// 1フレーム分動かす。
    /// direction は進みたい向き（長さ1以内、水平）。止めたいときは Vector3.zero を渡す。
    /// </summary>
    public void Tick(Vector3 direction)
    {
        Tick(direction, false);
    }

    /// <summary>
    /// 1フレーム分動かす。
    /// jumpRequested に true を渡すと、**地面に付いていればジャンプする**。
    /// 「誰がジャンプできるか」は <see cref="RobotController"/> が決めるので、
    /// ここでは渡されたとおりに動くだけにしてある。
    /// </summary>
    public void Tick(Vector3 direction, bool jumpRequested)
    {
        Tick(direction, jumpRequested, null);
    }

    /// <summary>
    /// 1フレーム分動かす。
    ///
    /// `faceDirection` に向きを渡すと、**進む方向ではなくその向きを向く。**
    /// 一人称のときに「見ている方向を向く」ために使う。
    /// null を渡すと、これまでどおり進む方向へ向き直る。
    /// </summary>
    public void Tick(Vector3 direction, bool jumpRequested, Vector3? faceDirection)
    {
        if (characterController == null)
        {
            return;
        }

        bool grounded = characterController.isGrounded;

        if (grounded && verticalVelocity <= 0f)
        {
            // 地面に押し付けておかないと、坂で浮いてガタつく
            verticalVelocity = -2f;
        }

        if (jumpRequested && grounded && jumpHeight > 0f)
        {
            // 「この高さまで上がる」速さを逆算して入れる
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        else if (!grounded)
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        Vector3 velocity = direction * (moveSpeed * GetSpeedRate(direction, faceDirection));

        // 切り離しなどで与えられた勢いを足す。時間とともに弱まる
        velocity += launchVelocity;
        launchVelocity = Vector3.Lerp(
            launchVelocity, Vector3.zero, 1f - Mathf.Exp(-launchDamping * Time.deltaTime));

        velocity.y = verticalVelocity;
        characterController.Move(velocity * Time.deltaTime);

        ApplyRotation(direction, faceDirection);
    }

    /// <summary>
    /// **外から勢いを与える。** 切り離したときに飛ばすのに使う。
    ///
    /// 水平方向の勢いはだんだん弱まる。
    /// 上向きの成分は、ジャンプと同じ扱いになる（重力で落ちてくる）。
    /// </summary>
    public void Launch(Vector3 velocity)
    {
        launchVelocity = new Vector3(velocity.x, 0f, velocity.z);

        if (velocity.y > 0f)
        {
            verticalVelocity = velocity.y;
        }
    }

    /// <summary>
    /// **重力を無視して、そのまま動かす。**
    /// ロープにつかまっている間など、**落ちてほしくないとき**に使う。
    ///
    /// 落ちる勢いも、切り離しで与えられた勢いも、毎回ゼロに戻している。
    /// つかまっている間に落ちる速さが溜まっていると、
    /// **手を離した瞬間に急降下してしまう**ため。
    ///
    /// 壁や天井にはぶつかる（<see cref="CharacterController"/> 越しに動かしているため）。
    /// </summary>
    public void MoveWithoutGravity(Vector3 delta, Vector3? faceDirection)
    {
        if (characterController == null)
        {
            return;
        }

        verticalVelocity = 0f;
        launchVelocity = Vector3.zero;

        characterController.Move(delta);

        ApplyRotation(Vector3.zero, faceDirection);
    }

    /// <summary>
    /// **進む向きによって速さを変える。**
    ///
    /// 体が「見ている方向」を向いているとき（＝向きが固定されているとき）だけ働く。
    /// 進む方向へ向き直る作りのときは、常に前を向いているので差が出ない。
    ///
    /// | 進む向き | 速さ |
    /// | 前 | そのまま |
    /// | 横 | `sideSpeedRate`（初期値0.5＝50%減） |
    /// | 後ろ | `backSpeedRate`（初期値0.7＝30%減） |
    ///
    /// 斜めのときは、前後と左右の割合で混ぜる。
    /// 「斜め前」なら前と横の中間の速さになる。
    /// </summary>
    private float GetSpeedRate(Vector3 direction, Vector3? faceDirection)
    {
        if (!faceDirection.HasValue || direction.sqrMagnitude < 0.0001f)
        {
            return 1f;
        }

        Vector3 forward = faceDirection.Value;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
        {
            return 1f;
        }

        forward.Normalize();

        Vector3 move = direction;
        move.y = 0f;
        move.Normalize();

        // 前後の成分と、左右の成分に分ける
        float forwardAmount = Vector3.Dot(move, forward);
        float sideAmount = Mathf.Abs(Vector3.Dot(move, Vector3.Cross(Vector3.up, forward)));

        float forwardRate = forwardAmount >= 0f ? 1f : backSpeedRate;
        float total = Mathf.Abs(forwardAmount) + sideAmount;

        if (total < 0.0001f)
        {
            return 1f;
        }

        // それぞれの割合で混ぜる。まっすぐ前なら1、真横なら sideSpeedRate になる
        return (Mathf.Abs(forwardAmount) * forwardRate + sideAmount * sideSpeedRate) / total;
    }

    /// <summary>
    /// 体の向きを決める。
    ///
    /// - `faceDirection` が渡されていれば、**止まっていてもその向きを向く**（一人称のとき）
    /// - 渡されていなければ、**進む方向へ向き直る**（三人称のとき）
    /// </summary>
    private void ApplyRotation(Vector3 direction, Vector3? faceDirection)
    {
        if (faceDirection.HasValue)
        {
            Vector3 look = faceDirection.Value;
            look.y = 0f;

            if (look.sqrMagnitude > 0.0001f)
            {
                // 見ている方向にぴったり合わせる。
                // 遅れて付いてくると、狙った所と体の向きがずれて分かりにくいため
                transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            }

            return;
        }

        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, target, turnSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// 指定した場所へ移す。
    /// CharacterController は動いている最中に位置を変えると暴れるので、
    /// **いったん切ってから**動かしている。
    /// </summary>
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        bool wasEnabled = characterController.enabled;
        characterController.enabled = false;

        transform.SetPositionAndRotation(position, rotation);

        characterController.enabled = wasEnabled;
        verticalVelocity = 0f;
        launchVelocity = Vector3.zero;
    }
}

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

    private CharacterController characterController;
    private float verticalVelocity;

    /// <summary>この体の進む速さ。</summary>
    public float MoveSpeed => moveSpeed;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    /// <summary>
    /// 1フレーム分動かす。
    /// direction は進みたい向き（長さ1以内、水平）。止めたいときは Vector3.zero を渡す。
    /// </summary>
    public void Tick(Vector3 direction)
    {
        if (characterController == null)
        {
            return;
        }

        if (characterController.isGrounded)
        {
            // 地面に押し付けておかないと、坂で浮いてガタつく
            verticalVelocity = -2f;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        Vector3 velocity = direction * moveSpeed;
        velocity.y = verticalVelocity;
        characterController.Move(velocity * Time.deltaTime);

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
    }
}

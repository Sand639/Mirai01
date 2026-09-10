using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **見下ろし視点のプレイヤー操作。**
/// WASD で地面を移動し、体はいつもマウスカーソルの方向を向く。
///
/// 向きの計算は <see cref="PlayerAimController"/> に任せ、ここは「動かす」だけに絞っている。
/// フックの発射などはこのスクリプトには入れない（<see cref="HookController"/> が担当）。
///
/// 使い方：CharacterController の付いた体に、これと PlayerAimController を付ける。
/// Input Actions に Assets/InputSystem_Actions を入れる。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FishingPlayerController : MonoBehaviour
{
    [Header("移動")]
    [Tooltip("歩く速さ（1秒あたりのメートル）")]
    [SerializeField] private float moveSpeed = 6f;

    [Tooltip("落ちる強さ。マイナスの値にすること")]
    [SerializeField] private float gravity = -20f;

    [Header("向き")]
    [Tooltip("マウス方向へ向き直る速さ（1秒あたりの度）。大きいほどキビキビ振り向く")]
    [SerializeField] private float turnSpeed = 900f;

    [Header("参照")]
    [Tooltip("同じ体に付いている PlayerAimController を入れる")]
    [SerializeField] private PlayerAimController aim;

    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    [Tooltip("スタン（動けない状態）の管理。スタン中は移動できなくなる")]
    [SerializeField] private PlayerStun stun;

    private CharacterController characterController;
    private InputActionMap playerMap;
    private InputAction moveAction;
    private float verticalVelocity;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (aim == null)
        {
            aim = GetComponent<PlayerAimController>();
        }

        if (stun == null)
        {
            stun = GetComponent<PlayerStun>();
        }

        if (inputActions == null)
        {
            Debug.LogError($"{name}: 入力の設定（InputSystem_Actions）が入っていません。", this);
            enabled = false;
            return;
        }

        playerMap = inputActions.FindActionMap("Player", true);
        moveAction = playerMap.FindAction("Move", true);
    }

    private void Start()
    {
        // 見下ろしでマウスを狙いに使うので、カーソルは固定せず表示したままにする。
        // （PlayerRig 由来の FPS 操作が万一残っていてカーソルをロックしても、ここで戻す）
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnEnable()
    {
        playerMap?.Enable();
    }

    private void OnDisable()
    {
        playerMap?.Disable();
    }

    private void Update()
    {
        // ポーズ中は何もしない（既存の PlayerController と同じ作法）
        if (GamePause.IsPaused)
        {
            return;
        }

        Move();
        FaceCursor();
    }

    /// <summary>WASD で水平移動し、重力で下に落とす。**スタン中は横に動かない。**</summary>
    private void Move()
    {
        Vector3 direction = Vector3.zero;

        // スタン中は移動禁止（向きを変えることと、落ちることはできる）。
        // 試合が終わったあと（結果画面）も動かせない
        if ((stun == null || !stun.IsStunned) && FishingMatch.PlayAllowed)
        {
            Vector2 input = moveAction.ReadValue<Vector2>();

            // 画面の上下左右＝ワールドの XZ。見下ろしカメラは真上から見ているのでこれで合う
            direction = new Vector3(input.x, 0f, input.y);
            if (direction.sqrMagnitude > 1f)
            {
                direction.Normalize();
            }
        }

        if (characterController.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = direction * moveSpeed;
        velocity.y = verticalVelocity;

        characterController.Move(velocity * Time.deltaTime);
    }

    /// <summary>体をマウスカーソルの方向へ向ける。</summary>
    private void FaceCursor()
    {
        if (aim == null || !aim.HasAim)
        {
            return;
        }

        Quaternion target = Quaternion.LookRotation(aim.AimDirection, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, target, turnSpeed * Time.deltaTime);
    }
}

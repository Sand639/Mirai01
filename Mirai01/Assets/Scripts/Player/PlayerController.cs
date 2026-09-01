using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// カプセルのキャラクターを WASD で動かし、マウスで視点を回す。
/// TPS / FPS の切り替えは PlayerViewSwitcher が担当する。
///
/// 使い方：Prefabs/PlayerRig をシーンに置くだけ。
/// 参照はすべてインスペクターから受け取るので、他のオブジェクトを名前で探しにいかない。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("移動")]
    [Tooltip("歩く速さ（1秒あたりのメートル）")]
    [SerializeField] private float walkSpeed = 4f;

    [Tooltip("ダッシュ中の速さ（1秒あたりのメートル）")]
    [SerializeField] private float sprintSpeed = 7f;

    [Tooltip("ジャンプで上がる高さ（メートル）")]
    [SerializeField] private float jumpHeight = 1.2f;

    [Tooltip("落ちる強さ。マイナスの値にすること")]
    [SerializeField] private float gravity = -20f;

    [Header("視点")]
    [Tooltip("マウスを動かしたときに回る量。大きいほど速く回る")]
    [SerializeField] private float mouseSensitivity = 0.12f;

    [Tooltip("上を向ける限界の角度")]
    [SerializeField] private float maxLookUp = 80f;

    [Tooltip("下を向ける限界の角度")]
    [SerializeField] private float maxLookDown = 80f;

    [Tooltip("上下の首振りをさせる場所。PlayerRig の中の CameraPivot を入れる")]
    [SerializeField] private Transform cameraPivot;

    [Header("入力")]
    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("マウスカーソル")]
    [Tooltip("再生したらカーソルを消して画面に固定する")]
    [SerializeField] private bool lockCursorOnStart = true;

    private CharacterController characterController;
    private InputActionMap playerMap;
    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction jumpAction;
    private InputAction sprintAction;

    /// <summary>上下の首振り角度。カメラ側から見たいときのために公開している。</summary>
    public float Pitch { get; private set; }

    private float verticalVelocity;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (inputActions == null)
        {
            Debug.LogError($"{name}: 入力の設定（InputSystem_Actions）が入っていません。インスペクターで設定してください。", this);
            enabled = false;
            return;
        }

        playerMap = inputActions.FindActionMap("Player", true);
        moveAction = playerMap.FindAction("Move", true);
        lookAction = playerMap.FindAction("Look", true);
        jumpAction = playerMap.FindAction("Jump", true);
        sprintAction = playerMap.FindAction("Sprint", true);

        if (cameraPivot == null)
        {
            Debug.LogError($"{name}: CameraPivot が入っていません。上下の首振りができません。", this);
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

    private void Start()
    {
        if (lockCursorOnStart)
        {
            SetCursorLocked(true);
        }
    }

    private void Update()
    {
        UpdateCursor();
        Look();
        Move();
    }

    /// <summary>マウスの動きで、体を左右に・カメラを上下に回す。</summary>
    private void Look()
    {
        Vector2 look = lookAction.ReadValue<Vector2>() * mouseSensitivity;

        // 左右は体ごと回す
        transform.Rotate(Vector3.up, look.x, Space.World);

        // 上下はカメラの根元だけ回す（体は傾けない）
        Pitch = Mathf.Clamp(Pitch - look.y, -maxLookUp, maxLookDown);
        if (cameraPivot != null)
        {
            cameraPivot.localRotation = Quaternion.Euler(Pitch, 0f, 0f);
        }
    }

    /// <summary>WASD で移動し、重力とジャンプを処理する。</summary>
    private void Move()
    {
        Vector2 input = moveAction.ReadValue<Vector2>();
        Vector3 direction = transform.right * input.x + transform.forward * input.y;

        // 斜め移動が速くなりすぎないようにする
        if (direction.sqrMagnitude > 1f)
        {
            direction.Normalize();
        }

        float speed = sprintAction.IsPressed() ? sprintSpeed : walkSpeed;

        if (characterController.isGrounded)
        {
            // 地面に押し付けておかないと、坂で浮いてガタつく
            verticalVelocity = -2f;

            if (jumpAction.WasPressedThisFrame())
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        Vector3 velocity = direction * speed;
        velocity.y = verticalVelocity;

        characterController.Move(velocity * Time.deltaTime);
    }

    /// <summary>Escape でカーソルを出し、画面をクリックすると再び固定する。</summary>
    private void UpdateCursor()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetCursorLocked(false);
        }

        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
        {
            SetCursorLocked(true);
        }
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}

using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// LAN通信でつながったときの、プレイヤー1人分。
///
/// **自分が操作している1人だけが動かし、その位置が他のPCへ送られる。**
/// 他の人のキャラクターは、送られてきた位置を再現するだけで、
/// このPCでは動かさない（`NetworkTransform` が位置を合わせてくれる）。
///
/// つまり、このスクリプトの中で「動かす処理」が走るのは、
/// **4台のうち1台だけ**ということになる。
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayer : NetworkBehaviour
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

    [Header("入力")]
    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("見た目")]
    [Tooltip("色を変える対象。カプセルの Mesh Renderer を入れる")]
    [SerializeField] private Renderer bodyRenderer;

    [Tooltip("参加した順に、この色が割り当てられる")]
    [SerializeField]
    private Color[] playerColors =
    {
        new Color(0.30f, 0.60f, 0.95f), // 青
        new Color(0.95f, 0.45f, 0.35f), // 赤
        new Color(0.45f, 0.85f, 0.45f), // 緑
        new Color(0.95f, 0.80f, 0.30f), // 黄
    };

    [Header("出てくる場所")]
    [Tooltip("中心からどれだけ離れた場所に出てくるか（メートル）")]
    [SerializeField] private float spawnRadius = 3f;

    private CharacterController characterController;
    private InputActionMap playerMap;
    private InputAction moveAction;
    private float verticalVelocity;

    /// <summary>自分が操作しているプレイヤーか。UI表示などに使える。</summary>
    public bool IsLocalPlayer => IsOwner;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    public override void OnNetworkSpawn()
    {
        ApplyColor();

        // どのPCで誰が出てきたかを残す。つながっているかの確認に使う
        Debug.Log($"[LAN] プレイヤー {OwnerClientId} が出てきました（自分が操作する：{IsOwner}）");

        if (!IsOwner)
        {
            // 他の人のキャラクター。ここでは動かさない。
            // CharacterController を切っておかないと、
            // 送られてきた位置に移そうとしても押し戻してしまう
            characterController.enabled = false;
            enabled = false;
            return;
        }

        SetUpInput();
        MoveToSpawnPoint();
        FollowWithCamera();
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            playerMap?.Disable();
        }
    }

    // ------------------------------------------------------------
    // 見た目
    // ------------------------------------------------------------

    /// <summary>
    /// 参加番号から色を決める。
    /// **計算で決めているので、通信で色を送る必要がない。**
    /// どのPCで見ても同じ人が同じ色になる。
    /// </summary>
    private void ApplyColor()
    {
        if (bodyRenderer == null || playerColors == null || playerColors.Length == 0)
        {
            return;
        }

        int index = (int)(OwnerClientId % (ulong)playerColors.Length);
        bodyRenderer.material.color = playerColors[index];
    }

    // ------------------------------------------------------------
    // 入力
    // ------------------------------------------------------------

    private void SetUpInput()
    {
        if (inputActions == null)
        {
            Debug.LogError($"{name}: 入力の設定（InputSystem_Actions）が入っていません。", this);
            return;
        }

        playerMap = inputActions.FindActionMap("Player", true);
        moveAction = playerMap.FindAction("Move", true);
        playerMap.Enable();
    }

    private void Update()
    {
        // OnNetworkSpawn で自分以外は enabled = false にしているので、
        // ここへ来るのは自分が操作している1人だけ
        if (!IsSpawned || !IsOwner || moveAction == null)
        {
            return;
        }

        Vector3 direction = GetMoveDirection();

        if (characterController.isGrounded && verticalVelocity <= 0f)
        {
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

    /// <summary>カメラの向きを基準に、進みたい方向を求める。</summary>
    private Vector3 GetMoveDirection()
    {
        Vector2 input = moveAction.ReadValue<Vector2>();

        Transform view = Camera.main != null ? Camera.main.transform : null;

        Vector3 forward = view != null ? view.forward : Vector3.forward;
        Vector3 right = view != null ? view.right : Vector3.right;

        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        Vector3 direction = forward * input.y + right * input.x;

        return direction.sqrMagnitude > 1f ? direction.normalized : direction;
    }

    // ------------------------------------------------------------
    // 物を押す
    // ------------------------------------------------------------

    [Header("物を押す")]
    [Tooltip("「押している」をホストへ伝える間隔（秒）。短いほど反応がよく、通信量は増える")]
    [Range(0.02f, 0.5f)]
    [SerializeField] private float pushSendInterval = 0.1f;

    private float nextPushSendTime;

    /// <summary>
    /// 歩いていて何かにぶつかったときに、Unityが呼んでくれる。
    /// **押せる箱だったら、ホストに「押した」と伝える。**
    ///
    /// 自分では力を加えない。加えてしまうと、
    /// **自分の画面でだけ箱が動いて、他の人の画面では動かない**という食い違いが起きるため。
    ///
    /// **ぶつかっている間、毎フレーム呼ばれる。**
    /// 毎フレーム送ると通信量が無駄なので、少し間隔をあけて送る。
    ///
    /// ホスト側は、これを**「押している状態」として預かり、その間ずっと力を加える。**
    /// 1回の指示で1発叩く形ではないので、**途中で1回届かなくても体感が変わらない。**
    /// </summary>
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!IsOwner || !IsSpawned || Time.time < nextPushSendTime)
        {
            return;
        }

        PushableBox box = hit.collider.GetComponentInParent<PushableBox>();

        if (box == null)
        {
            return;
        }

        nextPushSendTime = Time.time + pushSendInterval;
        box.PushServerRpc(hit.moveDirection);
    }

    // ------------------------------------------------------------
    // 出てくる場所とカメラ
    // ------------------------------------------------------------

    /// <summary>
    /// 参加番号ごとに、円周上の違う場所へ移す。
    /// 全員が同じ場所に出てきて重なるのを防ぐため。
    /// </summary>
    private void MoveToSpawnPoint()
    {
        float angle = OwnerClientId * 90f;
        Vector3 position = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * spawnRadius);

        characterController.enabled = false;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, angle + 180f, 0f));
        characterController.enabled = true;

        verticalVelocity = 0f;
    }

    private void FollowWithCamera()
    {
        LocalPlayerCamera camera = FindFirstObjectByType<LocalPlayerCamera>();

        if (camera != null)
        {
            camera.SetTarget(transform);
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ロボットのカメラ。マウスで向きを変え、操作している体を追いかける。
///
/// **体の子にはしない。** 体の子にすると、体が動くたびにカメラが引っ張られて
/// 見た目がカクつく。代わりに、毎フレーム体の位置へ寄せていく形にしている。
///
/// **体そのものは回さない。** カメラだけが回り、体は進みたい方向へ自分で向き直る。
/// </summary>
public class RobotCameraLook : MonoBehaviour
{
    [Header("つなぐもの")]
    [Tooltip("上下の首振りをさせる場所。この下にカメラを置く")]
    [SerializeField] private Transform pitchPivot;

    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("見え方")]
    [Tooltip("マウスを動かしたときに回る量")]
    [Range(0.02f, 1f)]
    [SerializeField] private float sensitivity = 0.12f;

    [Tooltip("上を向ける限界の角度")]
    [Range(0f, 89f)]
    [SerializeField] private float maxLookUp = 70f;

    [Tooltip("下を向ける限界の角度")]
    [Range(0f, 89f)]
    [SerializeField] private float maxLookDown = 70f;

    [Header("追いかけ方")]
    [Tooltip("追いかける相手の足元から、どれだけ上を見るか（メートル）")]
    [SerializeField] private float targetHeight = 1.4f;

    [Tooltip("追いつく速さ。大きいほどぴったり付いてくる。0にすると瞬時に付いてくる")]
    [Range(0f, 30f)]
    [SerializeField] private float followSmooth = 14f;

    [Header("マウスカーソル")]
    [Tooltip("再生したらカーソルを消して画面に固定する")]
    [SerializeField] private bool lockCursorOnStart = true;

    private InputActionMap playerMap;
    private InputAction lookAction;
    private float pitch;
    private Transform followTarget;

    /// <summary>
    /// 左右の向き（水平方向）。移動の向きを決めるのに使う。
    /// 上下の傾きを含まないので、そのまま地面に沿った方向になる。
    /// </summary>
    public Vector3 FlatForward
    {
        get
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }
    }

    /// <summary>右方向（水平）。移動の向きを決めるのに使う。</summary>
    public Vector3 FlatRight
    {
        get
        {
            Vector3 right = transform.right;
            right.y = 0f;
            return right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
        }
    }

    private void Awake()
    {
        if (inputActions == null)
        {
            Debug.LogError($"{name}: 入力の設定（InputSystem_Actions）が入っていません。", this);
            enabled = false;
            return;
        }

        playerMap = inputActions.FindActionMap("Player", true);
        lookAction = playerMap.FindAction("Look", true);
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

    /// <summary>追いかける相手を決める。切り替わっても位置は滑らかに移る。</summary>
    public void SetTarget(Transform target)
    {
        followTarget = target;
    }

    /// <summary>
    /// 体の位置へ寄せていく。
    /// 体を動かしたあとに動かしたいので、Update ではなく LateUpdate で行う。
    /// </summary>
    private void LateUpdate()
    {
        if (followTarget == null)
        {
            return;
        }

        Vector3 wanted = followTarget.position + Vector3.up * targetHeight;

        if (followSmooth <= 0f)
        {
            transform.position = wanted;
            return;
        }

        transform.position = Vector3.Lerp(
            transform.position, wanted, 1f - Mathf.Exp(-followSmooth * Time.deltaTime));
    }

    private void Update()
    {
        UpdateCursor();

        Vector2 look = lookAction.ReadValue<Vector2>() * sensitivity;

        // 左右はこのオブジェクトごと回す（体は回さない）
        transform.Rotate(Vector3.up, look.x, Space.World);

        // 上下は首の部分だけ回す
        pitch = Mathf.Clamp(pitch - look.y, -maxLookUp, maxLookDown);
        if (pitchPivot != null)
        {
            pitchPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }

    /// <summary>Escape でカーソルを出し、画面をクリックすると再び固定する。</summary>
    private void UpdateCursor()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetCursorLocked(false);
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
        {
            SetCursorLocked(true);
        }
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}

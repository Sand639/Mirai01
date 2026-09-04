using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ロボットの上半身と下半身を、切り離したり合体させたりする。
///
/// 遊び方：
///   W / A / S / D … 今操作している体を動かす
///   マウス        … カメラを回す
///   Q             … 切り離す
///   Tab           … 操作する方を切り替える（上半身 ⇔ 下半身）
///   E             … 近づいていれば合体する
///   Space         … ジャンプする（合体中と、分離中の下半身だけ）
///   F             … 目の前の物を持つ／離す（合体中と、分離中の上半身だけ）
///   V             … 一人称と三人称を切り替える
///
/// **合体しているときは1体、分けたときは2体**という作りにしている。
/// 2つを物理的に繋いで動かすとガタつくため、繋ぐのをやめて
/// **「合体した姿の体」と「上半身」「下半身」を入れ替える**方式にした。
/// </summary>
public class RobotController : MonoBehaviour
{
    /// <summary>いまの状態。</summary>
    public enum RobotState
    {
        /// <summary>合体中。1体として動く</summary>
        Combined,

        /// <summary>分離中。上半身を操作している</summary>
        SplitUpper,

        /// <summary>分離中。下半身を操作している</summary>
        SplitLower,
    }

    [Header("つなぐもの")]
    [Tooltip("合体した姿の体。合体中だけ出てくる")]
    [SerializeField] private RobotBody combinedBody;

    [Tooltip("上半身。分離中だけ出てくる")]
    [SerializeField] private RobotBody upperBody;

    [Tooltip("下半身。分離中だけ出てくる")]
    [SerializeField] private RobotBody lowerBody;

    [Tooltip("カメラ。操作している体を追いかける")]
    [SerializeField] private RobotCameraLook cameraLook;

    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("切り離したときの置き場所")]
    [Tooltip("合体した体の足元から見た、上半身の出てくる位置")]
    [SerializeField] private Vector3 upperSplitOffset = new Vector3(0f, 1.0f, -0.9f);

    [Tooltip("合体した体の足元から見た、下半身の出てくる位置")]
    [SerializeField] private Vector3 lowerSplitOffset = Vector3.zero;

    [Header("合体")]
    [Tooltip("この距離まで近づくと合体できる（メートル）")]
    [Range(0.5f, 10f)]
    [SerializeField] private float combineDistance = 2.5f;

    [Header("カメラ")]
    [Tooltip("ONにすると、下半身を操作中もカメラは上半身のまま。OFFなら操作している方を追いかける")]
    [SerializeField] private bool cameraAlwaysOnUpper = false;

    [Header("ジャンプ")]
    [Tooltip("ONにすると、分離中の上半身もジャンプできるようになる。OFFなら足（下半身）だけ")]
    [SerializeField] private bool upperCanJump = false;

    [Header("キーの割り当て")]
    [Tooltip("切り離すキー")]
    [SerializeField] private Key splitKey = Key.Q;

    [Tooltip("合体させるキー")]
    [SerializeField] private Key combineKey = Key.E;

    [Tooltip("操作する方を切り替えるキー")]
    [SerializeField] private Key switchKey = Key.Tab;

    // ------------------------------------------------------------
    // 拡張ポイント：エフェクトや音を足したいときはここを購読する
    // ------------------------------------------------------------

    /// <summary>切り離した瞬間に呼ばれる。</summary>
    public event Action Split;

    /// <summary>合体した瞬間に呼ばれる。</summary>
    public event Action Combined;

    /// <summary>操作する方が切り替わったときに呼ばれる。</summary>
    public event Action<RobotState> ControlSwitched;

    /// <summary>いまの状態。UI表示などから見たいときのために公開している。</summary>
    public RobotState State { get; private set; } = RobotState.Combined;

    /// <summary>いま操作している体。</summary>
    public RobotBody ActiveBody { get; private set; }

    /// <summary>いま合体できるか（近づいているか）。UIの表示に使える。</summary>
    public bool CanCombine
    {
        get
        {
            if (State == RobotState.Combined || upperBody == null || lowerBody == null)
            {
                return false;
            }

            // 高さの差は見ない。段差の上下でも近ければ合体できる
            Vector3 gap = upperBody.transform.position - lowerBody.transform.position;
            gap.y = 0f;

            return gap.magnitude <= combineDistance;
        }
    }

    /// <summary>
    /// いまジャンプが許されている状態か。**分離中の上半身は跳べない**。
    /// （地面に付いているかどうかは見ていない。それは体の側で判断する）
    /// </summary>
    public bool CanJump => State != RobotState.SplitUpper || upperCanJump;

    private InputActionMap playerMap;
    private InputAction moveAction;
    private InputAction jumpAction;

    private void Awake()
    {
        if (combinedBody == null || upperBody == null || lowerBody == null)
        {
            Debug.LogError($"{name}: 体（合体・上半身・下半身）が揃っていません。", this);
            enabled = false;
            return;
        }

        if (inputActions == null)
        {
            Debug.LogError($"{name}: 入力の設定（InputSystem_Actions）が入っていません。", this);
            enabled = false;
            return;
        }

        playerMap = inputActions.FindActionMap("Player", true);
        moveAction = playerMap.FindAction("Move", true);
        jumpAction = playerMap.FindAction("Jump", true);

        ApplyState(RobotState.Combined);
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
        ReadStateInput();
        MoveBodies();
        UpdateCameraTarget();
    }

    // ------------------------------------------------------------
    // 入力
    // ------------------------------------------------------------

    private void ReadStateInput()
    {
        if (WasKeyPressed(splitKey) && State == RobotState.Combined)
        {
            DoSplit();
        }

        if (WasKeyPressed(switchKey) && State != RobotState.Combined)
        {
            SwitchControl();
        }

        if (WasKeyPressed(combineKey) && State != RobotState.Combined)
        {
            TryCombine();
        }
    }

    // ------------------------------------------------------------
    // 切り離す
    // ------------------------------------------------------------

    /// <summary>合体した体を、上半身と下半身に分ける。</summary>
    public void DoSplit()
    {
        if (State != RobotState.Combined)
        {
            return;
        }

        Vector3 basePosition = combinedBody.transform.position;
        Quaternion baseRotation = combinedBody.transform.rotation;

        // 合体した体を隠してから、2つを置く
        combinedBody.gameObject.SetActive(false);

        PlaceBody(lowerBody, basePosition + baseRotation * lowerSplitOffset, baseRotation);
        PlaceBody(upperBody, basePosition + baseRotation * upperSplitOffset, baseRotation);

        ApplyState(RobotState.SplitUpper);

        // 拡張ポイント：ここで切り離しのエフェクトや音を鳴らせる
        Split?.Invoke();
        ControlSwitched?.Invoke(State);
    }

    // ------------------------------------------------------------
    // 操作を切り替える
    // ------------------------------------------------------------

    /// <summary>操作する方を入れ替える。</summary>
    public void SwitchControl()
    {
        if (State == RobotState.Combined)
        {
            return;
        }

        ApplyState(State == RobotState.SplitUpper ? RobotState.SplitLower : RobotState.SplitUpper);

        // 拡張ポイント：ここで「いまどちらを操作中か」のUI表示を切り替えられる
        ControlSwitched?.Invoke(State);
    }

    // ------------------------------------------------------------
    // 合体する
    // ------------------------------------------------------------

    /// <summary>近づいていれば合体する。合体できたら true。</summary>
    public bool TryCombine()
    {
        if (!CanCombine)
        {
            return false;
        }

        // 足の位置に合体した体を作る。向きは今操作している方に合わせる
        Vector3 basePosition = lowerBody.transform.position - lowerSplitOffset;
        Quaternion baseRotation = ActiveBody != null
            ? ActiveBody.transform.rotation
            : lowerBody.transform.rotation;

        upperBody.gameObject.SetActive(false);
        lowerBody.gameObject.SetActive(false);

        PlaceBody(combinedBody, basePosition, baseRotation);

        ApplyState(RobotState.Combined);

        // 拡張ポイント：ここで合体のエフェクトや音を鳴らせる
        Combined?.Invoke();
        ControlSwitched?.Invoke(State);

        return true;
    }

    // ------------------------------------------------------------
    // 状態を反映する
    // ------------------------------------------------------------

    /// <summary>状態に合わせて、出す体と操作する体を決める。</summary>
    private void ApplyState(RobotState next)
    {
        State = next;

        bool isCombined = next == RobotState.Combined;

        combinedBody.gameObject.SetActive(isCombined);
        upperBody.gameObject.SetActive(!isCombined);
        lowerBody.gameObject.SetActive(!isCombined);

        ActiveBody = next switch
        {
            RobotState.Combined => combinedBody,
            RobotState.SplitUpper => upperBody,
            _ => lowerBody,
        };
    }

    /// <summary>体を出しつつ、指定した場所へ移す。</summary>
    private static void PlaceBody(RobotBody body, Vector3 position, Quaternion rotation)
    {
        body.gameObject.SetActive(true);
        body.Teleport(position, rotation);
    }

    // ------------------------------------------------------------
    // 動かす
    // ------------------------------------------------------------

    /// <summary>
    /// **体を「見ている方向」に向けるか。**
    ///
    /// ONにすると、**一人称でも三人称でも**カメラの向きに体が揃う（フォートナイト型）。
    /// 向きが固定されるぶん、**横歩き・後ろ歩きが表現できる**ようになる
    /// （速さの違いは <see cref="RobotBody"/> 側で付けている）。
    ///
    /// OFFにすると、進む方向へ向き直る昔の作りに戻る。
    ///
    /// 切り替えるのは <see cref="RobotViewSwitcher"/>。
    /// </summary>
    public bool FaceLookDirection { get; set; }

    private void MoveBodies()
    {
        Vector3 direction = GetMoveDirection();

        // ジャンプできるのは「合体中」と「分離中の下半身」だけ
        bool jump = jumpAction.WasPressedThisFrame() && CanJump;

        // 見ている方向を向かせる（一人称・三人称とも）
        Vector3? facing = FaceLookDirection && cameraLook != null
            ? cameraLook.FlatForward
            : (Vector3?)null;

        if (State == RobotState.Combined)
        {
            combinedBody.Tick(direction, jump, facing);
            return;
        }

        // 操作していない方も、重力だけは効かせる（勝手には動かない）
        if (State == RobotState.SplitUpper)
        {
            upperBody.Tick(direction, jump, facing);
            lowerBody.Tick(Vector3.zero);
        }
        else
        {
            lowerBody.Tick(direction, jump, facing);
            upperBody.Tick(Vector3.zero);
        }
    }

    /// <summary>カメラの向きを基準に、進みたい方向を求める。</summary>
    private Vector3 GetMoveDirection()
    {
        if (cameraLook == null)
        {
            return Vector3.zero;
        }

        Vector2 input = moveAction.ReadValue<Vector2>();
        Vector3 direction = cameraLook.FlatForward * input.y + cameraLook.FlatRight * input.x;

        return direction.sqrMagnitude > 1f ? direction.normalized : direction;
    }

    // ------------------------------------------------------------
    // カメラ
    // ------------------------------------------------------------

    private void UpdateCameraTarget()
    {
        if (cameraLook == null)
        {
            return;
        }

        // 設定によって、常に上半身を見るか、操作している方を見るかが変わる
        RobotBody target = cameraAlwaysOnUpper && State != RobotState.Combined
            ? upperBody
            : ActiveBody;

        cameraLook.SetTarget(target != null ? target.transform : null);
    }

    private static bool WasKeyPressed(Key key)
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[key].wasPressedThisFrame;
    }
}

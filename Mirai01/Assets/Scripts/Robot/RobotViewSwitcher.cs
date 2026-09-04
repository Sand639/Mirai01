using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ロボットの視点を、**三人称（TPS）と一人称（FPS）で切り替える。**
///
/// V キーで切り替わる。
///
/// - **三人称**… 体の後ろからカメラが見る。全体が見えて、動かしやすい
/// - **一人称**… 体の目の位置からカメラが見る。**狙いやすく、隙間を通るときに分かりやすい**
///
/// ## 作り
///
/// カメラは体の子になっていない（体の子にするとカクつくため）。
/// そのため、切り替えでは次の2つを動かしている。
///
/// 1. **カメラの位置**（後ろに引くか、目の高さに置くか）
/// 2. **カメラが追いかける高さ**（体ごとに目の高さが違うため）
///
/// あわせて、一人称のときは**自分の体の見た目を隠す**。
/// 隠さないと、目の位置から自分の体の内側が見えてしまう。
/// </summary>
[RequireComponent(typeof(RobotController))]
public class RobotViewSwitcher : MonoBehaviour
{
    [Header("つなぐもの")]
    [Tooltip("カメラの向きを扱う部品。CameraRig に付いているもの")]
    [SerializeField] private RobotCameraLook cameraLook;

    [Tooltip("動かすカメラ。CameraPivot の下の RobotCamera を入れる")]
    [SerializeField] private Transform cameraTransform;

    [Header("三人称のとき")]
    [Tooltip("体からどれだけ後ろ・上に引くか")]
    [SerializeField] private Vector3 thirdPersonOffset = new Vector3(0f, 0.8f, -5.5f);

    [Tooltip("追いかける高さ（足元から）")]
    [SerializeField] private float thirdPersonHeight = 1.4f;

    [Header("一人称のとき")]
    [Tooltip("目の位置から、どれだけ前に出すか。0だと体の中から見ることになる")]
    [Range(0f, 1f)]
    [SerializeField] private float firstPersonForward = 0.25f;

    [Header("切り替え")]
    [Tooltip("視点を切り替えるキー")]
    [SerializeField] private Key switchKey = Key.V;

    [Tooltip("切り替わるときの滑らかさ。大きいほど速く移る。0にすると一瞬で切り替わる")]
    [Range(0f, 30f)]
    [SerializeField] private float switchSmooth = 12f;

    [Tooltip("開始時から一人称にする")]
    [SerializeField] private bool startInFirstPerson = false;

    [Tooltip("ONにすると、**一人称でも三人称でも**体が見ている方向を向く（フォートナイト型）。" +
             "OFFにすると、進む方向へ向き直る昔の作りに戻る")]
    [SerializeField] private bool faceLookDirection = true;

    private RobotController controller;
    private RobotBody shownBody;

    /// <summary>いま一人称か。</summary>
    public bool IsFirstPerson { get; private set; }

    private void Awake()
    {
        controller = GetComponent<RobotController>();
        IsFirstPerson = startInFirstPerson;
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

    private void Start()
    {
        ApplyBodyVisibility(true);
        ApplyFacingMode();
    }

    /// <summary>
    /// 体の向き方を決める。
    ///
    /// **一人称でも三人称でも、体は「見ている方向」を向く**（フォートナイト型）。
    ///
    /// 進む方向へ向き直る作りだと、歩きながら視点を動かしたときに
    /// 体だけ先に曲がって分かりにくい。
    /// 向きを固定すると、そのぶん**横歩きや後ろ歩きが自然に表現できる**
    /// （速さの違いは <see cref="RobotBody"/> 側で付けている）。
    /// </summary>
    private void ApplyFacingMode()
    {
        if (controller != null)
        {
            controller.FaceLookDirection = faceLookDirection;
        }
    }

    private void Update()
    {
        if (WasSwitchKeyPressed())
        {
            SetFirstPerson(!IsFirstPerson);
        }

        UpdateCameraPosition();
    }

    /// <summary>視点を切り替える。</summary>
    public void SetFirstPerson(bool firstPerson)
    {
        if (IsFirstPerson == firstPerson)
        {
            return;
        }

        IsFirstPerson = firstPerson;
        ApplyBodyVisibility(true);
        ApplyFacingMode();

        Debug.Log(IsFirstPerson ? "[ROBOT] 一人称にしました" : "[ROBOT] 三人称にしました");
    }

    /// <summary>カメラの位置と、追いかける高さを、いまの視点に合わせる。</summary>
    private void UpdateCameraPosition()
    {
        if (cameraTransform == null)
        {
            return;
        }

        RobotBody body = controller.ActiveBody;

        // 目の高さは体ごとに違う。上半身だけのときは低くなる
        float eyeHeight = body != null ? body.EyeHeight : thirdPersonHeight;

        Vector3 wantedLocal = IsFirstPerson
            ? new Vector3(0f, 0f, firstPersonForward)
            : thirdPersonOffset;

        float wantedHeight = IsFirstPerson ? eyeHeight : thirdPersonHeight;

        if (switchSmooth <= 0f)
        {
            cameraTransform.localPosition = wantedLocal;
        }
        else
        {
            cameraTransform.localPosition = Vector3.Lerp(
                cameraTransform.localPosition,
                wantedLocal,
                1f - Mathf.Exp(-switchSmooth * Time.deltaTime));
        }

        if (cameraLook != null)
        {
            cameraLook.TargetHeight = wantedHeight;
        }
    }

    /// <summary>
    /// 一人称のときだけ、いま操作している体の見た目を隠す。
    ///
    /// **操作していない体は隠さない。** 分離中に相手の体が見えないと、
    /// どこにいるか分からなくなるため。
    /// </summary>
    private void ApplyBodyVisibility(bool showPrevious)
    {
        RobotBody body = controller != null ? controller.ActiveBody : null;

        // 前に隠していた体があれば、先に戻す
        if (showPrevious && shownBody != null && shownBody != body)
        {
            shownBody.SetVisualVisible(true);
        }

        if (body == null)
        {
            shownBody = null;
            return;
        }

        body.SetVisualVisible(!IsFirstPerson);
        shownBody = body;
    }

    /// <summary>操作する体が変わったら、隠す相手も変える。</summary>
    private void HandleControlSwitched(RobotController.RobotState state)
    {
        ApplyBodyVisibility(true);
    }

    private bool WasSwitchKeyPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[switchKey].wasPressedThisFrame;
    }
}

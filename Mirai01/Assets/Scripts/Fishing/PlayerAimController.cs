using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **マウスカーソルが指している地面の一点**を計算して、みんなに配る係。
///
/// 見下ろし視点なので「画面のマウス位置」だけでは方向が決まらない。
/// カメラからマウスへ向かう線を伸ばし、地面にぶつかった場所を「狙点」とする。
///
/// この狙点から見たプレイヤーの向きが、このゲームの「前方向」になる（仕様6）。
/// プレイヤーの向き・フックの発射方向・投げる方向は、すべてここの値を読む。
///
/// 使い方：プレイヤーに付けて、見下ろしカメラを Aim Camera に入れるだけ。
/// </summary>
public class PlayerAimController : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("見下ろしカメラ。空なら Camera.main を自動で使う")]
    [SerializeField] private Camera aimCamera;

    [Header("地面の判定")]
    [Tooltip("この高さの水平な床があるものとして狙点を出す（下のレイヤー判定に当たらなかったときの保険）")]
    [SerializeField] private float groundHeight = 0f;

    [Tooltip("ここに入れたレイヤーに線が当たれば、その場所を優先して狙点にする")]
    [SerializeField] private LayerMask groundMask = ~0;

    [Tooltip("カメラから伸ばす線の長さ")]
    [SerializeField] private float rayDistance = 300f;

    /// <summary>マウスが指している地面のワールド座標。</summary>
    public Vector3 AimPoint { get; private set; }

    /// <summary>プレイヤーから狙点へ向かう、水平に倒した向き（長さ1）。これが「前方向」。</summary>
    public Vector3 AimDirection { get; private set; } = Vector3.forward;

    /// <summary>今フレーム、狙点をちゃんと計算できたか。</summary>
    public bool HasAim { get; private set; }

    private void Awake()
    {
        if (aimCamera == null)
        {
            aimCamera = Camera.main;
        }
    }

    /// <summary>
    /// 使うカメラを後から決める。
    /// **オンラインではプレハブから生まれるため、シーンのカメラを入れておけない。**
    /// 生まれた時点で <see cref="FishingNetPlayer"/> から渡される。
    /// </summary>
    public void SetCamera(Camera camera)
    {
        aimCamera = camera;
    }

    private void Update()
    {
        UpdateAim();
    }

    private void UpdateAim()
    {
        HasAim = false;

        if (aimCamera == null)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        Ray ray = aimCamera.ScreenPointToRay(mouse.position.ReadValue());

        // まず、指定レイヤーの実際の床に当たるか試す
        if (Physics.Raycast(ray, out RaycastHit hit, rayDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            AimPoint = hit.point;
            HasAim = true;
        }
        else
        {
            // 当たらなければ、高さ groundHeight の水平な面との交点を使う
            Plane plane = new Plane(Vector3.up, new Vector3(0f, groundHeight, 0f));
            if (plane.Raycast(ray, out float enter))
            {
                AimPoint = ray.GetPoint(enter);
                HasAim = true;
            }
        }

        if (HasAim)
        {
            Vector3 flat = AimPoint - transform.position;
            flat.y = 0f;

            // マウスがプレイヤーの真上あたりに来て向きが定まらないときは、前の向きを保つ
            if (flat.sqrMagnitude > 0.0001f)
            {
                AimDirection = flat.normalized;
            }
        }
    }
}

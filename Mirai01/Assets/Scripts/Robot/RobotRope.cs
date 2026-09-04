using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **つかまって上り下りできるロープ。**
/// （VALORANT のマップにある、上下に移動できる紐と同じ役割）
///
/// この部品を付けたゲームオブジェクトの位置が**ロープの下端**になる。
/// そこから `height` の分だけ**真上（この物の上向き）**へ伸びているものとして扱う。
///
/// つまり、
///
/// - 床に置けば、そのまま上へ伸びる
/// - 傾けて置けば、斜めのロープになる
///
/// ## 当たり判定を持たない
///
/// ロープは**当たり判定（Collider）を使っていない。**
/// 見つけ方は「近くにある物を物理で探す」のではなく、
/// **自分自身を <see cref="All"/> に登録しておいて、そこから探す**方式にしてある。
///
/// 理由は2つ。
///
/// - ロープに体がぶつかって、つかまる前に押し返されるのを防ぐため
/// - ステージに置くロープはせいぜい数本なので、探すのが軽いため
///
/// 上り下りする側の処理は <see cref="RobotRopeClimber"/> にある。
/// </summary>
public class RobotRope : MonoBehaviour
{
    [Header("長さ")]
    [Tooltip("下端から上端までの長さ（メートル）。この物の位置が下端になる")]
    [Range(1f, 50f)]
    [SerializeField] private float height = 6f;

    [Header("つかまり方")]
    [Tooltip("ロープからどれだけ離れてぶら下がるか（メートル）。体がロープにめり込んで見えないようにする")]
    [Range(0f, 2f)]
    [SerializeField] private float hangDistance = 0.55f;

    [Tooltip("下端から、ここまでは上れない余白（メートル）。0なら下端ぴったりまで下りられる")]
    [Range(0f, 5f)]
    [SerializeField] private float bottomMargin = 0f;

    [Tooltip("上端から、ここまでは上れない余白（メートル）。頭が天井に刺さるのを防ぐ")]
    [Range(0f, 5f)]
    [SerializeField] private float topMargin = 0.2f;

    [Header("見た目")]
    [Tooltip("長さに合わせて自動で伸び縮みさせる筒。Unityの「Cylinder」を入れる。空でもよい")]
    [SerializeField] private Transform visual;

    /// <summary>
    /// いまシーンに出ているロープの一覧。
    /// **探すときはここを見る。**（当たり判定を使わないため）
    /// </summary>
    public static readonly List<RobotRope> All = new List<RobotRope>();

    /// <summary>ロープが伸びていく向き。この物の上向き。</summary>
    public Vector3 Direction => transform.up;

    /// <summary>ロープの下端。この物の位置そのもの。</summary>
    public Vector3 Bottom => transform.position;

    /// <summary>ロープの上端。</summary>
    public Vector3 Top => Bottom + Direction * height;

    /// <summary>ロープからどれだけ離れてぶら下がるか。</summary>
    public float HangDistance => hangDistance;

    /// <summary>つかまれる一番下の高さ（下端からの距離）。</summary>
    public float MinHold => Mathf.Min(bottomMargin, height);

    /// <summary>つかまれる一番上の高さ（下端からの距離）。</summary>
    public float MaxHold => Mathf.Max(MinHold, height - topMargin);

    private void OnEnable()
    {
        All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    /// <summary>
    /// 長さを変えたときに、見た目も合わせて伸ばす。
    ///
    /// Unityの筒（Cylinder）は**高さ2・中心が原点**なので、
    /// 大きさは長さの半分にし、位置も半分だけ持ち上げている。
    /// </summary>
    private void OnValidate()
    {
        if (visual == null)
        {
            return;
        }

        Vector3 scale = visual.localScale;

        visual.localPosition = new Vector3(0f, height * 0.5f, 0f);
        visual.localScale = new Vector3(scale.x, height * 0.5f, scale.z);
    }

    /// <summary>下端から `distance` メートル上がった所の座標を返す。</summary>
    public Vector3 PointAt(float distance)
    {
        return Bottom + Direction * distance;
    }

    /// <summary>
    /// ある座標が、ロープの**どのくらいの高さ**にあたるかを返す（下端からの距離）。
    /// ロープの外側にある場合は、はみ出した値がそのまま返る。
    /// </summary>
    public float HeightOf(Vector3 worldPosition)
    {
        return Vector3.Dot(worldPosition - Bottom, Direction);
    }

    /// <summary>ある座標から一番近い、ロープ上の点を返す。</summary>
    public Vector3 ClosestPoint(Vector3 worldPosition)
    {
        return PointAt(Mathf.Clamp(HeightOf(worldPosition), 0f, height));
    }

    /// <summary>シーンビューにロープの線を描く。置いたときに長さが分かるように。</summary>
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.35f);
        Gizmos.DrawLine(Bottom, Top);

        // つかまれる範囲を、少しだけ太い線で重ねて描く
        Gizmos.color = new Color(1f, 0.85f, 0.35f, 0.5f);
        Gizmos.DrawSphere(PointAt(MinHold), 0.08f);
        Gizmos.DrawSphere(PointAt(MaxHold), 0.08f);
    }
}

using UnityEngine;

/// <summary>
/// 釣りフックを引っ掛けると、物ではなくプレイヤーを引き寄せる固定アンカー。
/// 見た目はこの部品を付けたゲームオブジェクトから独立しているため、モデルに差し替えても使える。
/// </summary>
[RequireComponent(typeof(HookableObject))]
[RequireComponent(typeof(Rigidbody))]
public class AnchorGimmick : MonoBehaviour
{
    [Header("引っ張る先")]
    [Tooltip("プレイヤーがここまで近づいたら引っ張りを終える。空ならオブジェクトの中心を使う")]
    [SerializeField] private Transform pullPoint;

    [Tooltip("1秒あたりにプレイヤーを引っ張る距離")]
    [SerializeField] private float pullSpeed = 8f;

    [Tooltip("この距離まで近づいたら引っ張りを終える")]
    [SerializeField] private float stopDistance = 1.2f;

    /// <summary>プレイヤーを引っ張る先。見た目を変えても空オブジェクトを指定すれば同じ位置を保てる。</summary>
    public Vector3 PullPosition => pullPoint != null ? pullPoint.position : transform.position;

    private void Reset()
    {
        MakeBodyFixed();
    }

    private void Awake()
    {
        MakeBodyFixed();
    }

    /// <summary>
    /// プレイヤーをアンカーの方向へ1フレームぶん動かす。
    /// CharacterController を使うため、壁にぶつかったときも通常のプレイヤー移動と同じ扱いになる。
    /// </summary>
    /// <returns>十分近づいて引っ張りが終わったとき true。</returns>
    public bool PullPlayer(CharacterController player)
    {
        if (player == null)
        {
            return true;
        }

        Vector3 direction = PullPosition - player.transform.position;
        direction.y = 0f;
        float distance = direction.magnitude;
        float endDistance = Mathf.Max(0f, stopDistance);

        if (distance <= endDistance)
        {
            return true;
        }

        float moveDistance = Mathf.Min(pullSpeed * Time.deltaTime, distance - endDistance);
        player.Move(direction / distance * moveDistance);
        return false;
    }

    private void MakeBodyFixed()
    {
        Rigidbody body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeAll;
    }
}

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

    [Tooltip("チャージ0のときの、1秒あたりにプレイヤーを引っ張る距離")]
    [SerializeField] private float minPullSpeed = 4f;

    [Tooltip("チャージ最大のときの、1秒あたりにプレイヤーを引っ張る距離")]
    [SerializeField] private float maxPullSpeed = 16f;

    [Tooltip("チャージ0のときに、1回だけ引っ張る秒数")]
    [SerializeField] private float minPullSeconds = 0.1f;

    [Tooltip("チャージ最大のときに、1回だけ引っ張る秒数。終わると釣り竿は手元へ戻る")]
    [SerializeField] private float maxPullSeconds = 0.7f;

    private float activePullSpeed;
    private float activePullSeconds;

    [Tooltip("この距離まで近づいたら引っ張りを終える")]
    [SerializeField] private float stopDistance = 1.2f;

    /// <summary>プレイヤーを引っ張る先。見た目を変えても空オブジェクトを指定すれば同じ位置を保てる。</summary>
    public Vector3 PullPosition => pullPoint != null ? pullPoint.position : transform.position;

    /// <summary>今回の1回ぶんの引っ張りを続ける秒数。</summary>
    public float PullSeconds => activePullSeconds;

    private void Reset()
    {
        MakeBodyFixed();
    }

    private void Awake()
    {
        MakeBodyFixed();
        BeginPull(0f);
    }

    /// <summary>フックのチャージ量に応じて、今回だけ使う引っ張る強さと時間を決める。</summary>
    public void BeginPull(float charge)
    {
        float strength = Mathf.Clamp01(charge);
        activePullSpeed = Mathf.Lerp(minPullSpeed, maxPullSpeed, strength);
        activePullSeconds = Mathf.Lerp(minPullSeconds, maxPullSeconds, strength);
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

        float moveDistance = Mathf.Min(activePullSpeed * Time.deltaTime, distance - endDistance);
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

using UnityEngine;

/// <summary>
/// **フックの先端。** 飛んでいる「かぎ針」そのもの。
///
/// このスクリプトは考えない。動かすのは <see cref="HookController"/> の役目で、
/// ここは「物資に当たったよ」と報告する当たり判定だけを持つ。
/// 分けておくと、フックの見た目や当たり方だけを後で差し替えやすい。
///
/// 必要な部品：トリガーにした Collider と、キネマティックな Rigidbody。
/// （Rigidbody が無いと、動かしたときにトリガーの通知が出ないことがある）
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class HookProjectile : MonoBehaviour
{
    /// <summary>物資に触れた瞬間に呼ばれる。相手の <see cref="HookableObject"/> を渡す。</summary>
    public System.Action<HookableObject> HookableTouched;

    private Rigidbody body;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        // 当たり判定はすり抜け（トリガー）にしておく。押し返さず、触れたことだけ拾う
        foreach (Collider col in GetComponents<Collider>())
        {
            col.isTrigger = true;
        }
    }

    /// <summary>フックの位置を動かす。HookController から毎フレーム呼ばれる。</summary>
    public void MoveTo(Vector3 worldPosition)
    {
        body.position = worldPosition;
        transform.position = worldPosition;
    }

    private void OnTriggerEnter(Collider other)
    {
        HookableObject hookable = other.GetComponentInParent<HookableObject>();

        if (hookable != null && !hookable.IsHooked)
        {
            HookableTouched?.Invoke(hookable);
        }
    }
}

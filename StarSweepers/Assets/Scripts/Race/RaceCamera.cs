using UnityEngine;

/// <summary>
/// **カートを後ろから追いかけるカメラ。**
///
/// マウスでは動かさない。**進む向きの後ろに回り込む**だけの、レースにありがちな見せ方。
/// 遅れて付いてくるので、曲がるときに車体が見える。
///
/// 追う相手が入っていなければ、**自分が操作しているカートを自分で探す**
/// （<see cref="RaceRacer.All"/> の中から）。
/// </summary>
public class RaceCamera : MonoBehaviour
{
    [Tooltip("追いかける相手。空なら、自分が操作しているカートを探す")]
    [SerializeField] private Transform target;

    [Tooltip("後ろに下がる距離（メートル）")]
    [SerializeField] private float distance = 7.5f;

    [Tooltip("上に上がる高さ（メートル）")]
    [SerializeField] private float height = 3.4f;

    [Tooltip("見る位置を、相手のどれだけ上にするか")]
    [SerializeField] private float lookHeight = 1.2f;

    [Tooltip("付いていく滑らかさ。大きいほどぴったり付く")]
    [SerializeField] private float positionSmooth = 7f;

    [Tooltip("向きを合わせる滑らかさ。大きいほど早く向く")]
    [SerializeField] private float rotationSmooth = 10f;

    private void LateUpdate()
    {
        if (GamePause.IsPaused)
        {
            return;
        }

        if (target == null)
        {
            target = FindLocalKart();

            if (target == null)
            {
                return;
            }

            // 見つけた最初のフレームは、いきなり後ろへ回り込ませる
            SnapBehind();
            return;
        }

        Vector3 wanted = target.position - target.forward * distance + Vector3.up * height;

        transform.position = Vector3.Lerp(
            transform.position, wanted, 1f - Mathf.Exp(-positionSmooth * Time.deltaTime));

        Vector3 lookAt = target.position + Vector3.up * lookHeight;
        Vector3 direction = lookAt - transform.position;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        transform.rotation = Quaternion.Slerp(
            transform.rotation, Quaternion.LookRotation(direction),
            1f - Mathf.Exp(-rotationSmooth * Time.deltaTime));
    }

    /// <summary>追う相手を入れ替える。通信で自分のカートが出てきたときに使う。</summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        SnapBehind();
    }

    /// <summary>間を置かずに、相手の後ろへ移動する。</summary>
    public void SnapBehind()
    {
        if (target == null)
        {
            return;
        }

        transform.position = target.position - target.forward * distance + Vector3.up * height;
        transform.rotation = Quaternion.LookRotation(
            target.position + Vector3.up * lookHeight - transform.position);
    }

    private static Transform FindLocalKart()
    {
        for (int i = 0; i < RaceRacer.All.Count; i++)
        {
            if (RaceRacer.All[i].IsLocalPlayer)
            {
                return RaceRacer.All[i].transform;
            }
        }

        return RaceRacer.All.Count > 0 ? RaceRacer.All[0].transform : null;
    }
}

using UnityEngine;

/// <summary>
/// **チャージ量と、投げのタイミングを見せる簡単な表示。**
///
/// プロトタイプ用なので、画像は使わず四角を伸び縮みさせるだけ（仕様2・5の「簡単なUI/Visual」）。
///   ・チャージ … 押している間、下のバーが左から伸びる
///   ・タイミング … 引き寄せ中、マーカーが左右に動く。色の付いた枠が“当たり”
///
/// 値の受け渡し：チャージは HookController、タイミングは ThrowController から呼ばれる。
/// </summary>
public class HookChargeUI : MonoBehaviour
{
    [Header("チャージ表示")]
    [Tooltip("チャージ表示のまとまり（出す/隠すの対象）")]
    [SerializeField] private GameObject chargeRoot;

    [Tooltip("伸びる部分。左端を軸にして横方向の拡大率を変える")]
    [SerializeField] private RectTransform chargeFill;

    [Header("タイミング表示")]
    [Tooltip("タイミング表示のまとまり（出す/隠すの対象）")]
    [SerializeField] private GameObject timingRoot;

    [Tooltip("左右に動くマーカー")]
    [SerializeField] private RectTransform timingMarker;

    [Tooltip("『強い力』の枠")]
    [SerializeField] private RectTransform goodZone;

    [Tooltip("『非常に強い力』の枠")]
    [SerializeField] private RectTransform perfectZone;

    [Tooltip("タイミングバーの幅（ピクセル）。マーカーと枠の位置計算に使う")]
    [SerializeField] private float barWidth = 420f;

    /// <summary>チャージ表示を出す／隠す。</summary>
    public void ShowCharge(bool visible)
    {
        if (chargeRoot != null)
        {
            chargeRoot.SetActive(visible);
        }
    }

    /// <summary>チャージ量（0〜1）を反映する。</summary>
    public void SetCharge(float amount)
    {
        if (chargeFill != null)
        {
            chargeFill.localScale = new Vector3(Mathf.Clamp01(amount), 1f, 1f);
        }
    }

    /// <summary>タイミング表示を出す／隠す。</summary>
    public void ShowTiming(bool visible)
    {
        if (timingRoot != null)
        {
            timingRoot.SetActive(visible);
        }
    }

    /// <summary>
    /// マーカーと枠の位置を更新する。
    /// <paramref name="pos"/>・<paramref name="center"/> は 0〜1、
    /// <paramref name="good"/>・<paramref name="perfect"/> は中心からの片側の広さ（0〜1）。
    /// </summary>
    public void SetTiming(float pos, float center, float good, float perfect)
    {
        if (timingMarker != null)
        {
            timingMarker.anchoredPosition = new Vector2((pos - 0.5f) * barWidth, 0f);
        }

        if (goodZone != null)
        {
            goodZone.anchoredPosition = new Vector2((center - 0.5f) * barWidth, 0f);
            goodZone.sizeDelta = new Vector2(good * 2f * barWidth, goodZone.sizeDelta.y);
        }

        if (perfectZone != null)
        {
            perfectZone.anchoredPosition = new Vector2((center - 0.5f) * barWidth, 0f);
            perfectZone.sizeDelta = new Vector2(perfect * 2f * barWidth, perfectZone.sizeDelta.y);
        }
    }
}

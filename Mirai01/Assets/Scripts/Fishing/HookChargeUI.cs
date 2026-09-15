using UnityEngine;

/// <summary>
/// **チャージ量と、スキルチェックのゲージを見せる簡単な表示。**
///
/// プロトタイプ用なので、画像は使わず四角を伸び縮みさせるだけ（仕様2・5の「簡単なUI/Visual」）。
///   ・チャージ … 押している間、下のバーが左から伸びる
///   ・スキルチェック … 引き寄せ中、マーカーが左から右へ**1回だけ**動く。
///     色の付いた枠が“当たり”で、**枠ごとに投げる方向が違う**
///
/// 値の受け渡し：チャージは <see cref="HookController"/>、
/// ゲージは <see cref="ThrowController"/> から呼ばれる。
/// </summary>
public class HookChargeUI : MonoBehaviour
{
    /// <summary>スキルチェックの枠1つぶんの見た目（外枠・良い・最適の3つ）。</summary>
    [System.Serializable]
    public class ZoneVisual
    {
        [Tooltip("枠のまとまり（出す/隠すの対象）")]
        public GameObject root;

        [Tooltip("『通常成功』の範囲を表す四角")]
        public RectTransform hitRect;

        [Tooltip("『強い力』の範囲を表す四角")]
        public RectTransform goodRect;

        [Tooltip("『非常に強い力』の範囲を表す四角")]
        public RectTransform perfectRect;
    }

    [Header("チャージ表示")]
    [Tooltip("チャージ表示のまとまり（出す/隠すの対象）")]
    [SerializeField] private GameObject chargeRoot;

    [Tooltip("伸びる部分。左端を軸にして横方向の拡大率を変える")]
    [SerializeField] private RectTransform chargeFill;

    [Header("スキルチェック表示")]
    [Tooltip("スキルチェック表示のまとまり（出す/隠すの対象）")]
    [SerializeField] private GameObject timingRoot;

    [Tooltip("左から右へ動くマーカー")]
    [SerializeField] private RectTransform timingMarker;

    [Tooltip("枠の見た目。ThrowController の枠の数だけ用意しておく")]
    [SerializeField] private ZoneVisual[] zoneVisuals;

    [Tooltip("ゲージの幅（ピクセル）。マーカーと枠の位置計算に使う")]
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

    /// <summary>スキルチェック表示を出す／隠す。</summary>
    public void ShowTiming(bool visible)
    {
        if (timingRoot != null)
        {
            timingRoot.SetActive(visible);
        }
    }

    /// <summary>使う枠の数を伝える。余った見た目は隠す。</summary>
    public void SetZoneCount(int count)
    {
        if (zoneVisuals == null)
        {
            return;
        }

        for (int i = 0; i < zoneVisuals.Length; i++)
        {
            if (zoneVisuals[i] != null && zoneVisuals[i].root != null)
            {
                zoneVisuals[i].root.SetActive(i < count);
            }
        }

        if (count > zoneVisuals.Length)
        {
            Debug.LogWarning(
                $"{name}: 枠が {count} 個ありますが、表示は {zoneVisuals.Length} 個分しかありません。" +
                "検証シーンを作り直すか、Zone Visuals を足してください。", this);
        }
    }

    /// <summary>
    /// 枠1つの位置と広さを決める。
    /// <paramref name="center"/> は 0〜1、広さは中心からの片側の広さ（0〜1）。
    /// </summary>
    public void SetZone(int index, float center, float hit, float good, float perfect)
    {
        if (zoneVisuals == null || index < 0 || index >= zoneVisuals.Length)
        {
            return;
        }

        ZoneVisual visual = zoneVisuals[index];
        if (visual == null)
        {
            return;
        }

        float x = (center - 0.5f) * barWidth;

        PlaceRect(visual.hitRect, x, hit);
        PlaceRect(visual.goodRect, x, good);
        PlaceRect(visual.perfectRect, x, perfect);
    }

    private void PlaceRect(RectTransform rect, float x, float halfWidth01)
    {
        if (rect == null)
        {
            return;
        }

        rect.anchoredPosition = new Vector2(x, 0f);
        rect.sizeDelta = new Vector2(halfWidth01 * 2f * barWidth, rect.sizeDelta.y);
    }

    /// <summary>マーカーの位置（0〜1）を反映する。</summary>
    public void SetMarker(float position)
    {
        if (timingMarker != null)
        {
            timingMarker.anchoredPosition =
                new Vector2((Mathf.Clamp01(position) - 0.5f) * barWidth, 0f);
        }
    }
}

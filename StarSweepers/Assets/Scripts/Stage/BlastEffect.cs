using UnityEngine;

/// <summary>
/// **爆発の見た目。** 一瞬で大きく広がって、縮みながら消える球。
///
/// 爆弾（<see cref="Bomb"/>）が爆発したときに作られ、終わったら自分で消える。
/// 当たり判定は無い（見た目だけ）。
/// </summary>
public class BlastEffect : MonoBehaviour
{
    private float size;
    private float duration;
    private float elapsed;

    /// <summary>広がり始める。<paramref name="diameter"/> は一番大きくなったときの直径。</summary>
    public void Play(float diameter, float seconds)
    {
        size = diameter;
        duration = Mathf.Max(0.05f, seconds);
        elapsed = 0f;

        transform.localScale = Vector3.zero;
    }

    private void Update()
    {
        // ポーズ中でも止めない（止めると画面に火の玉が残り続ける）
        elapsed += Time.unscaledDeltaTime;

        float t = Mathf.Clamp01(elapsed / duration);

        // 前半で一気に広がり、後半で縮んで消える
        float scale = t < 0.35f
            ? Mathf.SmoothStep(0f, 1f, t / 0.35f)
            : Mathf.Lerp(1f, 0f, (t - 0.35f) / 0.65f);

        transform.localScale = Vector3.one * (size * scale);

        if (t >= 1f)
        {
            Destroy(gameObject);
        }
    }
}

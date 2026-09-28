using UnityEngine;

/// <summary>
/// **爆発の見た目。** ふくらんで縮む球と、一瞬の閃光で「爆発した」と分かるようにする。
///
/// 画像や粒子（パーティクル）の素材を用意しなくても動くように、
/// 球の大きさとライトの明るさを動かすだけで作ってある。
///
/// 使い方：<see cref="ExplosiveObject"/> の Explosion Effect Prefab に入れる。
/// 置かれた瞬間から再生が始まり、終わると自分で消える。
/// </summary>
public class ExplosionEffect : MonoBehaviour
{
    [Header("部品")]
    [Tooltip("ふくらむ球（当たり判定は付けない）")]
    [SerializeField] private Transform ball;

    [Tooltip("一瞬光らせるライト")]
    [SerializeField] private Light flash;

    [Header("動き")]
    [Tooltip("爆発の見た目が終わるまでの秒数")]
    [SerializeField] private float duration = 0.5f;

    [Tooltip("球が一番大きくなるまでの割合（0〜1）。小さいほど「バン」と一気にふくらむ")]
    [Range(0.05f, 0.9f)]
    [SerializeField] private float peakAt = 0.3f;

    [Tooltip("球の直径が、爆発の範囲の何倍まで大きくなるか")]
    [SerializeField] private float ballScale = 1.8f;

    [Tooltip("閃光の明るさ")]
    [SerializeField] private float flashIntensity = 700f;

    private float radius = 4f;
    private float timer;

    /// <summary>爆発の範囲を伝えて再生を始める。<see cref="ExplosiveObject"/> から呼ばれる。</summary>
    public void Play(float explosionRadius)
    {
        radius = Mathf.Max(0.1f, explosionRadius);
        timer = 0f;

        if (flash != null)
        {
            flash.range = radius * 2.5f;
        }
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / Mathf.Max(0.05f, duration));

        if (ball != null)
        {
            // 一気にふくらんで、そのあと縮んで消える
            float size = t < peakAt
                ? Mathf.Lerp(0f, radius * ballScale, t / peakAt)
                : Mathf.Lerp(radius * ballScale, 0f, (t - peakAt) / (1f - peakAt));

            ball.localScale = new Vector3(size, size, size);
        }

        if (flash != null)
        {
            flash.intensity = Mathf.Lerp(flashIntensity, 0f, t);
        }

        if (t >= 1f)
        {
            Destroy(gameObject);
        }
    }
}

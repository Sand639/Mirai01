using UnityEngine;

/// <summary>
/// **プレイヤーのスタン（動けない状態）を管理する。**
///
/// 爆発に巻き込まれると、ここに <see cref="Stun"/> が呼ばれて一定時間動けなくなる。
/// スタン中だと分かるように、体を赤く点滅させ、頭の上に印を出す。
///
/// 「動けない」の判定を持つだけで、移動を止めるのは <see cref="FishingPlayerController"/>、
/// フックを止めるのは <see cref="HookController"/> の側で <see cref="IsStunned"/> を見て行う。
/// </summary>
public class PlayerStun : MonoBehaviour
{
    [Header("見せ方")]
    [Tooltip("スタン中に赤く点滅させる見た目。プレイヤーの体の Renderer を入れる")]
    [SerializeField] private Renderer[] bodyRenderers;

    [Tooltip("点滅するときの色")]
    [SerializeField] private Color stunColor = new Color(1f, 0.2f, 0.15f);

    [Tooltip("1秒に何回点滅させるか")]
    [SerializeField] private float blinkPerSecond = 6f;

    [Tooltip("スタン中だけ出す、頭の上の印")]
    [SerializeField] private GameObject stunMarker;

    [Tooltip("頭の上の印を回す速さ（1秒あたりの度）")]
    [SerializeField] private float markerSpinSpeed = 240f;

    /// <summary>いま動けない状態か。</summary>
    public bool IsStunned => remaining > 0f;

    /// <summary>スタンが解けるまでの残り秒数。</summary>
    public float Remaining => Mathf.Max(0f, remaining);

    private float remaining;

    // 色を変える前に元の色を控えておく。
    // 一度 renderer.material に触ると専用のマテリアルが作られ、
    // sharedMaterial からは元の色を取り出せなくなるため（AIの申し送り参照）
    private Color[] originalColors;

    private void Awake()
    {
        CaptureColors();

        if (stunMarker != null)
        {
            stunMarker.SetActive(false);
        }
    }

    private void CaptureColors()
    {
        if (bodyRenderers == null)
        {
            originalColors = new Color[0];
            return;
        }

        originalColors = new Color[bodyRenderers.Length];
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            originalColors[i] = bodyRenderers[i] != null
                ? bodyRenderers[i].sharedMaterial.color
                : Color.white;
        }
    }

    /// <summary>
    /// この秒数だけ動けなくする。
    /// すでにスタン中なら、**残りが長いほうを採用**する（短い爆発で上書きしない）。
    /// </summary>
    public void Stun(float seconds)
    {
        remaining = Mathf.Max(remaining, seconds);

        if (stunMarker != null)
        {
            stunMarker.SetActive(true);
        }
    }

    private void Update()
    {
        if (remaining <= 0f)
        {
            return;
        }

        remaining -= Time.deltaTime;

        if (remaining <= 0f)
        {
            remaining = 0f;
            RestoreColors();

            if (stunMarker != null)
            {
                stunMarker.SetActive(false);
            }
            return;
        }

        Blink();

        if (stunMarker != null)
        {
            stunMarker.transform.Rotate(Vector3.up, markerSpinSpeed * Time.deltaTime, Space.World);
        }
    }

    private void Blink()
    {
        bool on = Mathf.Repeat(Time.time * blinkPerSecond, 1f) < 0.5f;

        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            if (bodyRenderers[i] == null)
            {
                continue;
            }
            bodyRenderers[i].material.color = on ? stunColor : originalColors[i];
        }
    }

    private void RestoreColors()
    {
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            if (bodyRenderers[i] == null)
            {
                continue;
            }
            bodyRenderers[i].material.color = originalColors[i];
        }
    }
}

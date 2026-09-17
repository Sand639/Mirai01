using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **爆発する物資。** プレイヤーに釣り上げられてから一定時間で爆発する。
///
/// 流れ：
///   1. フックが当たる → 導火線に火がつく（<see cref="LightFuse"/>）
///   2. 残り時間が減るにつれて**点滅が速くなり、脈打つように大きく縮む**
///      （画面のカウントダウンは <see cref="FishingStatusUI"/> が出す）
///   3. 0になったら爆発。**範囲内の物資は消滅**し、**プレイヤーはスタン**する
///
/// 一度火がついたら、投げても離しても止まらない（熱いジャガイモのように押し付け合える）。
/// </summary>
[RequireComponent(typeof(HookableObject))]
public class ExplosiveObject : MonoBehaviour
{
    /// <summary>
    /// いま火がついている爆発物の一覧。画面のカウントダウン表示が参照する。
    /// **プロトタイプ用の簡単な作り**（静的な一覧）。数が増えたら作り替える。
    /// </summary>
    public static readonly List<ExplosiveObject> LitBombs = new List<ExplosiveObject>();

    [Header("導火線")]
    [Tooltip("釣り上げられてから爆発するまでの秒数")]
    [SerializeField] private float fuseSeconds = 5f;

    [Header("爆発")]
    [Tooltip("爆発が届く範囲（メートル）")]
    [SerializeField] private float explosionRadius = 4.5f;

    [Tooltip("巻き込まれたプレイヤーが動けなくなる秒数")]
    [SerializeField] private float playerStunSeconds = 2f;

    [Tooltip("爆発の見た目。Prefabs/FishingExplosion を入れる")]
    [SerializeField] private ExplosionEffect explosionEffectPrefab;

    [Header("危なさの見せ方")]
    [Tooltip("点滅させる見た目。この物の Renderer を入れる")]
    [SerializeField] private Renderer[] blinkRenderers;

    [Tooltip("点滅する色")]
    [SerializeField] private Color dangerColor = new Color(1f, 0.15f, 0.1f);

    [Tooltip("火がついた直後の点滅回数（1秒あたり）")]
    [SerializeField] private float minBlinkPerSecond = 1.5f;

    [Tooltip("爆発直前の点滅回数（1秒あたり）。速いほど焦る")]
    [SerializeField] private float maxBlinkPerSecond = 16f;

    [Tooltip("爆発直前に、点滅と合わせて何倍まで膨らむか")]
    [SerializeField] private float maxPulseScale = 1.3f;

    /// <summary>導火線に火がついているか。</summary>
    public bool FuseLit { get; private set; }

    /// <summary>爆発までの残り秒数。</summary>
    public float FuseRemaining { get; private set; }

    /// <summary>爆発までの合計秒数（表示の割合計算用）。</summary>
    public float FuseSeconds => fuseSeconds;

    private HookableObject hookable;
    private Color[] originalColors;
    private Vector3 baseScale;

    private void Awake()
    {
        hookable = GetComponent<HookableObject>();
        baseScale = transform.localScale;
        CaptureColors();
    }

    private void OnDestroy()
    {
        LitBombs.Remove(this);
    }

    private void CaptureColors()
    {
        if (blinkRenderers == null)
        {
            originalColors = new Color[0];
            return;
        }

        // 色を変える前に元の色を控えておく（AIの申し送り参照）
        originalColors = new Color[blinkRenderers.Length];
        for (int i = 0; i < blinkRenderers.Length; i++)
        {
            originalColors[i] = blinkRenderers[i] != null
                ? blinkRenderers[i].sharedMaterial.color
                : Color.white;
        }
    }

    /// <summary>導火線に火をつける。<see cref="HookController"/> が釣り上げた瞬間に呼ぶ。</summary>
    public void LightFuse()
    {
        if (FuseLit)
        {
            return;
        }

        FuseLit = true;
        FuseRemaining = fuseSeconds;

        if (!LitBombs.Contains(this))
        {
            LitBombs.Add(this);
        }
    }

    private void Update()
    {
        if (!FuseLit)
        {
            return;
        }

        FuseRemaining -= Time.deltaTime;

        if (FuseRemaining <= 0f)
        {
            Explode();
            return;
        }

        UpdateWarningLook();
    }

    /// <summary>残り時間が減るほど、点滅を速く・脈動を大きくする。</summary>
    private void UpdateWarningLook()
    {
        float progress = 1f - Mathf.Clamp01(FuseRemaining / Mathf.Max(0.01f, fuseSeconds));
        float hz = Mathf.Lerp(minBlinkPerSecond, maxBlinkPerSecond, progress);
        bool on = Mathf.Repeat(Time.time * hz, 1f) < 0.5f;

        for (int i = 0; i < blinkRenderers.Length; i++)
        {
            if (blinkRenderers[i] == null)
            {
                continue;
            }
            blinkRenderers[i].material.color = on ? dangerColor : originalColors[i];
        }

        float pulse = on ? Mathf.Lerp(1f, maxPulseScale, progress) : 1f;
        transform.localScale = baseScale * pulse;
    }

    private void Explode()
    {
        SpawnEffect();
        ApplyDamage();

        // 火を消して見た目を戻してから消える。
        // 復活する設定なら、この物は元の場所に戻ってきて、また使える
        FuseLit = false;
        FuseRemaining = 0f;
        LitBombs.Remove(this);
        RestoreLook();

        // オンラインでは、ホストが「火を消した」ことも全員に配る
        // （消さないと、途中から入ってきた人に燃えたままの状態が届いてしまう）
        FishingNetBomb netBomb = GetComponent<FishingNetBomb>();
        if (netBomb != null)
        {
            netBomb.ServerResetFuse();
        }

        hookable.Vanish();
    }

    private void SpawnEffect()
    {
        if (explosionEffectPrefab == null)
        {
            return;
        }

        ExplosionEffect effect = Instantiate(
            explosionEffectPrefab, transform.position, Quaternion.identity);
        effect.Play(explosionRadius);
    }

    /// <summary>範囲内の物資を消し、プレイヤーをスタンさせる。</summary>
    private void ApplyDamage()
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position, explosionRadius, ~0, QueryTriggerInteraction.Ignore);

        // 1つの物に複数の当たり判定が付いていても、1回だけ処理する
        HashSet<GameObject> handled = new HashSet<GameObject>();

        foreach (Collider hit in hits)
        {
            PlayerStun stun = hit.GetComponentInParent<PlayerStun>();
            if (stun != null && handled.Add(stun.gameObject))
            {
                stun.Stun(playerStunSeconds);
            }

            HookableObject other = hit.GetComponentInParent<HookableObject>();
            if (other != null && other != hookable && handled.Add(other.gameObject))
            {
                other.Vanish();
            }
        }
    }

    private void RestoreLook()
    {
        transform.localScale = baseScale;

        for (int i = 0; i < blinkRenderers.Length; i++)
        {
            if (blinkRenderers[i] == null)
            {
                continue;
            }
            blinkRenderers[i].material.color = originalColors[i];
        }
    }
}

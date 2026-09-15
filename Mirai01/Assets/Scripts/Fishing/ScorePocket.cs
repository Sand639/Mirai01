using UnityEngine;

/// <summary>
/// **物資を投げ入れる「ポケット」。** 中に物資が入ると点数が入る。
///
/// ステージの四方の壁の中央に1つずつ置いてある。
/// **Owner Player Index を分けておけば、将来プレイヤーが増えたときに
/// 「このポケットは誰の運び先か」をそのまま表せる。**
///
/// フックで引っ張っている最中の物資は数えない（引きずり込みで稼げないようにするため）。
/// 点数が 0 の物資（爆発物など）も数えない。
/// </summary>
[RequireComponent(typeof(Collider))]
public class ScorePocket : MonoBehaviour
{
    [Header("このポケットは誰のものか")]
    [Tooltip("画面に出す名前（「北」など）")]
    [SerializeField] private string pocketLabel = "北";

    [Tooltip("点が入るプレイヤーの番号。0が1人目。**将来の複数人対応用**")]
    [SerializeField] private int ownerPlayerIndex = 0;

    [Header("参照")]
    [Tooltip("点数を持っている ScoreBoard")]
    [SerializeField] private ScoreBoard scoreBoard;

    [Header("入ったときの合図")]
    [Tooltip("入ったときに光らせる床の Renderer")]
    [SerializeField] private Renderer padRenderer;

    [Tooltip("光る色")]
    [SerializeField] private Color flashColor = new Color(0.35f, 1f, 0.45f);

    [Tooltip("光っている秒数")]
    [SerializeField] private float flashSeconds = 0.4f;

    private Color originalColor = Color.white;
    private float flashUntil = -999f;

    private void Awake()
    {
        // 色を変える前に元の色を控えておく（AIの申し送り参照）
        if (padRenderer != null)
        {
            originalColor = padRenderer.sharedMaterial.color;
        }

        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        HookableObject supply = other.GetComponentInParent<HookableObject>();

        if (supply == null || supply.IsHooked || supply.IsVanished || supply.ScoreValue <= 0)
        {
            return;
        }

        if (scoreBoard != null)
        {
            scoreBoard.AddScore(ownerPlayerIndex, supply.ScoreValue, pocketLabel);
        }

        flashUntil = Time.time + flashSeconds;
        supply.Vanish();
    }

    private void Update()
    {
        if (padRenderer == null)
        {
            return;
        }

        bool lit = Time.time < flashUntil;
        padRenderer.material.color = lit ? flashColor : originalColor;
    }
}

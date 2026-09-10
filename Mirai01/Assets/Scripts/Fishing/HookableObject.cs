using System.Collections;
using UnityEngine;

/// <summary>
/// **フックで引っ掛けられる「物資」に付ける目印。**
///
/// これが付いている物だけが、フックの対象になる。
/// フィールドに置いた箱などに付けて使う。
///
/// 引き寄せ・投げの処理そのものは <see cref="ThrowController"/> にある。
/// こちらは「引っ掛けられる物かどうか」「糸を結ぶ位置」「点数」「消えて戻ってくる」だけを持つ。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class HookableObject : MonoBehaviour
{
    [Header("糸・フックの結び目")]
    [Tooltip("物の中心から、どれだけずらした位置に糸とフックを結ぶか（物のローカル座標）")]
    [SerializeField] private Vector3 hookAnchorOffset = Vector3.zero;

    [Header("点数")]
    [Tooltip("ポケットに入れたときに入る点数。0にするとポケットが無視する（爆発物など）")]
    [SerializeField] private int scoreValue = 1;

    [Header("消えたあとの復活")]
    [Tooltip("消えてから元の場所に戻ってくるまでの秒数。0にすると戻ってこない（そのまま削除）")]
    [SerializeField] private float respawnSeconds = 6f;

    /// <summary>この物の Rigidbody。投げるときに力を加える相手。</summary>
    public Rigidbody Body { get; private set; }

    /// <summary>いまフックされているか。**二重に引っ掛けるのを防ぐ**ために使う。</summary>
    public bool IsHooked { get; private set; }

    /// <summary>いま消えている（復活を待っている）か。</summary>
    public bool IsVanished { get; private set; }

    /// <summary>ポケットに入れたときに入る点数。</summary>
    public int ScoreValue => scoreValue;

    /// <summary>糸とフックを結ぶワールド座標。</summary>
    public Vector3 AnchorPoint => transform.TransformPoint(hookAnchorOffset);

    private Vector3 startPosition;
    private Quaternion startRotation;

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();

        // 復活させる場所として、最初に置かれていた位置を覚えておく
        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    /// <summary>フックされた／外れた、を記録する。<see cref="HookController"/> から呼ばれる。</summary>
    public void SetHooked(bool hooked)
    {
        IsHooked = hooked;
    }

    /// <summary>
    /// **この物を消す。** ポケットに入ったときと、爆発に巻き込まれたときに呼ばれる。
    ///
    /// Respawn Seconds が 0 より大きければ、その秒数後に元の場所へ戻ってくる。
    /// （プロトタイプで物資が尽きて遊べなくならないようにするため）
    /// </summary>
    public void Vanish()
    {
        if (IsVanished)
        {
            return;
        }

        SetHooked(false);

        if (respawnSeconds <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        IsVanished = true;
        StartCoroutine(VanishRoutine());
    }

    private IEnumerator VanishRoutine()
    {
        SetPartsEnabled(false);
        Body.isKinematic = true;
        Body.linearVelocity = Vector3.zero;
        Body.angularVelocity = Vector3.zero;

        yield return new WaitForSeconds(respawnSeconds);

        transform.SetPositionAndRotation(startPosition, startRotation);
        Body.isKinematic = false;
        Body.linearVelocity = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
        SetPartsEnabled(true);

        IsVanished = false;
    }

    /// <summary>見た目と当たり判定をまとめて出す／消す。オブジェクト自体は動かし続ける（復活を数えるため）。</summary>
    private void SetPartsEnabled(bool enabledState)
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = enabledState;
        }

        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = enabledState;
        }
    }
}

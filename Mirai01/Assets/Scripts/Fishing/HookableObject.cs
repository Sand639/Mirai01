using UnityEngine;

/// <summary>
/// **フックで引っ掛けられる「物資」に付ける目印。**
///
/// これが付いている物だけが、フックの対象になる。
/// フィールドに置いた箱などに付けて使う。
///
/// 引き寄せ・投げの処理そのものは <see cref="ThrowController"/> にある。
/// こちらは「引っ掛けられる物かどうか」と、糸を結ぶ位置だけを持つ。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class HookableObject : MonoBehaviour
{
    [Header("糸・フックの結び目")]
    [Tooltip("物の中心から、どれだけずらした位置に糸とフックを結ぶか（物のローカル座標）")]
    [SerializeField] private Vector3 hookAnchorOffset = Vector3.zero;

    /// <summary>この物の Rigidbody。投げるときに力を加える相手。</summary>
    public Rigidbody Body { get; private set; }

    /// <summary>いまフックされているか。**二重に引っ掛けるのを防ぐ**ために使う。</summary>
    public bool IsHooked { get; private set; }

    /// <summary>糸とフックを結ぶワールド座標。</summary>
    public Vector3 AnchorPoint => transform.TransformPoint(hookAnchorOffset);

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
    }

    /// <summary>フックされた／外れた、を記録する。<see cref="HookController"/> から呼ばれる。</summary>
    public void SetHooked(bool hooked)
    {
        IsHooked = hooked;
    }
}

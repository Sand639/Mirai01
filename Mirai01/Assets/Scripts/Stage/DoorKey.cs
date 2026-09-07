using UnityEngine;

/// <summary>
/// **鍵。** 持ち運んで、鍵付きの扉を開けるための物。
///
/// **持ち運びは「持てる物」の仕組みをそのまま使っている**（<see cref="Grabbable"/>）。
/// つまり、
///
/// - 上半身（と合体した体）が **F で持てる**
/// - 持ったまま歩ける。**下半身に切り替えても、上半身が持ったまま待っている**
///
/// 鍵として特別なのは、**<see cref="LockedDoor"/> に使える**という一点だけ。
/// 扉に使うと、**その場で無くなる**（<see cref="RobotKeyUser"/> が消す）。
///
/// ## 鍵の種類を分けたいとき
///
/// `keyId` に同じ言葉を入れた鍵と扉だけが合う。
/// **空のままなら、どの鍵でもどの扉でも開く**ので、1組しか使わないなら空でよい。
/// </summary>
[RequireComponent(typeof(Grabbable))]
public class DoorKey : MonoBehaviour
{
    [Header("鍵の種類")]
    [Tooltip("**同じ言葉を入れた扉だけが開く。** 空なら、どの扉でも開く（例：`赤` `倉庫`）")]
    [SerializeField] private string keyId = "";

    /// <summary>この鍵の種類。空なら、どの扉にも合う。</summary>
    public string KeyId => keyId;
}

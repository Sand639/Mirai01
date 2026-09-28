using UnityEngine;

/// <summary>
/// **宇宙船の素材の種類。** ラウンドの勝利条件がこの3種類で決まる。
///
/// **自陣のゴールに、この3種類を1個ずつ入れたチームがラウンド勝利。**
/// 同じ種類を2個目入れても何も起きない（消えるだけ）。
///
/// 種類を増やしたくなったら、ここに足して <see cref="SpaceJunkMaterials.Count"/> を見直す。
/// ただし**勝利条件（3種類そろえる）も一緒に変わる**ので、そのつもりで。
/// </summary>
public enum SpaceJunkMaterialKind
{
    /// <summary>装甲板</summary>
    Plate = 0,

    /// <summary>回路基板</summary>
    Circuit = 1,

    /// <summary>燃料タンク</summary>
    Fuel = 2
}

/// <summary>
/// 素材の種類ごとの、名前と色をまとめた場所。画面表示・素材の見た目・ゴールの表示に使う。
///
/// **色は、チームの色（青・赤・緑・黄）と見分けがつくものを選んである。**
/// 素材の色とチームの色が似ていると、「誰の物か」と「何の素材か」が混ざって読めなくなるため。
/// </summary>
public static class SpaceJunkMaterials
{
    /// <summary>素材の種類の数。**ラウンド勝利に必要な数でもある。**</summary>
    public const int Count = 3;

    private static readonly string[] Names = { "装甲板", "回路基板", "燃料タンク" };

    private static readonly Color[] Colors =
    {
        new Color(0.90f, 0.90f, 0.93f), // 装甲板 … 白
        new Color(0.62f, 0.35f, 0.88f), // 回路基板 … 紫
        new Color(1.00f, 0.62f, 0.10f), // 燃料タンク … 橙
    };

    /// <summary>種類の名前。</summary>
    public static string Name(SpaceJunkMaterialKind kind)
    {
        return Names[Mathf.Clamp((int)kind, 0, Names.Length - 1)];
    }

    /// <summary>種類の色。</summary>
    public static Color Color(SpaceJunkMaterialKind kind)
    {
        return Colors[Mathf.Clamp((int)kind, 0, Colors.Length - 1)];
    }

    /// <summary>番号から種類へ。スポナーがランダムに選ぶときに使う。</summary>
    public static SpaceJunkMaterialKind FromIndex(int index)
    {
        return (SpaceJunkMaterialKind)Mathf.Clamp(index, 0, Count - 1);
    }
}

/// <summary>
/// **「この物は宇宙船の素材で、種類はこれ」という目印。**
///
/// フックで引っ掛けられるようにする目印（<c>HookableObject</c>）は**釣りと共用**しているので、
/// こちらは**種類だけ**を持つ。素材のプレハブは種類ごとに1つずつ作るので、
/// 種類は最初から決まっていて、通信で送る必要がない。
///
/// 使い方：素材のプレハブに <c>HookableObject</c> と一緒に付ける。
/// </summary>
public class SpaceJunkMaterial : MonoBehaviour
{
    [Header("この素材の種類")]
    [Tooltip("ゴールに入れたときに、どの種類として数えるか")]
    [SerializeField] private SpaceJunkMaterialKind kind = SpaceJunkMaterialKind.Plate;

    [Header("見た目")]
    [Tooltip("種類の色に塗る見た目。**空にしておけば色を変えない**（自分でマテリアルを作ったとき用）")]
    [SerializeField] private Renderer[] tintRenderers;

    /// <summary>この素材の種類。</summary>
    public SpaceJunkMaterialKind Kind => kind;

    /// <summary>画面に出す名前。</summary>
    public string KindName => SpaceJunkMaterials.Name(kind);

    private void Start()
    {
        ApplyColor();
    }

    /// <summary>種類の色を見た目に塗る。</summary>
    private void ApplyColor()
    {
        if (tintRenderers == null)
        {
            return;
        }

        Color color = SpaceJunkMaterials.Color(kind);

        foreach (Renderer renderer in tintRenderers)
        {
            if (renderer != null)
            {
                renderer.material.color = color;
            }
        }
    }
}

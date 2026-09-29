using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// **電撃1本ぶんの線の見た目**。「太くて薄い光（Glow）」と「細くて明るい芯（Core）」の2本の LineRenderer を重ねて描く。
///
/// 色は HDR（1 より大きい値）で渡すので、カメラのブルーム（光のにじみ）で光って見える。
/// 線は親オブジェクト基準（<c>useWorldSpace = false</c>）なので、親が動けば一緒に動く。
/// </summary>
public class LightningLine
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private readonly LineRenderer glow;
    private readonly LineRenderer core;
    private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

    public LightningLine(Transform parent, string name, Material material)
    {
        glow = CreateLine(parent, name + "_Glow", material);
        core = CreateLine(parent, name + "_Core", material);
    }

    public void Set(Vector3[] positions, int count, Color glowColor, Color coreColor, float glowWidth, float coreWidth)
    {
        SetLine(glow, positions, count, glowColor, glowWidth);
        SetLine(core, positions, count, coreColor, coreWidth);
    }

    public void SetVisible(bool visible)
    {
        glow.enabled = visible;
        core.enabled = visible;
    }

    private void SetLine(LineRenderer line, Vector3[] positions, int count, Color color, float width)
    {
        line.positionCount = count;

        for (int i = 0; i < count; i++)
        {
            line.SetPosition(i, positions[i]);
        }

        line.widthMultiplier = width;

        block.SetColor(BaseColorId, color);
        line.SetPropertyBlock(block);

        line.enabled = true;
    }

    private static LineRenderer CreateLine(Transform parent, string name, Material material)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);

        var line = child.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.sharedMaterial = material;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.numCapVertices = 2;
        line.alignment = LineAlignment.View;

        // 両端を細くする
        line.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.2f),
            new Keyframe(0.3f, 1f),
            new Keyframe(0.7f, 1f),
            new Keyframe(1f, 0.2f));

        line.enabled = false;
        return line;
    }

    /// <summary>
    /// 線の太さの変わり方を差し替える（ビームの芯のように「根元から先まで同じ太さ」にしたいとき）。
    /// </summary>
    public void SetWidthCurve(AnimationCurve curve)
    {
        glow.widthCurve = curve;
        core.widthCurve = curve;
    }

    // ------------------------------------------------------------
    // マテリアル
    // ------------------------------------------------------------

    /// <summary>マテリアルが指定されていないときの予備。URP の Particles/Unlit を加算合成にする。</summary>
    public static Material CreateAdditiveMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        var material = new Material(shader);
        SetupAdditive(material);
        return material;
    }

    /// <summary>URP の Particles/Unlit を「透明・加算合成」に設定する。</summary>
    public static void SetupAdditive(Material material)
    {
        material.SetFloat("_Surface", 1f);    // 透明
        material.SetFloat("_Blend", 2f);      // 加算
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.One);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetColor("_BaseColor", Color.white);
    }
}

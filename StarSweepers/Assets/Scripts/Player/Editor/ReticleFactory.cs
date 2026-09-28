using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// **画面中央のレティクル（照準）を作る、共通の道具。**
///
/// プレイヤーのプロトタイプとロボットのプロトタイプの**両方で使う**ので、
/// それぞれの生成ツールに同じものを書かず、ここにまとめてある。
///
/// 置き場所が `Player/Editor` なのは、`Reticle.cs` 本体が `Player/` にあるため。
/// レティクルに関するものを1か所に集めている。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class ReticleFactory
{
    /// <summary>
    /// レティクルを作って返す。
    /// 中央に小さな点、その周りに4本の線を置いた形。
    ///
    /// 一人称ではマウスカーソルを消しているため、これが無いと狙いが分からない。
    /// </summary>
    public static Reticle Create(Transform parent)
    {
        GameObject canvasObject = new GameObject("ReticleCanvas");
        canvasObject.transform.SetParent(parent, false);
        canvasObject.layer = LayerMask.NameToLayer("UI");

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // 画面の大きさが変わっても、見た目の比率が変わらないようにする
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject reticleObject = new GameObject("Reticle");
        reticleObject.transform.SetParent(canvasObject.transform, false);
        reticleObject.layer = canvasObject.layer;

        RectTransform reticleRect = reticleObject.AddComponent<RectTransform>();
        reticleRect.anchorMin = new Vector2(0.5f, 0.5f);
        reticleRect.anchorMax = new Vector2(0.5f, 0.5f);
        reticleRect.anchoredPosition = Vector2.zero;
        reticleRect.sizeDelta = new Vector2(40f, 40f);

        List<Graphic> parts = new List<Graphic>
        {
            CreatePart(reticleRect, "Center", new Vector2(0f, 0f), new Vector2(3f, 3f)),
            CreatePart(reticleRect, "Up", new Vector2(0f, 11f), new Vector2(2f, 9f)),
            CreatePart(reticleRect, "Down", new Vector2(0f, -11f), new Vector2(2f, 9f)),
            CreatePart(reticleRect, "Left", new Vector2(-11f, 0f), new Vector2(9f, 2f)),
            CreatePart(reticleRect, "Right", new Vector2(11f, 0f), new Vector2(9f, 2f)),
        };

        Reticle reticle = reticleObject.AddComponent<Reticle>();

        SerializedObject serialized = new SerializedObject(reticle);
        SerializedProperty partsProperty = serialized.FindProperty("parts");
        partsProperty.arraySize = parts.Count;
        for (int i = 0; i < parts.Count; i++)
        {
            partsProperty.GetArrayElementAtIndex(i).objectReferenceValue = parts[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return reticle;
    }

    /// <summary>レティクルを作っている線や点を1つ作る。画像は使わず、白い四角をそのまま使う。</summary>
    private static Graphic CreatePart(RectTransform parent, string name, Vector2 position, Vector2 size)
    {
        GameObject part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.layer = parent.gameObject.layer;

        RectTransform rect = part.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = part.AddComponent<Image>();
        image.raycastTarget = false; // 押せる必要はないので、当たり判定を持たせない

        return image;
    }
}

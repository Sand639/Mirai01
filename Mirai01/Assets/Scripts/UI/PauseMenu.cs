using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// **Escape で開くポーズ画面。**
///
/// | 項目 | 押すとどうなるか |
/// | **ゲームをつづける** | 画面を閉じて、続きから遊ぶ |
/// | **設定** | 音量とマウス感度を変える画面へ |
/// | **ゲームをやめる** | ゲームを終了する（エディタでは再生を止める） |
///
/// **マウスで選ぶ。** 項目に重なると**色が変わり、少し大きくなり、左に「▶」が出る**
/// （<see cref="MenuHoverHighlight"/>）。
///
/// ## 画面はコードで組み立てている
///
/// **プレハブに画面を作り込んでいない。** 再生したときに、この部品が自分で作る。
///
/// 理由は**日本語のため。** Unityに最初から入っている文字は日本語を持っていないので、
/// そのままだと項目名が四角（□□□）になってしまう。
/// **パソコンに入っている日本語フォントを、再生したときに借りてくる**必要があり、
/// それは保存できない（プレハブに残らない）ので、作るところまで含めてコードにしてある。
///
/// ## 置き方
///
/// **空のゲームオブジェクトにこの部品を付けるだけ。**
/// カメラもUIも要らない（足りない物はこちらで作る）。
/// メニューの `Tools > Mirai01 > ポーズ画面を置く` でも置ける。
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Header("キーの割り当て")]
    [Tooltip("ポーズ画面を開く／閉じるキー")]
    [SerializeField] private Key pauseKey = Key.Escape;

    [Header("つなぐもの")]
    [Tooltip("Assets/InputSystem_Actions を入れる。**UIのクリックに使う。**" +
             "シーンにすでに EventSystem があるなら空でもよい")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("文字")]
    [Tooltip("使いたい日本語フォントの名前。上から順に、パソコンに入っているものを探す")]
    [SerializeField]
    private string[] fontNames =
    {
        "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo", "MS UI Gothic", "Noto Sans CJK JP",
    };

    [Header("見た目")]
    [Tooltip("後ろを暗くする色")]
    [SerializeField] private Color backdropColor = new Color(0f, 0f, 0f, 0.65f);

    [Tooltip("項目の色（ふだん）")]
    [SerializeField] private Color itemColor = new Color(0.16f, 0.18f, 0.22f, 0.95f);

    [Tooltip("項目の色（マウスが重なったとき）")]
    [SerializeField] private Color itemHoverColor = new Color(0.30f, 0.55f, 0.85f, 1f);

    [Tooltip("文字の色")]
    [SerializeField] private Color textColor = Color.white;

    [Tooltip("項目の文字の大きさ")]
    [Range(14, 60)]
    [SerializeField] private int fontSize = 28;

    /// <summary>いまポーズ画面が開いているか。</summary>
    public bool IsOpen { get; private set; }

    private GameObject root;
    private GameObject mainPage;
    private GameObject settingsPage;
    private Font font;

    // 画面の作り。1920x1080 を基準にした大きさ
    private const float ItemWidth = 420f;
    private const float ItemHeight = 72f;
    private const float ItemGap = 16f;

    private void Awake()
    {
        font = FindFont();

        Build();
        SetVisible(false);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard[pauseKey].wasPressedThisFrame)
        {
            Toggle();
        }
    }

    private void OnDisable()
    {
        // 開いたまま消えると、**止まったまま戻せなくなる**
        if (IsOpen)
        {
            Close();
        }
    }

    // ------------------------------------------------------------
    // 開く・閉じる
    // ------------------------------------------------------------

    /// <summary>開いていれば閉じ、閉じていれば開く。</summary>
    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    /// <summary>ポーズ画面を開く。**ゲームは止まる。**</summary>
    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;

        ShowSettings(false);
        SetVisible(true);

        GamePause.SetPaused(true);

        // マウスで選ぶので、カーソルを出す
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>ポーズ画面を閉じる。**ゲームが動き出す。**</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;

        SetVisible(false);
        GamePause.SetPaused(false);

        // 遊びに戻るので、カーソルを画面に固定して消す
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>設定の画面と、最初の画面を切り替える。</summary>
    public void ShowSettings(bool show)
    {
        if (mainPage != null)
        {
            mainPage.SetActive(!show);
        }

        if (settingsPage != null)
        {
            settingsPage.SetActive(show);
        }
    }

    /// <summary>
    /// ゲームを終了する。
    /// **エディタで遊んでいるときは、再生を止めるだけ**（エディタごと閉じたら困るため）。
    /// </summary>
    public void QuitGame()
    {
        Debug.Log("[UI] ゲームを終了します");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void SetVisible(bool visible)
    {
        if (root != null)
        {
            root.SetActive(visible);
        }
    }

    // ------------------------------------------------------------
    // 画面を組み立てる
    // ------------------------------------------------------------

    private void Build()
    {
        EnsureEventSystem();

        // ----- 一番外側（画面いっぱいに広がる） -----
        root = new GameObject("PauseCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // ほかのUIより手前に出す
        canvas.sortingOrder = 100;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // ----- 後ろを暗くする板（クリックが後ろへ抜けるのも防ぐ） -----
        GameObject backdrop = CreateImage("Backdrop", root.transform, backdropColor);
        Stretch(backdrop.GetComponent<RectTransform>());

        mainPage = CreatePage("MainPage", "ポーズ");
        settingsPage = CreatePage("SettingsPage", "設定");

        BuildMainPage();
        BuildSettingsPage();
    }

    /// <summary>1ページ分の入れ物を作る（見出し付き）。</summary>
    private GameObject CreatePage(string name, string title)
    {
        GameObject page = new GameObject(name, typeof(RectTransform));
        page.transform.SetParent(root.transform, false);
        Stretch(page.GetComponent<RectTransform>());

        GameObject titleObject = CreateText("Title", page.transform, title, fontSize + 12);
        RectTransform titleRect = titleObject.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.5f);
        titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.sizeDelta = new Vector2(ItemWidth, ItemHeight);
        titleRect.anchoredPosition = new Vector2(0f, 190f);

        return page;
    }

    private void BuildMainPage()
    {
        CreateMenuItem(mainPage.transform, "ゲームをつづける", 90f, Close);
        CreateMenuItem(mainPage.transform, "設定", 90f - (ItemHeight + ItemGap), () => ShowSettings(true));
        CreateMenuItem(mainPage.transform, "ゲームをやめる", 90f - (ItemHeight + ItemGap) * 2f, QuitGame);
    }

    private void BuildSettingsPage()
    {
        CreateSlider(settingsPage.transform, "音量", 90f, GameSettings.Volume, 0f, 1f,
            value => GameSettings.Volume = value);

        CreateSlider(settingsPage.transform, "マウス感度", 90f - 96f, GameSettings.MouseSensitivity, 0.2f, 3f,
            value => GameSettings.MouseSensitivity = value);

        CreateMenuItem(settingsPage.transform, "もどる", 90f - (ItemHeight + ItemGap) * 2f,
            () => ShowSettings(false));
    }

    /// <summary>押せる項目を1つ作る。マウスが重なったときの見せ方もここで付ける。</summary>
    private void CreateMenuItem(Transform parent, string label, float y, UnityEngine.Events.UnityAction onClick)
    {
        GameObject item = CreateImage(label, parent, itemColor);

        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(ItemWidth, ItemHeight);
        rect.anchoredPosition = new Vector2(0f, y);

        Button button = item.AddComponent<Button>();
        button.targetGraphic = item.GetComponent<Image>();

        // 色は自分で変えるので、ボタンの色替えは切っておく（二重に変わると濁る）
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(onClick);

        CreateText("Label", item.transform, label, fontSize);

        // マウスが重なったときに出る印
        GameObject marker = CreateText("Marker", item.transform, "▶", fontSize);
        RectTransform markerRect = marker.GetComponent<RectTransform>();
        markerRect.anchorMin = new Vector2(0f, 0.5f);
        markerRect.anchorMax = new Vector2(0f, 0.5f);
        markerRect.sizeDelta = new Vector2(40f, ItemHeight);
        markerRect.anchoredPosition = new Vector2(26f, 0f);
        marker.SetActive(false);

        MenuHoverHighlight highlight = item.AddComponent<MenuHoverHighlight>();
        highlight.Setup(item.GetComponent<Image>(), itemColor, itemHoverColor, marker);
    }

    /// <summary>設定用のスライダーを1本作る。</summary>
    private void CreateSlider(Transform parent, string label, float y, float value,
        float min, float max, UnityEngine.Events.UnityAction<float> onChanged)
    {
        GameObject row = new GameObject(label, typeof(RectTransform));
        row.transform.SetParent(parent, false);

        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 0.5f);
        rowRect.anchorMax = new Vector2(0.5f, 0.5f);
        rowRect.sizeDelta = new Vector2(ItemWidth, ItemHeight);
        rowRect.anchoredPosition = new Vector2(0f, y);

        GameObject labelObject = CreateText("Label", row.transform, label, fontSize - 4);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0.5f);
        labelRect.anchorMax = new Vector2(0f, 0.5f);
        labelRect.sizeDelta = new Vector2(180f, ItemHeight);
        labelRect.anchoredPosition = new Vector2(100f, 0f);
        labelObject.GetComponent<Text>().alignment = TextAnchor.MiddleLeft;

        // ----- スライダー本体 -----
        GameObject sliderObject = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        sliderObject.transform.SetParent(row.transform, false);

        RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(1f, 0.5f);
        sliderRect.anchorMax = new Vector2(1f, 0.5f);
        sliderRect.sizeDelta = new Vector2(200f, 16f);
        sliderRect.anchoredPosition = new Vector2(-110f, 0f);

        GameObject background = CreateImage("Background", sliderObject.transform, itemColor);
        Stretch(background.GetComponent<RectTransform>());

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderObject.transform, false);
        Stretch(fillArea.GetComponent<RectTransform>());

        GameObject fill = CreateImage("Fill", fillArea.transform, itemHoverColor);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.sizeDelta = new Vector2(10f, 0f);

        GameObject handle = CreateImage("Handle", sliderObject.transform, Color.white);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(20f, 32f);

        Slider slider = sliderObject.GetComponent<Slider>();
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        slider.onValueChanged.AddListener(onChanged);
    }

    // ------------------------------------------------------------
    // 部品づくり
    // ------------------------------------------------------------

    private static GameObject CreateImage(string name, Transform parent, Color color)
    {
        GameObject image = new GameObject(name, typeof(RectTransform), typeof(Image));
        image.transform.SetParent(parent, false);
        image.GetComponent<Image>().color = color;

        return image;
    }

    private GameObject CreateText(string name, Transform parent, string content, int size)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);

        Text text = textObject.GetComponent<Text>();
        text.text = content;
        text.font = font;
        text.fontSize = size;
        text.color = textColor;
        text.alignment = TextAnchor.MiddleCenter;

        // 文字はクリックの邪魔をしない（下のボタンに通す）
        text.raycastTarget = false;

        Stretch(textObject.GetComponent<RectTransform>());

        return textObject;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// **パソコンに入っている日本語フォントを借りてくる。**
    ///
    /// Unityに最初から入っている文字は日本語を持っていないので、
    /// そのままだと項目名が四角（□□□）になってしまう。
    /// </summary>
    private Font FindFont()
    {
        Font found = Font.CreateDynamicFontFromOSFont(fontNames, fontSize);

        if (found != null)
        {
            return found;
        }

        Debug.LogWarning("[UI] 日本語のフォントが見つかりませんでした。文字が四角になるかもしれません。");

        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    /// <summary>
    /// **UIのクリックを受け取る仕組みがなければ作る。**
    ///
    /// このプロジェクトは新しい入力方式（Input System）を使っているので、
    /// 古い受け取り方（`StandaloneInputModule`）ではエラーになる。
    /// </summary>
    private void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem",
            typeof(EventSystem), typeof(InputSystemUIInputModule));

        InputSystemUIInputModule module = eventSystem.GetComponent<InputSystemUIInputModule>();

        if (inputActions != null)
        {
            // 入れてあれば、そこにある「UI」の設定を使う
            module.actionsAsset = inputActions;
        }

        Debug.Log("[UI] UIのクリックを受け取る EventSystem を作りました");
    }
}

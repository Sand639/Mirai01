using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 目の前のオブジェクトを複製して、好きな場所に置ける機能。
///
/// 遊び方：
///   E          … 見ているオブジェクトを複製する（プレビューが出る）
///   マウス移動 … 置く場所を決める
///   左クリック … その場所に確定して置く
///   Escape     … 置くのをやめる
///   R          … 自分が置いたものを消す
///
/// 置ける数には上限があり、超えると**一番古いものが自動で消える**。
/// 消える少し前に点滅して知らせる。
///
/// PlayerRig に付けて使う。参照はインスペクターから受け取るので、
/// 他のオブジェクトを名前で探しにいかない。
/// </summary>
public class ObjectCloner : MonoBehaviour
{
    [Header("つなぐもの")]
    [Tooltip("狙いを決めるカメラ。PlayerRig の中の PlayerCamera を入れる")]
    [SerializeField] private Camera aimCamera;

    [Tooltip("自分自身。ここに含まれるものは狙いの対象から外す")]
    [SerializeField] private Transform playerRoot;

    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("狙える距離")]
    [Tooltip("複製したいものを狙える距離（メートル）")]
    [SerializeField] private float pickDistance = 10f;

    [Tooltip("置き場所を探せる距離（メートル）")]
    [SerializeField] private float placeDistance = 30f;

    [Header("置ける数")]
    [Tooltip("同時に置いておける数。超えると一番古いものが消える")]
    [SerializeField] private int maxPlacedObjects = 5;

    [Tooltip("消える前に点滅させる時間（秒）")]
    [SerializeField] private float warningSeconds = 0.5f;

    [Tooltip("消える予告の点滅の色")]
    [SerializeField] private Color warningColor = new Color(1f, 0.35f, 0.25f);

    [Header("置き方")]
    [Tooltip("床にめり込まないよう、当たった面から少し浮かせる量（メートル）")]
    [SerializeField] private float placeOffset = 0.02f;

    [Tooltip("プレビュー中の色。半透明にはせず色だけ変える")]
    [SerializeField] private Color previewColor = new Color(0.45f, 0.95f, 0.65f);

    [Header("狙っているものの強調")]
    [Tooltip("画面中央の照準。無くても動く")]
    [SerializeField] private Reticle reticle;

    [Tooltip("複製できるものを狙ったときの色（水色）")]
    [SerializeField] private Color highlightColor = new Color(0.45f, 0.85f, 1f);

    [Tooltip("自分が置いたもの（Rで消せる）を狙ったときの色")]
    [SerializeField] private Color removableColor = new Color(1f, 0.72f, 0.40f);

    [Header("キーの割り当て")]
    [Tooltip("複製するキー。入力設定の Interact（長押し設定つき）でも複製できるが、押した瞬間に反応させるためこちらも見ている")]
    [SerializeField] private Key pickKey = Key.E;

    [Tooltip("複製をやめるキー")]
    [SerializeField] private Key cancelKey = Key.Escape;

    [Tooltip("置いたものを消すキー")]
    [SerializeField] private Key removeKey = Key.R;

    /// <summary>複製元だと分かるタグ。</summary>
    public const string DuplicableTag = "Duplicable";

    /// <summary>自分が置いた複製だと分かるタグ。</summary>
    public const string PlacedCloneTag = "PlacedClone";

    private readonly Queue<GameObject> placedObjects = new Queue<GameObject>();
    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    // 色を変える前に元の色を控えておく。
    // 一度 renderer.material を触ると sharedMaterial も複製側を指すようになり、
    // 「元の色」を後から取り出せなくなるため。
    private readonly List<Renderer> previewRenderers = new List<Renderer>();
    private readonly List<Color> previewOriginalColors = new List<Color>();
    private readonly List<Renderer> warningRenderers = new List<Renderer>();
    private readonly List<Color> warningOriginalColors = new List<Color>();
    private readonly List<Renderer> highlightRenderers = new List<Renderer>();
    private readonly List<Color> highlightOriginalColors = new List<Color>();

    private GameObject highlightedObject;

    private InputActionMap playerMap;
    private InputAction pickAction;    // E（Interact）
    private InputAction placeAction;   // 左クリック（Attack）

    private GameObject previewObject;
    private GameObject warningObject;
    private float warningTimer;

    /// <summary>いま置いてある数。UIから見たいときのために公開している。</summary>
    public int PlacedCount => placedObjects.Count;

    /// <summary>置ける最大数。</summary>
    public int MaxPlacedObjects => maxPlacedObjects;

    /// <summary>いまプレビュー中かどうか。</summary>
    public bool IsPreviewing => previewObject != null;

    private void Awake()
    {
        if (aimCamera == null)
        {
            Debug.LogError($"{name}: カメラが入っていません。複製機能を止めます。", this);
            enabled = false;
            return;
        }

        if (inputActions == null)
        {
            Debug.LogError($"{name}: 入力の設定（InputSystem_Actions）が入っていません。", this);
            enabled = false;
            return;
        }

        playerMap = inputActions.FindActionMap("Player", true);
        pickAction = playerMap.FindAction("Interact", true);
        placeAction = playerMap.FindAction("Attack", true);
    }

    private void OnEnable()
    {
        playerMap?.Enable();
    }

    private void OnDisable()
    {
        playerMap?.Disable();
    }

    private void Update()
    {
        UpdateWarningBlink();

        if (previewObject == null)
        {
            UpdateHighlight();

            // 入力設定の Interact は「長押し」設定になっているため、
            // 押した瞬間に反応するようキー直接の判定も併用している
            if (pickAction.WasPressedThisFrame() || WasKeyPressed(pickKey))
            {
                TryStartPreview();
            }

            if (WasKeyPressed(removeKey))
            {
                TryRemoveAimedClone();
            }

            return;
        }

        // プレビュー中は、置こうとしているもの自体が目印になるので強調はしない
        ClearHighlight();
        UpdatePreviewPosition();

        if (placeAction.WasPressedThisFrame())
        {
            ConfirmPlacement();
        }
        else if (WasKeyPressed(cancelKey))
        {
            CancelPreview();
        }
    }

    // ------------------------------------------------------------
    // 狙っているものを強調する
    // ------------------------------------------------------------

    /// <summary>
    /// いま狙っているものの色を変えて、「これに操作できる」と分かるようにする。
    /// 複製できるものは水色、自分が置いたもの（Rで消せる）は橙色にする。
    /// </summary>
    private void UpdateHighlight()
    {
        GameObject target = null;
        Color color = highlightColor;

        if (TryAim(pickDistance, out RaycastHit hit))
        {
            if (hit.collider.CompareTag(DuplicableTag))
            {
                target = hit.collider.gameObject;
                color = highlightColor;
            }
            else if (hit.collider.CompareTag(PlacedCloneTag))
            {
                target = hit.collider.gameObject;
                color = removableColor;
            }
        }

        // 消える予告で点滅中のものは、そちらの表示を優先する
        if (target != null && target == warningObject)
        {
            target = null;
        }

        if (target == highlightedObject)
        {
            return;
        }

        ClearHighlight();

        if (target == null)
        {
            return;
        }

        highlightedObject = target;
        CaptureColors(target, highlightRenderers, highlightOriginalColors);
        Tint(highlightRenderers, color);

        if (reticle != null)
        {
            reticle.SetHighlight(color);
        }
    }

    /// <summary>強調をやめて、元の色に戻す。</summary>
    private void ClearHighlight()
    {
        // 強調していた相手が既に消えている場合もあるので、
        // 相手の有無ではなく「控えが残っているか」で判定する
        if (highlightedObject == null && highlightRenderers.Count == 0)
        {
            return;
        }

        RestoreColors(highlightRenderers, highlightOriginalColors);
        highlightedObject = null;

        if (reticle != null)
        {
            reticle.SetHighlight(null);
        }
    }

    // ------------------------------------------------------------
    // 1. 目の前のものを複製してプレビューに入る
    // ------------------------------------------------------------

    private void TryStartPreview()
    {
        if (!TryAim(pickDistance, out RaycastHit hit))
        {
            return;
        }

        if (!hit.collider.CompareTag(DuplicableTag))
        {
            return;
        }

        GameObject source = hit.collider.gameObject;

        // 強調中の色のまま複製すると、その色が複製にも移ってしまう。
        // 元の色に戻してから複製する
        ClearHighlight();

        previewObject = Instantiate(source, source.transform.position, source.transform.rotation);
        previewObject.name = source.name + "_Clone";

        SetPhysicsEnabled(previewObject, false);
        StartPreviewLook();
    }

    /// <summary>プレビュー中は色を変えて「まだ置いていない」ことを分かるようにする。</summary>
    private void StartPreviewLook()
    {
        CaptureColors(previewObject, previewRenderers, previewOriginalColors);
        Tint(previewRenderers, previewColor);
    }

    // ------------------------------------------------------------
    // 2. 置く場所を決める
    // ------------------------------------------------------------

    private void UpdatePreviewPosition()
    {
        if (!TryAim(placeDistance, out RaycastHit hit))
        {
            return;
        }

        // 当たった面の上に、めり込まないように乗せる
        Vector3 position = hit.point + hit.normal * placeOffset;

        // オブジェクトの底が面に接するように持ち上げる
        if (TryGetLocalBoundsHeight(previewObject, out float halfHeight))
        {
            position += hit.normal * halfHeight;
        }

        previewObject.transform.position = position;
    }

    private void ConfirmPlacement()
    {
        SetPhysicsEnabled(previewObject, true);
        RestoreOriginalColors();

        previewObject.tag = PlacedCloneTag;
        placedObjects.Enqueue(previewObject);

        previewObject = null;

        UpdateOldestWarning();
    }

    private void CancelPreview()
    {
        Destroy(previewObject);
        previewObject = null;
        previewRenderers.Clear();
    }

    /// <summary>プレビュー用に変えた色を元に戻す。</summary>
    private void RestoreOriginalColors()
    {
        RestoreColors(previewRenderers, previewOriginalColors);
    }

    // ------------------------------------------------------------
    // 3. 置ける数の制限と、古いものの自動削除
    // ------------------------------------------------------------

    /// <summary>
    /// 上限を超えていたら、一番古いものを点滅させてから消す。
    /// いきなり消えると「なぜ消えたか」が分からないため、予告してから消す。
    /// </summary>
    private void UpdateOldestWarning()
    {
        ClearWarning();

        if (placedObjects.Count <= maxPlacedObjects)
        {
            return;
        }

        warningObject = placedObjects.Peek();

        if (warningObject == null)
        {
            RemoveOldest();
            return;
        }

        warningTimer = warningSeconds;
        CaptureColors(warningObject, warningRenderers, warningOriginalColors);
    }

    private void UpdateWarningBlink()
    {
        if (warningObject == null)
        {
            return;
        }

        warningTimer -= Time.deltaTime;

        // 点滅させる。0.08秒ごとに色を入れ替える
        bool highlight = Mathf.FloorToInt(warningTimer / 0.08f) % 2 == 0;
        if (highlight)
        {
            Tint(warningRenderers, warningColor);
        }
        else
        {
            ApplyStoredColors(warningRenderers, warningOriginalColors);
        }

        if (warningTimer <= 0f)
        {
            RemoveOldest();
        }
    }

    private void RemoveOldest()
    {
        ClearWarning();

        if (placedObjects.Count == 0)
        {
            return;
        }

        GameObject oldest = placedObjects.Dequeue();
        if (oldest != null)
        {
            Destroy(oldest);
        }
    }

    private void ClearWarning()
    {
        RestoreColors(warningRenderers, warningOriginalColors);
        warningObject = null;
        warningTimer = 0f;
    }

    // ------------------------------------------------------------
    // 4. 手で消す
    // ------------------------------------------------------------

    private void TryRemoveAimedClone()
    {
        if (!TryAim(pickDistance, out RaycastHit hit))
        {
            return;
        }

        if (!hit.collider.CompareTag(PlacedCloneTag))
        {
            return;
        }

        GameObject target = hit.collider.gameObject;

        // 消す相手を強調している場合があるので、先に解除しておく
        if (highlightedObject == target)
        {
            ClearHighlight();
        }

        RemoveFromQueue(target);
        Destroy(target);
    }

    /// <summary>
    /// 順番待ちの列から、指定のものだけを抜く。
    /// Queue は途中から抜けないので、作り直している。
    /// </summary>
    private void RemoveFromQueue(GameObject target)
    {
        if (warningObject == target)
        {
            ClearWarning();
        }

        Queue<GameObject> rebuilt = new Queue<GameObject>();
        foreach (GameObject placed in placedObjects)
        {
            if (placed != null && placed != target)
            {
                rebuilt.Enqueue(placed);
            }
        }

        placedObjects.Clear();
        foreach (GameObject placed in rebuilt)
        {
            placedObjects.Enqueue(placed);
        }
    }

    // ------------------------------------------------------------
    // 共通の処理
    // ------------------------------------------------------------

    /// <summary>
    /// 画面の中央から狙いの線を飛ばす。
    /// 自分自身とプレビュー中のものは無視する（自分に当たって進まなくなるのを防ぐ）。
    /// </summary>
    private bool TryAim(float distance, out RaycastHit result)
    {
        Ray ray = new Ray(aimCamera.transform.position, aimCamera.transform.forward);
        int count = Physics.RaycastNonAlloc(ray, hitBuffer, distance, ~0, QueryTriggerInteraction.Ignore);

        bool found = false;
        result = default;
        float nearest = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hitBuffer[i];
            Transform hitTransform = hit.collider.transform;

            if (playerRoot != null && hitTransform.IsChildOf(playerRoot))
            {
                continue;
            }

            if (previewObject != null && hitTransform.IsChildOf(previewObject.transform))
            {
                continue;
            }

            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                result = hit;
                found = true;
            }
        }

        return found;
    }

    /// <summary>物理（落下と当たり判定）を入れたり切ったりする。</summary>
    private void SetPhysicsEnabled(GameObject target, bool isEnabled)
    {
        foreach (Rigidbody body in target.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = !isEnabled;

            if (isEnabled)
            {
                // 置いた瞬間に前の勢いが残らないようにする
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        foreach (Collider collider in target.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = isEnabled;
        }
    }

    /// <summary>オブジェクトの高さの半分を返す。床に乗せる位置を決めるのに使う。</summary>
    private bool TryGetLocalBoundsHeight(GameObject target, out float halfHeight)
    {
        halfHeight = 0f;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return false;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        halfHeight = bounds.extents.y;
        return true;
    }

    /// <summary>
    /// 対象の見た目をすべて集めて、いまの色を控える。
    /// 色を変える前に必ずこれを呼ぶこと。控えておかないと元の色に戻せなくなる。
    /// </summary>
    private static void CaptureColors(GameObject target, List<Renderer> renderers, List<Color> colors)
    {
        renderers.Clear();
        colors.Clear();

        if (target == null)
        {
            return;
        }

        target.GetComponentsInChildren(true, renderers);

        foreach (Renderer renderer in renderers)
        {
            colors.Add(renderer.sharedMaterial != null ? renderer.sharedMaterial.color : Color.white);
        }
    }

    /// <summary>控えておいた色に戻して、控えを空にする。</summary>
    private static void RestoreColors(List<Renderer> renderers, List<Color> colors)
    {
        ApplyStoredColors(renderers, colors);
        renderers.Clear();
        colors.Clear();
    }

    /// <summary>控えておいた色に戻す（控えは残す。点滅で往復させるため）。</summary>
    private static void ApplyStoredColors(List<Renderer> renderers, List<Color> colors)
    {
        for (int i = 0; i < renderers.Count && i < colors.Count; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].material.color = colors[i];
            }
        }
    }

    /// <summary>まとめて色を塗る。</summary>
    private static void Tint(List<Renderer> renderers, Color color)
    {
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
            {
                // material を触ると、そのオブジェクト専用のマテリアルが作られる。
                // 元のマテリアル（他のオブジェクトと共用）は変わらない
                renderer.material.color = color;
            }
        }
    }

    private static bool WasKeyPressed(Key key)
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[key].wasPressedThisFrame;
    }
}

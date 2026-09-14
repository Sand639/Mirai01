using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **重い重力のときに踏むと割れる、ガラスの床。**
///
/// | 段階 | 何が起きるか |
/// | --- | --- |
/// | ふつう | 乗っても何も起きない |
/// | **ヒビ** | **重力が決めた値以上のときに踏むと、ヒビが入る。** ヒビは広がっていき、ふちが赤く点滅する |
/// | **割れる** | ヒビが入ってから決めた秒数で割れる。**破片に当たり判定は無い**ので、乗っていた人は落ちる |
///
/// **一度ヒビが入ったら止まらない。** 途中で重力が軽くなっても割れる。
///
/// ## 使い方
///
/// **ガラスのマテリアルを付けた Cube に、これを付けるだけ。**
/// 大きさは Cube の Scale で決める（ヒビも破片も、大きさに合わせて作られる）。
/// メニューの `Tools > Mirai01 > 重力で割れるガラスの床を置く` でも置ける。
///
/// ## 「踏んだ」の見分け方
///
/// **床の上面のすぐ上に、人（CharacterController）がいるか**を毎フレーム調べている。
/// 当たった瞬間を拾う方式（OnCollision）は、CharacterController では呼ばれないため使っていない。
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class GravityGlassFloor : MonoBehaviour
{
    /// <summary>床の状態。</summary>
    public enum FloorState
    {
        /// <summary>割れていない</summary>
        Intact,

        /// <summary>ヒビが入った（もうすぐ割れる）</summary>
        Cracked,

        /// <summary>割れた</summary>
        Broken,
    }

    [Header("割れる条件")]
    [Tooltip("**この重力以上のときに踏むと、ヒビが入る。** 正の数で入れる。地球は9.81")]
    [Min(0f)]
    [SerializeField] private float breakGravity = 12f;

    [Tooltip("**ヒビが入ってから割れるまでの秒数**")]
    [Min(0f)]
    [SerializeField] private float secondsUntilBreak = 1.5f;

    [Tooltip("ONなら**人（CharacterController）だけ**を数える。OFFにすると、置いた箱などでも割れる")]
    [SerializeField] private bool onlyCharacters = true;

    [Tooltip("上面からどれだけ上までを「乗っている」とみなすか（メートル）")]
    [Min(0.05f)]
    [SerializeField] private float detectHeight = 0.3f;

    [Header("ヒビの見た目")]
    [Tooltip("ヒビの線に使うマテリアル。**白い不透明なもの**が見えやすい")]
    [SerializeField] private Material crackMaterial;

    [Tooltip("ヒビの線の本数（踏んだ場所から放射状に伸びる）")]
    [Range(3, 16)]
    [SerializeField] private int crackLines = 7;

    [Tooltip("ヒビの線の太さ（メートル）")]
    [Min(0.005f)]
    [SerializeField] private float crackWidth = 0.035f;

    [Tooltip("ヒビが入ったときの、ふちの色。**割れる直前ほど速く点滅する**")]
    [SerializeField] private Color crackEdgeColor = new Color(1f, 0.35f, 0.25f, 1f);

    [Header("割れたときの見た目")]
    [Tooltip("破片の数（縦×横）。3なら 3×3＝9枚")]
    [Range(1, 6)]
    [SerializeField] private int piecesPerSide = 3;

    [Tooltip("破片が消えるまでの秒数")]
    [Min(0.1f)]
    [SerializeField] private float pieceLifeSeconds = 3f;

    [Tooltip("破片が散らばる勢い")]
    [Min(0f)]
    [SerializeField] private float scatterSpeed = 1.2f;

    [Header("元に戻る")]
    [Tooltip("**割れてから元に戻るまでの秒数。** 0なら戻らない（やり直しのときだけ戻る）")]
    [Min(0f)]
    [SerializeField] private float respawnSeconds;

    [Tooltip("割れるたびにコンソールへ書き出す")]
    [SerializeField] private bool logChanges = true;

    /// <summary>いまの状態。</summary>
    public FloorState State { get; private set; } = FloorState.Intact;

    /// <summary>割れるまでの残り秒数（ヒビが入っている間だけ意味がある）。</summary>
    public float BreakRemaining { get; private set; }

    private static readonly Collider[] Hits = new Collider[16];
    private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");

    private BoxCollider box;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock propertyBlock;
    private Color originalEdgeColor = Color.white;
    private bool hasEdgeColor;

    private Transform crackRoot;
    private readonly List<GameObject> crackSegments = new List<GameObject>();
    private float respawnTimer;

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        meshRenderer = GetComponent<MeshRenderer>();
        propertyBlock = new MaterialPropertyBlock();

        if (meshRenderer != null && meshRenderer.sharedMaterial != null &&
            meshRenderer.sharedMaterial.HasProperty(EdgeColorId))
        {
            hasEdgeColor = true;
            originalEdgeColor = meshRenderer.sharedMaterial.GetColor(EdgeColorId);
        }
    }

    private void OnEnable()
    {
        StageReset.Requested += Restore;
    }

    private void OnDisable()
    {
        StageReset.Requested -= Restore;
    }

    private void Update()
    {
        if (GamePause.IsPaused)
        {
            return;
        }

        switch (State)
        {
            case FloorState.Intact:
                CheckStep();
                break;

            case FloorState.Cracked:
                UpdateCracked();
                break;

            case FloorState.Broken:
                UpdateBroken();
                break;
        }
    }

    // ------------------------------------------------------------
    // 踏まれたか
    // ------------------------------------------------------------

    private void CheckStep()
    {
        // 重力が軽いうちは、乗っても平気
        if (Mathf.Abs(WorldGravity.Value) < breakGravity)
        {
            return;
        }

        if (TryFindStepper(out Vector3 footPosition))
        {
            Crack(footPosition);
        }
    }

    /// <summary>
    /// **上面のすぐ上に、誰か乗っているか。**
    /// 乗っていれば、その足もとの位置を返す（ヒビをそこから広げるため）。
    /// </summary>
    private bool TryFindStepper(out Vector3 footPosition)
    {
        footPosition = transform.position;

        Vector3 size = Vector3.Scale(box.size, transform.lossyScale);
        Vector3 up = transform.up;

        // 上面の少し内側から、上へ detectHeight ぶんの薄い箱
        Vector3 top = transform.TransformPoint(box.center) + up * (size.y * 0.5f);
        Vector3 center = top + up * (detectHeight * 0.5f);
        Vector3 halfExtents = new Vector3(size.x * 0.48f, detectHeight * 0.5f, size.z * 0.48f);

        int count = Physics.OverlapBoxNonAlloc(
            center, halfExtents, Hits, transform.rotation, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider hit = Hits[i];

            if (hit == null || hit == box || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            if (onlyCharacters && !(hit is CharacterController))
            {
                continue;
            }

            footPosition = hit.bounds.center;
            return true;
        }

        return false;
    }

    // ------------------------------------------------------------
    // ヒビ
    // ------------------------------------------------------------

    /// <summary>
    /// **ヒビを入れる。** 外から割れかけにしたいときにも呼べる。
    /// <paramref name="from"/> は、ヒビが広がり始める場所（だいたいでよい）。
    /// </summary>
    public void Crack(Vector3 from)
    {
        if (State != FloorState.Intact)
        {
            return;
        }

        State = FloorState.Cracked;
        BreakRemaining = secondsUntilBreak;

        BuildCracks(from);

        if (logChanges)
        {
            Debug.Log($"[GLASS] {name}：ヒビが入った（重力 {Mathf.Abs(WorldGravity.Value):0.0}）。" +
                      $"{secondsUntilBreak:0.0}秒後に割れる", this);
        }

        if (secondsUntilBreak <= 0f)
        {
            Break();
        }
    }

    private void UpdateCracked()
    {
        BreakRemaining -= Time.deltaTime;

        float progress = secondsUntilBreak > 0f ? 1f - BreakRemaining / secondsUntilBreak : 1f;

        RevealCracks(progress);
        BlinkEdge(progress);

        if (BreakRemaining <= 0f)
        {
            Break();
        }
    }

    /// <summary>
    /// ヒビの線を作る。**踏んだ場所から放射状に、ギザギザに**伸ばす。
    ///
    /// 線は最初は隠しておき、割れる時間に近づくほど**踏んだ場所に近いほうから順に見せていく**
    /// （ヒビが広がっていくように見える）。
    /// </summary>
    private void BuildCracks(Vector3 from)
    {
        ClearCracks();

        if (crackMaterial == null)
        {
            Debug.LogWarning($"{name}: ヒビのマテリアルが入っていません。ヒビは見えませんが、割れる動きはします。", this);
            return;
        }

        // Cube の Scale がゆがんでいても線が斜めにゆがまないよう、
        // 入れ物の大きさを打ち消して「1倍」に戻しておく
        Vector3 scale = transform.lossyScale;

        crackRoot = new GameObject("Cracks").transform;
        crackRoot.SetParent(transform, false);
        crackRoot.localScale = new Vector3(
            SafeInverse(scale.x),
            SafeInverse(scale.y),
            SafeInverse(scale.z));

        Vector3 size = Vector3.Scale(box.size, scale);
        float halfX = size.x * 0.5f;
        float halfZ = size.z * 0.5f;
        float topY = box.center.y * scale.y + size.y * 0.5f + 0.004f;

        // 踏んだ場所を、上面の上の位置に直す（ふちに寄りすぎないように少し内側へ）
        Vector3 local = transform.InverseTransformPoint(from);
        Vector2 origin = new Vector2(
            Mathf.Clamp(local.x * scale.x, -halfX * 0.7f, halfX * 0.7f),
            Mathf.Clamp(local.z * scale.z, -halfZ * 0.7f, halfZ * 0.7f));

        float reach = Mathf.Max(halfX, halfZ) * 1.5f;
        float startAngle = Random.Range(0f, 360f);

        for (int line = 0; line < crackLines; line++)
        {
            float angle = (startAngle + 360f * line / crackLines + Random.Range(-15f, 15f)) * Mathf.Deg2Rad;
            Vector2 point = origin;

            int segments = Random.Range(3, 5);
            float step = reach / segments;

            for (int s = 0; s < segments; s++)
            {
                // 少しずつ向きをずらして、ギザギザにする
                float bend = angle + Random.Range(-0.45f, 0.45f);
                Vector2 next = point + new Vector2(Mathf.Cos(bend), Mathf.Sin(bend)) * step * Random.Range(0.7f, 1.1f);

                // 床の外にはみ出したら、ふちで止める
                bool outside = Mathf.Abs(next.x) > halfX || Mathf.Abs(next.y) > halfZ;
                next.x = Mathf.Clamp(next.x, -halfX, halfX);
                next.y = Mathf.Clamp(next.y, -halfZ, halfZ);

                CreateSegment(point, next, topY, s);

                point = next;

                if (outside)
                {
                    break;
                }
            }
        }

        // 踏んだ場所に近い順に並べる（名前の先頭が「何本目の区切りか」）。見せるときにこの順で出す
        crackSegments.Sort((a, b) => a.name.CompareTo(b.name));

        RevealCracks(0f);
    }

    private void CreateSegment(Vector2 from, Vector2 to, float y, int depth)
    {
        Vector2 delta = to - from;
        float length = delta.magnitude;

        if (length < 0.01f)
        {
            return;
        }

        GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);

        // 名前の先頭を「どれだけ奥か」にしておき、並べ替えに使う
        segment.name = $"{depth}_Crack";

        RemoveCollider(segment);

        segment.transform.SetParent(crackRoot, false);
        segment.transform.localPosition = new Vector3((from.x + to.x) * 0.5f, y, (from.y + to.y) * 0.5f);
        segment.transform.localRotation = Quaternion.LookRotation(new Vector3(delta.x, 0f, delta.y));
        segment.transform.localScale = new Vector3(crackWidth, 0.008f, length + crackWidth);

        MeshRenderer segmentRenderer = segment.GetComponent<MeshRenderer>();
        segmentRenderer.sharedMaterial = crackMaterial;
        segmentRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        crackSegments.Add(segment);
    }

    /// <summary>ヒビを、進み具合に合わせて見せていく。最初から4割は見えている。</summary>
    private void RevealCracks(float progress)
    {
        int visible = Mathf.CeilToInt(crackSegments.Count * Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(progress * 1.4f)));

        for (int i = 0; i < crackSegments.Count; i++)
        {
            bool show = i < visible;

            if (crackSegments[i].activeSelf != show)
            {
                crackSegments[i].SetActive(show);
            }
        }
    }

    /// <summary>ふちを赤く点滅させる。**割れる直前ほど速く**する。</summary>
    private void BlinkEdge(float progress)
    {
        if (!hasEdgeColor)
        {
            return;
        }

        float speed = Mathf.Lerp(4f, 18f, progress);
        float blink = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f;

        SetEdgeColor(Color.Lerp(originalEdgeColor, crackEdgeColor, Mathf.Lerp(0.4f, 1f, blink)));
    }

    private void SetEdgeColor(Color color)
    {
        meshRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(EdgeColorId, color);
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    private void ClearCracks()
    {
        crackSegments.Clear();

        if (crackRoot != null)
        {
            Destroy(crackRoot.gameObject);
            crackRoot = null;
        }
    }

    // ------------------------------------------------------------
    // 割れる・戻る
    // ------------------------------------------------------------

    /// <summary>**割る。** 外からすぐ割りたいときにも呼べる。</summary>
    public void Break()
    {
        if (State == FloorState.Broken)
        {
            return;
        }

        State = FloorState.Broken;
        respawnTimer = respawnSeconds;

        ClearCracks();
        SpawnPieces();

        // **当たり判定を消す。** 乗っていた人はそのまま落ちる
        box.enabled = false;

        if (meshRenderer != null)
        {
            meshRenderer.enabled = false;
        }

        if (logChanges)
        {
            Debug.Log($"[GLASS] {name}：割れた", this);
        }
    }

    private void UpdateBroken()
    {
        if (respawnSeconds <= 0f)
        {
            return;
        }

        respawnTimer -= Time.deltaTime;

        if (respawnTimer <= 0f)
        {
            Restore();
        }
    }

    /// <summary>
    /// **元の割れていない床に戻す。**
    /// やり直しの合図（<see cref="StageReset"/>）でも呼ばれる。
    /// </summary>
    public void Restore()
    {
        ClearCracks();

        State = FloorState.Intact;
        BreakRemaining = 0f;

        box.enabled = true;

        if (meshRenderer != null)
        {
            meshRenderer.enabled = true;

            // 点滅で変えた色を消して、マテリアルそのままの見た目に戻す
            meshRenderer.SetPropertyBlock(null);
        }
    }

    /// <summary>
    /// 割れた破片を出す。**当たり判定は付けない**ので、床も人も素通りして落ちていく。
    /// 落ちる速さはいまの重力に従う（重いほど速く落ちる）。
    /// </summary>
    private void SpawnPieces()
    {
        if (meshRenderer == null)
        {
            return;
        }

        Material material = meshRenderer.sharedMaterial;
        Vector3 size = Vector3.Scale(box.size, transform.lossyScale);
        int count = Mathf.Max(1, piecesPerSide);

        Vector3 pieceSize = new Vector3(size.x / count * 0.92f, size.y, size.z / count * 0.92f);

        for (int x = 0; x < count; x++)
        {
            for (int z = 0; z < count; z++)
            {
                // 床を count×count のマス目に分けたときの、マスの中心
                Vector3 offset = new Vector3(
                    ((x + 0.5f) / count - 0.5f) * size.x,
                    0f,
                    ((z + 0.5f) / count - 0.5f) * size.z);

                GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                piece.name = name + "_Piece";

                RemoveCollider(piece);

                piece.transform.SetPositionAndRotation(
                    transform.TransformPoint(box.center) + transform.rotation * offset,
                    transform.rotation);
                piece.transform.localScale = pieceSize;

                MeshRenderer pieceRenderer = piece.GetComponent<MeshRenderer>();
                pieceRenderer.sharedMaterial = material;
                pieceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                Rigidbody body = piece.AddComponent<Rigidbody>();
                body.mass = 0.2f;

                // 真ん中から外へ、少しだけ散らばる
                Vector3 outward = offset.sqrMagnitude > 0.0001f ? offset.normalized : Random.onUnitSphere;
                body.linearVelocity = (outward + Random.insideUnitSphere * 0.5f) * scatterSpeed;
                body.angularVelocity = Random.insideUnitSphere * 4f;

                Destroy(piece, pieceLifeSeconds);
            }
        }
    }

    /// <summary>
    /// 当たり判定を外す。**消えるのはフレームの終わり**なので、先に切っておく
    /// （切らないと、そのフレームだけ人や他の破片を押してしまう）。
    /// </summary>
    private static void RemoveCollider(GameObject target)
    {
        Collider collider = target.GetComponent<Collider>();

        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }
    }

    private static float SafeInverse(float value)
    {
        return Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;
    }

    private void OnDrawGizmosSelected()
    {
        BoxCollider collider = GetComponent<BoxCollider>();

        if (collider == null)
        {
            return;
        }

        // 「乗っている」とみなす範囲を、水色の箱で示す
        Vector3 size = Vector3.Scale(collider.size, transform.lossyScale);

        Gizmos.matrix = Matrix4x4.TRS(
            transform.TransformPoint(collider.center) + transform.up * (size.y * 0.5f + detectHeight * 0.5f),
            transform.rotation, Vector3.one);

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x * 0.96f, detectHeight, size.z * 0.96f));
        Gizmos.matrix = Matrix4x4.identity;
    }
}

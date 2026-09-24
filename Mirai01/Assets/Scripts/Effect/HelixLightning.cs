using UnityEngine;

/// <summary>
/// **体のまわりを、らせん状にびりびり走る電撃**（フルカウル風）。
///
/// しくみ（1本の電撃ができるまで）：
/// 1. 体のまわりに、らせん状の「目印の点」を数個置く（半径・回る向き・回る量・高さは**毎回ランダム**）
/// 2. 目印の点のあいだを**エルミート曲線**でなめらかにつなぐ（これが電撃の「芯」になる）
/// 3. 芯の各点を、進む向きと直角の方向へランダムにずらして**ギザギザ**にする
/// 4. 短い間隔（0.03〜0.08秒くらい）で 1〜3 を**作り直す**。これで毎回形が変わり、ちらついて見える
/// 5. ときどき芯から**枝分かれ**を生やす
///
/// 見た目は「太くて薄い光（Glow）」と「細くて明るい芯（Core）」の2本の線を重ねて作る。
/// 明るさ（HDRの色）を 1 より大きくしているので、カメラのブルーム（光のにじみ）で光って見える。
///
/// このコンポーネントを付けたオブジェクトの**上方向を軸に**らせんを描き、一緒に動く。
/// </summary>
public class HelixLightning : MonoBehaviour
{
    [Header("見た目")]
    [Tooltip("加算合成（光を足す）のマテリアル。空なら実行時に作る")]
    [SerializeField] private Material lineMaterial;

    [ColorUsage(true, true)]
    [SerializeField] private Color glowColor = new Color(0.15f, 3f, 0.45f, 1f);

    [ColorUsage(true, true)]
    [SerializeField] private Color coreColor = new Color(0.9f, 4f, 1.2f, 1f);

    [SerializeField] private float glowWidth = 0.16f;
    [SerializeField] private float coreWidth = 0.025f;

    [Header("本数")]
    [Tooltip("同時に出ている電撃の本数（ふだん）")]
    [SerializeField] private int boltCount = 5;

    [Tooltip("Burst（攻撃時など）で一時的に増やす本数")]
    [SerializeField] private int burstExtraBolts = 5;

    [Header("らせんの形")]
    [Tooltip("らせんの中心（足元からの高さ）")]
    [SerializeField] private float centerHeight = 1f;

    [Tooltip("らせんが走る範囲の高さ（体の高さ）")]
    [SerializeField] private float bodyHeight = 1.8f;

    [SerializeField] private float radius = 0.45f;

    [Tooltip("1本が何周回るか（最小・最大）")]
    [SerializeField] private Vector2 turnsRange = new Vector2(0.4f, 1.3f);

    [Tooltip("1本の長さ（体の高さに対する割合。最小・最大）")]
    [SerializeField] private Vector2 lengthRange = new Vector2(0.35f, 0.8f);

    [Tooltip("目印の点の数。多いほど曲がりくねる")]
    [SerializeField] private int controlPointCount = 6;

    [Tooltip("目印の点1区間を何分割するか。多いほど細かいギザギザになる")]
    [SerializeField] private int samplesPerSegment = 5;

    [Header("びりびり")]
    [Tooltip("ギザギザの大きさ（メートル）")]
    [SerializeField] private float jitter = 0.05f;

    [Tooltip("作り直す間隔（秒。最小・最大）。短いほど激しくちらつく")]
    [SerializeField] private Vector2 regenerateInterval = new Vector2(0.03f, 0.08f);

    [Tooltip("作り直したときに見えている確率。1 未満にすると、ときどき消えて点滅する")]
    [Range(0f, 1f)]
    [SerializeField] private float visibleChance = 0.8f;

    [Header("枝分かれ")]
    [Range(0f, 1f)]
    [SerializeField] private float branchChance = 0.5f;

    [SerializeField] private Vector2 branchLengthRange = new Vector2(0.15f, 0.4f);

    [Header("光")]
    [Tooltip("まわりを照らすライト（空なら照らさない）")]
    [SerializeField] private Light flickerLight;

    [SerializeField] private float lightIntensity = 3f;

    [Header("Burst（一時的に激しくする）")]
    [SerializeField] private float burstRadiusScale = 1.5f;
    [SerializeField] private float burstJitterScale = 1.8f;
    [SerializeField] private float burstBrightnessScale = 1.6f;

    private const int BranchPointCount = 5;

    private Bolt[] bolts;
    private float burstTimer;
    private float burstDuration;

    /// <summary>電撃を出すかどうか。false にすると全部消える。</summary>
    public bool IsOn { get; set; } = true;

    /// <summary>
    /// **一定時間だけ電撃を激しくする**（本数・太さ・広がりが増える）。攻撃やダッシュの瞬間に呼ぶ。
    /// </summary>
    public void Burst(float seconds = 0.4f)
    {
        burstTimer = seconds;
        burstDuration = seconds;
    }

    // ------------------------------------------------------------
    // Unityから呼ばれる
    // ------------------------------------------------------------

    private void Awake()
    {
        if (lineMaterial == null)
        {
            lineMaterial = CreateAdditiveMaterial();
        }

        int total = boltCount + burstExtraBolts;
        bolts = new Bolt[total];

        for (int i = 0; i < total; i++)
        {
            bolts[i] = new Bolt(this, i);
        }
    }

    private void Update()
    {
        if (burstTimer > 0f)
        {
            burstTimer -= Time.deltaTime;
        }

        // Burst の強さ（1 → 0 へ減っていく）
        float burst = burstDuration > 0f ? Mathf.Clamp01(burstTimer / burstDuration) : 0f;
        int activeCount = boltCount + Mathf.CeilToInt(burstExtraBolts * burst);

        for (int i = 0; i < bolts.Length; i++)
        {
            bool active = IsOn && i < activeCount;
            bolts[i].Tick(Time.deltaTime, active, burst);
        }

        if (flickerLight != null)
        {
            flickerLight.enabled = IsOn;
            // 光もちらつかせる
            flickerLight.intensity = lightIntensity * Random.Range(0.5f, 1f) * (1f + burst);
        }
    }

    // ------------------------------------------------------------
    // 1本の電撃
    // ------------------------------------------------------------

    private class Bolt
    {
        private readonly HelixLightning owner;
        private readonly LinePair main;
        private readonly LinePair branch;

        private readonly Vector3[] controlPoints;
        private readonly Vector3[] points;
        private readonly Vector3[] branchPoints = new Vector3[BranchPointCount];

        private float timer;

        public Bolt(HelixLightning owner, int index)
        {
            this.owner = owner;

            controlPoints = new Vector3[Mathf.Max(3, owner.controlPointCount)];
            points = new Vector3[(controlPoints.Length - 1) * Mathf.Max(1, owner.samplesPerSegment) + 1];

            main = new LinePair(owner, $"Bolt_{index}");
            branch = new LinePair(owner, $"Bolt_{index}_Branch");
        }

        public void Tick(float deltaTime, bool active, float burst)
        {
            if (!active)
            {
                main.SetVisible(false);
                branch.SetVisible(false);
                timer = 0f;
                return;
            }

            timer -= deltaTime;

            if (timer > 0f)
            {
                return;
            }

            // 次に作り直すまでの時間もランダム（全部が同時に切り替わると機械的に見えるため）
            timer = Random.Range(owner.regenerateInterval.x, owner.regenerateInterval.y);

            // Burst 中は必ず見せる。ふだんはときどき消えて点滅する
            bool visible = Random.value < Mathf.Lerp(owner.visibleChance, 1f, burst);

            if (!visible)
            {
                main.SetVisible(false);
                branch.SetVisible(false);
                return;
            }

            float radiusScale = Mathf.Lerp(1f, owner.burstRadiusScale, burst);
            float jitterScale = Mathf.Lerp(1f, owner.burstJitterScale, burst);
            float brightness = Mathf.Lerp(1f, owner.burstBrightnessScale, burst);

            BuildHelix(radiusScale);
            int count = BuildJaggedLine(owner.jitter * jitterScale);

            main.Set(points, count, brightness, jitterScale);

            if (Random.value < owner.branchChance)
            {
                BuildBranch(count, owner.jitter * jitterScale);
                branch.Set(branchPoints, BranchPointCount, brightness * 0.8f, 0.7f);
            }
            else
            {
                branch.SetVisible(false);
            }
        }

        /// <summary>手順1：らせん状に目印の点を置く。</summary>
        private void BuildHelix(float radiusScale)
        {
            float length = owner.bodyHeight * Random.Range(owner.lengthRange.x, owner.lengthRange.y);
            float bottom = owner.centerHeight - owner.bodyHeight * 0.5f;
            float startY = bottom + Random.Range(0f, owner.bodyHeight - length);

            float turns = Random.Range(owner.turnsRange.x, owner.turnsRange.y);
            float direction = Random.value < 0.5f ? 1f : -1f;
            float phase = Random.Range(0f, Mathf.PI * 2f);

            // 上から下へ走るものも混ぜる
            bool downward = Random.value < 0.5f;

            for (int i = 0; i < controlPoints.Length; i++)
            {
                float t = i / (float)(controlPoints.Length - 1);
                float angle = phase + direction * t * turns * Mathf.PI * 2f;

                // 半径を点ごとに少しゆらす（きれいな円柱に沿いすぎると機械的に見えるため）
                float r = owner.radius * radiusScale * Random.Range(0.8f, 1.25f);
                float y = startY + (downward ? 1f - t : t) * length;

                controlPoints[i] = new Vector3(Mathf.Cos(angle) * r, y, Mathf.Sin(angle) * r);
            }
        }

        /// <summary>手順2・3：エルミート曲線でつなぎ、直角方向にずらしてギザギザにする。点の数を返す。</summary>
        private int BuildJaggedLine(float jitterAmount)
        {
            int segments = controlPoints.Length - 1;
            int samples = Mathf.Max(1, owner.samplesPerSegment);
            int index = 0;

            for (int s = 0; s < segments; s++)
            {
                Vector3 p0 = controlPoints[s];
                Vector3 p1 = controlPoints[s + 1];
                Vector3 m0 = Tangent(s);
                Vector3 m1 = Tangent(s + 1);

                // 最後の区間だけ終点も含める
                int count = s == segments - 1 ? samples + 1 : samples;

                for (int k = 0; k < count; k++)
                {
                    points[index++] = Hermite(p0, p1, m0, m1, k / (float)samples);
                }
            }

            // 端はずらさない。真ん中ほど大きくずらす（両端が細く消えていくように見える）
            for (int i = 1; i < index - 1; i++)
            {
                float t = i / (float)(index - 1);
                float envelope = Mathf.Sqrt(Mathf.Sin(t * Mathf.PI));

                Vector3 forward = (points[i + 1] - points[i - 1]).normalized;
                Vector3 offset = Vector3.ProjectOnPlane(Random.insideUnitSphere, forward);

                points[i] += offset * (jitterAmount * 2f * envelope);
            }

            return index;
        }

        /// <summary>手順5：芯のどこかから、外向きに短い枝を生やす。</summary>
        private void BuildBranch(int mainCount, float jitterAmount)
        {
            int from = Random.Range(mainCount / 4, mainCount * 3 / 4);
            Vector3 start = points[from];

            // 体の外側へ向かう方向 ＋ 少しランダム
            Vector3 outward = new Vector3(start.x, 0f, start.z).normalized;
            Vector3 direction = (outward + Random.insideUnitSphere * 0.8f).normalized;
            float length = Random.Range(owner.branchLengthRange.x, owner.branchLengthRange.y);

            for (int i = 0; i < BranchPointCount; i++)
            {
                float t = i / (float)(BranchPointCount - 1);
                Vector3 p = start + direction * (length * t);

                if (i > 0)
                {
                    p += Random.insideUnitSphere * (jitterAmount * t);
                }

                branchPoints[i] = p;
            }
        }

        /// <summary>
        /// 目印の点での曲線の向き（接線）。前後の点から求める（Catmull-Rom と同じ求め方）。
        /// 両端は、となりの点との差をそのまま使う。
        /// </summary>
        private Vector3 Tangent(int i)
        {
            if (i == 0)
            {
                return controlPoints[1] - controlPoints[0];
            }

            if (i == controlPoints.Length - 1)
            {
                return controlPoints[i] - controlPoints[i - 1];
            }

            return (controlPoints[i + 1] - controlPoints[i - 1]) * 0.5f;
        }
    }

    /// <summary>
    /// エルミート曲線。p0 から p1 へ、それぞれの点で向き m0・m1 を持つなめらかな曲線上の点を返す（s は 0〜1）。
    /// </summary>
    public static Vector3 Hermite(Vector3 p0, Vector3 p1, Vector3 m0, Vector3 m1, float s)
    {
        float s2 = s * s;
        float s3 = s2 * s;

        return (2f * s3 - 3f * s2 + 1f) * p0
             + (s3 - 2f * s2 + s) * m0
             + (-2f * s3 + 3f * s2) * p1
             + (s3 - s2) * m1;
    }

    // ------------------------------------------------------------
    // 線の見た目（太い光 ＋ 細い芯 の2本セット）
    // ------------------------------------------------------------

    private class LinePair
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly HelixLightning owner;
        private readonly LineRenderer glow;
        private readonly LineRenderer core;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        public LinePair(HelixLightning owner, string name)
        {
            this.owner = owner;
            glow = CreateLine(owner, name + "_Glow");
            core = CreateLine(owner, name + "_Core");
        }

        public void Set(Vector3[] positions, int count, float brightness, float widthScale)
        {
            SetLine(glow, positions, count, owner.glowColor * brightness, owner.glowWidth * widthScale * Random.Range(0.7f, 1.2f));
            SetLine(core, positions, count, owner.coreColor * brightness, owner.coreWidth * Random.Range(0.8f, 1.3f));
        }

        public void SetVisible(bool visible)
        {
            glow.enabled = visible;
            core.enabled = visible;
        }

        private void SetLine(LineRenderer line, Vector3[] positions, int count, Color color, float width)
        {
            line.positionCount = count;
            line.SetPositions(positions);
            line.widthMultiplier = width;

            block.SetColor(BaseColorId, color);
            line.SetPropertyBlock(block);

            line.enabled = true;
        }

        private static LineRenderer CreateLine(HelixLightning owner, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(owner.transform, false);

            var line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = owner.lineMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
    }

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
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.SetColor("_BaseColor", Color.white);
    }
}

using UnityEngine;

/// <summary>
/// **まっすぐ飛ぶビームと、そのまわりにらせん状に巻きつく電撃。**
///
/// 使い方：ボタンを押した瞬間に <see cref="BeginCharge"/>、離した瞬間に <see cref="Release"/> を呼ぶ。
/// **押している時間が長いほど、ビームが遠くまで届く**（<see cref="minLength"/>〜<see cref="maxLength"/>）。
///
/// 流れ：
/// 1. **溜め**：押している間、届く距離の目安（うすい線）がのびていく
/// 2. **発射**：離すと、ビームの先端が前へ飛んでいく
/// 3. **維持**：届いたら少しのあいだ出たまま
/// 4. **消える**：だんだん細くなって消える
///
/// ビームのまわりの電撃は、体のまわりの電撃（<see cref="HelixLightning"/>）と同じ作り方
/// （らせん状の目印の点 → エルミート曲線 → ギザギザ → 短い間隔で作り直す）で、
/// らせんの軸が「上方向」ではなく「ビームの向き」になっている。
///
/// このコンポーネントを付けたオブジェクトの**前方向（青い矢印・Z）へ**飛ぶ。
/// </summary>
public class HelixBeam : MonoBehaviour
{
    [Header("見た目")]
    [Tooltip("加算合成（光を足す）のマテリアル。空なら実行時に作る")]
    [SerializeField] private Material lineMaterial;

    [ColorUsage(true, true)]
    [SerializeField] private Color beamGlowColor = new Color(0.2f, 2.5f, 0.6f, 1f);

    [ColorUsage(true, true)]
    [SerializeField] private Color beamCoreColor = new Color(2f, 5f, 2.5f, 1f);

    [SerializeField] private float beamGlowWidth = 0.45f;
    [SerializeField] private float beamCoreWidth = 0.12f;

    [ColorUsage(true, true)]
    [SerializeField] private Color boltGlowColor = new Color(0.15f, 3f, 0.45f, 1f);

    [ColorUsage(true, true)]
    [SerializeField] private Color boltCoreColor = new Color(0.9f, 4f, 1.2f, 1f);

    [SerializeField] private float boltGlowWidth = 0.14f;
    [SerializeField] private float boltCoreWidth = 0.025f;

    [Header("溜め（長押しの時間で距離が決まる）")]
    [Tooltip("ちょっと押しただけのときの距離（メートル）")]
    [SerializeField] private float minLength = 3f;

    [Tooltip("いちばん溜めたときの距離（メートル）")]
    [SerializeField] private float maxLength = 15f;

    [Tooltip("いちばん遠くまで届くのに必要な長押しの時間（秒）")]
    [SerializeField] private float maxChargeSeconds = 1.5f;

    [Header("発射")]
    [Tooltip("ビームの先端が進む速さ（メートル／秒）")]
    [SerializeField] private float beamSpeed = 45f;

    [Tooltip("届いてから消え始めるまでの時間（秒）")]
    [SerializeField] private float holdSeconds = 0.35f;

    [Tooltip("消えるまでにかかる時間（秒）")]
    [SerializeField] private float fadeSeconds = 0.25f;

    [Header("まわりのらせん")]
    [SerializeField] private int boltCount = 9;

    [Tooltip("らせんの半径（ビームの中心から）")]
    [SerializeField] private float radius = 0.45f;

    [Tooltip("1本の電撃の長さ（メートル。最小・最大）")]
    [SerializeField] private Vector2 boltLengthRange = new Vector2(2f, 5f);

    [Tooltip("1メートルあたり何周回るか（最小・最大）")]
    [SerializeField] private Vector2 turnsPerMeter = new Vector2(0.5f, 1.1f);

    [Tooltip("ギザギザの大きさ（メートル）")]
    [SerializeField] private float jitter = 0.06f;

    [Tooltip("作り直す間隔（秒。最小・最大）")]
    [SerializeField] private Vector2 regenerateInterval = new Vector2(0.03f, 0.07f);

    [Range(0f, 1f)]
    [SerializeField] private float visibleChance = 0.85f;

    [Header("光")]
    [Tooltip("ビームの先端についていくライト（空なら照らさない）")]
    [SerializeField] private Light headLight;

    [SerializeField] private float lightIntensity = 6f;

    private enum State
    {
        Idle,
        Charging,
        Firing,
        Holding,
        Fading,
    }

    /// <summary>ビームの芯の点の数（まっすぐだが、少しだけ震わせる）。</summary>
    private const int BeamPointCount = 16;

    /// <summary>らせん1本の目印の点の最大数（長い電撃ほど多く使う）。</summary>
    private const int MaxControlPoints = 16;

    private const int SamplesPerSegment = 4;

    private State state = State.Idle;
    private float chargeTime;
    private float targetLength;
    private float headDistance;
    private float stateTimer;

    private LightningLine beam;
    private LightningLine preview;
    private BeamBolt[] bolts;
    private readonly Vector3[] beamPoints = new Vector3[BeamPointCount];

    /// <summary>溜め具合（0〜1）。溜めていないときは 0。</summary>
    public float Charge01 => state == State.Charging ? Mathf.Clamp01(chargeTime / maxChargeSeconds) : 0f;

    /// <summary>溜め中か。</summary>
    public bool IsCharging => state == State.Charging;

    /// <summary>ビームが出ている（発射〜消えるまで）か。この間は次の溜めを始められない。</summary>
    public bool IsFiring => state == State.Firing || state == State.Holding || state == State.Fading;

    /// <summary>いま溜めを離したら、何メートル飛ぶか。</summary>
    public float CurrentLength => Mathf.Lerp(minLength, maxLength, Charge01);

    /// <summary>溜めを始める（ボタンを押した瞬間に呼ぶ）。ビームが出ている間は無視する。</summary>
    public void BeginCharge()
    {
        if (state != State.Idle)
        {
            return;
        }

        state = State.Charging;
        chargeTime = 0f;
    }

    /// <summary>ビームを撃つ（ボタンを離した瞬間に呼ぶ）。溜めた時間で距離が決まる。</summary>
    public void Release()
    {
        if (state != State.Charging)
        {
            return;
        }

        targetLength = CurrentLength;
        headDistance = 0f;
        state = State.Firing;
        preview.SetVisible(false);
    }

    // ------------------------------------------------------------
    // Unityから呼ばれる
    // ------------------------------------------------------------

    private void Awake()
    {
        if (lineMaterial == null)
        {
            lineMaterial = LightningLine.CreateAdditiveMaterial();
        }

        // ビームの芯と溜めの目安は、根元から先まで同じ太さにする
        var flat = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.9f, 1f), new Keyframe(1f, 0.5f));

        beam = new LightningLine(transform, "Beam", lineMaterial);
        beam.SetWidthCurve(flat);

        preview = new LightningLine(transform, "ChargePreview", lineMaterial);
        preview.SetWidthCurve(flat);

        bolts = new BeamBolt[boltCount];

        for (int i = 0; i < boltCount; i++)
        {
            bolts[i] = new BeamBolt(this, i);
        }

        if (headLight != null)
        {
            headLight.enabled = false;
        }
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        switch (state)
        {
            case State.Charging:
                chargeTime += deltaTime;
                DrawPreview();
                break;

            case State.Firing:
                headDistance += beamSpeed * deltaTime;

                if (headDistance >= targetLength)
                {
                    headDistance = targetLength;
                    state = State.Holding;
                    stateTimer = holdSeconds;
                }

                DrawBeam(1f, deltaTime);
                break;

            case State.Holding:
                stateTimer -= deltaTime;

                if (stateTimer <= 0f)
                {
                    state = State.Fading;
                    stateTimer = fadeSeconds;
                }

                DrawBeam(1f, deltaTime);
                break;

            case State.Fading:
                stateTimer -= deltaTime;

                if (stateTimer <= 0f)
                {
                    HideAll();
                    state = State.Idle;
                    break;
                }

                DrawBeam(stateTimer / fadeSeconds, deltaTime);
                break;
        }
    }

    // ------------------------------------------------------------
    // 描く
    // ------------------------------------------------------------

    /// <summary>溜め中：届く距離の目安をうすい線で出す。溜まるほど明るく、ちらつきも強くなる。</summary>
    private void DrawPreview()
    {
        float charge = Charge01;
        float length = CurrentLength;

        beamPoints[0] = Vector3.zero;
        beamPoints[1] = new Vector3(0f, 0f, length);

        // 溜めきったら点滅して「最大」と分かるようにする
        float flicker = charge >= 1f ? (Random.value < 0.5f ? 1f : 0.4f) : Random.Range(0.7f, 1f);
        float brightness = Mathf.Lerp(0.08f, 0.3f, charge) * flicker;

        preview.Set(beamPoints, 2,
            beamGlowColor * brightness,
            beamCoreColor * brightness,
            beamGlowWidth * 0.3f,
            beamCoreWidth * 0.3f);
    }

    /// <summary>ビーム本体と、まわりのらせんを描く。<paramref name="fade"/> は 1（そのまま）〜0（消える）。</summary>
    private void DrawBeam(float fade, float deltaTime)
    {
        // 芯：まっすぐだが、毎フレーム少しだけ震わせる
        for (int i = 0; i < BeamPointCount; i++)
        {
            float z = headDistance * i / (BeamPointCount - 1);
            Vector2 shake = i == 0 ? Vector2.zero : Random.insideUnitCircle * 0.02f;
            beamPoints[i] = new Vector3(shake.x, shake.y, z);
        }

        float pulse = Random.Range(0.85f, 1.15f);

        beam.Set(beamPoints, BeamPointCount,
            beamGlowColor,
            beamCoreColor,
            beamGlowWidth * fade * pulse,
            beamCoreWidth * fade * pulse);

        for (int i = 0; i < bolts.Length; i++)
        {
            bolts[i].Tick(deltaTime, fade);
        }

        if (headLight != null)
        {
            headLight.enabled = true;
            headLight.transform.localPosition = new Vector3(0f, 0f, headDistance);
            headLight.intensity = lightIntensity * fade * Random.Range(0.6f, 1f);
        }
    }

    private void HideAll()
    {
        beam.SetVisible(false);
        preview.SetVisible(false);

        for (int i = 0; i < bolts.Length; i++)
        {
            bolts[i].Hide();
        }

        if (headLight != null)
        {
            headLight.enabled = false;
        }
    }

    // ------------------------------------------------------------
    // ビームに巻きつく電撃1本
    // ------------------------------------------------------------

    private class BeamBolt
    {
        private readonly HelixBeam owner;
        private readonly LightningLine line;

        private readonly Vector3[] controlPoints = new Vector3[MaxControlPoints];
        private readonly Vector3[] points = new Vector3[LightningShape.PointCount(MaxControlPoints, SamplesPerSegment)];

        private float timer;

        public BeamBolt(HelixBeam owner, int index)
        {
            this.owner = owner;
            line = new LightningLine(owner.transform, $"BeamBolt_{index}", owner.lineMaterial);
        }

        public void Hide()
        {
            line.SetVisible(false);
            timer = 0f;
        }

        public void Tick(float deltaTime, float fade)
        {
            timer -= deltaTime;

            if (timer > 0f)
            {
                return;
            }

            timer = Random.Range(owner.regenerateInterval.x, owner.regenerateInterval.y);

            // ビームがまだ短すぎるときや、ときどき消えて点滅させる
            if (owner.headDistance < 0.5f || Random.value > owner.visibleChance)
            {
                line.SetVisible(false);
                return;
            }

            int controlCount = BuildHelix();
            int count = LightningShape.SampleHermite(controlPoints, controlCount, SamplesPerSegment, points);
            LightningShape.Jag(points, count, owner.jitter);

            line.Set(points, count,
                owner.boltGlowColor,
                owner.boltCoreColor,
                owner.boltGlowWidth * fade * Random.Range(0.7f, 1.2f),
                owner.boltCoreWidth * fade * Random.Range(0.8f, 1.3f));
        }

        /// <summary>
        /// ビームの軸（Z）のまわりに、らせん状に目印の点を置く。置いた数を返す。
        /// ビームのどこに出るか・長さ・回る向き・回る量は毎回ランダム。
        /// </summary>
        private int BuildHelix()
        {
            float head = owner.headDistance;
            float length = Mathf.Min(Random.Range(owner.boltLengthRange.x, owner.boltLengthRange.y), head);
            float start = Random.Range(0f, head - length);

            float turns = length * Random.Range(owner.turnsPerMeter.x, owner.turnsPerMeter.y);
            float direction = Random.value < 0.5f ? 1f : -1f;
            float phase = Random.Range(0f, Mathf.PI * 2f);

            // 1周あたり目印を4つ置くと、きれいならせんになる
            int controlCount = Mathf.Clamp(Mathf.CeilToInt(turns * 4f) + 1, 4, MaxControlPoints);

            for (int i = 0; i < controlCount; i++)
            {
                float t = i / (float)(controlCount - 1);
                float angle = phase + direction * t * turns * Mathf.PI * 2f;
                float r = owner.radius * Random.Range(0.75f, 1.3f);

                controlPoints[i] = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, start + t * length);
            }

            return controlCount;
        }
    }
}

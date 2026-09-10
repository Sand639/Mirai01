using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **時間が経つと、重力がひとりでに変わる。**
///
/// 決めた間隔ごとに、**用意しておいた候補の中からランダムに**新しい重力を選ぶ。
/// 月のように軽くなったり、いつもより重くなったりする。
///
/// | 段階 | 何が起きるか |
/// | --- | --- |
/// | 予告 | 「重力が変わる！」が出る（**身構える時間**） |
/// | 変化 | 決めた秒数をかけて、**じわじわ**変わる |
/// | しばらく | 次の予告まで、その重力のまま |
///
/// ## 使い方
///
/// **空のゲームオブジェクトに付けるだけ。** つなぐものは無い。
///
/// ## 軽くなると、どうなるか
///
/// **ジャンプの踏み切りの強さは変わらない。** 変わるのは落ちる速さだけ。
/// だから重力が弱いと**高く跳んで、ゆっくり落ちてくる。**
/// 「月面」らしさが出るのはこのため。
///
/// 中身は <see cref="WorldGravity"/> に入る。
/// </summary>
[DisallowMultipleComponent]
public class GravityShifter : MonoBehaviour
{
    [Header("重力の候補")]
    [Tooltip("**この中からランダムに1つ選ばれる。** 正の数で入れる。\n" +
             "月は約1.6、火星は約3.7、地球は9.81。\n" +
             "**同じ値を2つ入れれば、その重力が出やすくなる**")]
    [SerializeField]
    private List<float> gravityChoices = new List<float>
    {
        1.6f,   // 月
        3.7f,   // 火星
        6.0f,   // 少し軽い
        9.81f,  // 地球
        14.0f,  // 重い
    };

    [Tooltip("**同じ重力が続けて選ばれないようにする。** OFFだと「変わったのに何も変わらない」ことがある")]
    [SerializeField] private bool avoidSameTwice = true;

    [Header("いつ変わるか")]
    [Tooltip("次に変わるまでの、一番短い秒数")]
    [Min(0.5f)]
    [SerializeField] private float shortestInterval = 8f;

    [Tooltip("次に変わるまでの、一番長い秒数")]
    [Min(0.5f)]
    [SerializeField] private float longestInterval = 14f;

    [Tooltip("**変わる前に予告を出す秒数。** 0にすると予告なし")]
    [Min(0f)]
    [SerializeField] private float warningSeconds = 2f;

    [Tooltip("**変わりきるまでの秒数。** 0にすると一瞬で変わる（急に落ちるので危ない）")]
    [Min(0f)]
    [SerializeField] private float changeSeconds = 1.5f;

    [Header("その他")]
    [Tooltip("始めるときの重力。正の数で入れる")]
    [Min(0.1f)]
    [SerializeField] private float startGravity = 9.81f;

    [Tooltip("変わるたびにコンソールへ書き出す")]
    [SerializeField] private bool logChanges = true;

    /// <summary>いま動いている重力係。画面に出すときに見に来る。</summary>
    public static GravityShifter Current { get; private set; }

    /// <summary>**次に変わるまでの残り秒数。**</summary>
    public float NextChangeIn { get; private set; }

    /// <summary>**もうすぐ変わる**（予告の最中）か。</summary>
    public bool IsWarning => NextChangeIn <= warningSeconds && !IsChanging;

    /// <summary>いま、じわじわ変わっている最中か。</summary>
    public bool IsChanging { get; private set; }

    /// <summary>いまの重力の強さ（正の数）。画面に出すため。</summary>
    public float CurrentStrength => Mathf.Abs(WorldGravity.Value);

    /// <summary>
    /// **いまの重力を、ふつうと比べてどう言うか。**
    /// 「軽い」「ふつう」「重い」の3つ。
    /// </summary>
    public string StrengthLabel
    {
        get
        {
            float scale = WorldGravity.Scale;

            if (scale < 0.75f)
            {
                return "軽い";
            }

            return scale > 1.25f ? "重い" : "ふつう";
        }
    }

    private float changeTimer;
    private float changeDuration;
    private float fromGravity;
    private float toGravity;

    // 選ぶときの作業用。毎回作り直さないよう、使い回している
    private readonly List<float> candidates = new List<float>();

    private void Awake()
    {
        Current = this;
    }

    private void OnEnable()
    {
        WorldGravity.Set(-Mathf.Abs(startGravity));
        NextChangeIn = RandomInterval();
    }

    private void OnDisable()
    {
        // **付けっぱなしで次のシーンへ行かないように、必ず戻す**
        WorldGravity.Release();

        if (Current == this)
        {
            Current = null;
        }
    }

    private void Update()
    {
        if (GamePause.IsPaused)
        {
            return;
        }

        if (IsChanging)
        {
            Advance();
            return;
        }

        NextChangeIn -= Time.deltaTime;

        if (NextChangeIn <= 0f)
        {
            BeginChange();
        }
    }

    /// <summary>次の重力を決めて、変わり始める。</summary>
    public void BeginChange()
    {
        fromGravity = WorldGravity.Value;
        toGravity = -PickGravity();

        changeDuration = Mathf.Max(0f, changeSeconds);
        changeTimer = 0f;
        IsChanging = true;

        if (logChanges)
        {
            Debug.Log($"[GRAVITY] 重力が変わる：{Mathf.Abs(fromGravity):0.0} → {Mathf.Abs(toGravity):0.0}", this);
        }

        if (changeDuration <= 0f)
        {
            Finish();
        }
    }

    private void Advance()
    {
        changeTimer += Time.deltaTime;

        float t = Mathf.Clamp01(changeTimer / changeDuration);

        // 始めと終わりをゆるやかにする（急に体が重くなると操作しづらい）
        WorldGravity.Set(Mathf.Lerp(fromGravity, toGravity, Mathf.SmoothStep(0f, 1f, t)));

        if (t >= 1f)
        {
            Finish();
        }
    }

    private void Finish()
    {
        WorldGravity.Set(toGravity);

        IsChanging = false;
        NextChangeIn = RandomInterval();
    }

    /// <summary>
    /// **候補の中から1つ選ぶ。** 返すのは正の数。
    ///
    /// 候補が空だったり、全部が今と同じ値だったりしても止まらないようにしてある。
    /// </summary>
    private float PickGravity()
    {
        float now = Mathf.Abs(WorldGravity.Value);

        candidates.Clear();

        if (gravityChoices != null)
        {
            for (int i = 0; i < gravityChoices.Count; i++)
            {
                float value = Mathf.Abs(gravityChoices[i]);

                // 0は「重力なし」で戻ってこられなくなるので使わない
                if (value < 0.01f)
                {
                    continue;
                }

                // 同じ値が続くと「変わったのに何も起きない」になる
                if (avoidSameTwice && Mathf.Approximately(value, now))
                {
                    continue;
                }

                candidates.Add(value);
            }
        }

        if (candidates.Count > 0)
        {
            return candidates[Random.Range(0, candidates.Count)];
        }

        // 候補が1つしか無い（＝いまと同じ）なら、そのまま続ける
        if (gravityChoices != null && gravityChoices.Count > 0)
        {
            return now;
        }

        Debug.LogWarning($"{name}: 重力の候補が1つも入っていません。ふつうの重力のままにします。", this);

        return Mathf.Abs(WorldGravity.Standard);
    }

    private float RandomInterval()
    {
        float shortest = Mathf.Min(shortestInterval, longestInterval);
        float longest = Mathf.Max(shortestInterval, longestInterval);

        // 予告の時間も、この中に含める
        return Mathf.Max(warningSeconds, Random.Range(shortest, longest));
    }
}

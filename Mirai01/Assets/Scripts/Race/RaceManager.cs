using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **レースの進行役。**
///
/// 「用意 → カウントダウン → 走行 → ゴール」を進め、
/// **誰が何周したか**を数える。
///
/// | 段階 | 何が起きるか |
/// | --- | --- |
/// | 用意（Waiting） | 少し待つ。カートは動かせない |
/// | カウントダウン（Countdown） | 3・2・1。カートは動かせない |
/// | 走行（Running） | 走れる。時間を測る |
/// | 終了（Finished） | 全員がゴールした |
///
/// ## 通信（マルチプレイ）にする時のために
///
/// **周回を数えるのは、この1か所だけ**にしてある。
/// 通信にするときは、**この部品をホストの上でだけ動かし**、
/// 段階と周回数を配れば済む形を狙っている。
/// カート側（<see cref="KartController"/>）には順位も周回も持たせていない。
/// </summary>
// 走る側（カート）が動いたあとに調べたいので、最後に回す
[DefaultExecutionOrder(100)]
public class RaceManager : MonoBehaviour
{
    /// <summary>レースの進み具合。</summary>
    public enum Phase
    {
        /// <summary>用意（少し待つ）</summary>
        Waiting,

        /// <summary>カウントダウン中</summary>
        Countdown,

        /// <summary>走行中</summary>
        Running,

        /// <summary>全員ゴール</summary>
        Finished,
    }

    /// <summary>いま動いているレース。HUDなどが見に来る。</summary>
    public static RaceManager Current { get; private set; }

    [Header("ルール")]
    [Tooltip("**何周でゴールか。** まずは1周")]
    [Min(1)]
    [SerializeField] private int lapsToFinish = 1;

    [Tooltip("再生してから、カウントダウンが始まるまでの秒数")]
    [SerializeField] private float readySeconds = 1.5f;

    [Tooltip("カウントダウンの秒数（3なら 3・2・1）")]
    [Min(1)]
    [SerializeField] private int countdownSeconds = 3;

    [Tooltip("再生したら、ひとりでにレースを始めるか")]
    [SerializeField] private bool startAutomatically = true;

    [Header("コース")]
    [Tooltip("**通過点を、通る順番に入れる。** 0番目がスタート／ゴールの線。" +
             "空のままなら、シーンの中から番号順に集める")]
    [SerializeField] private List<RaceCheckpoint> checkpoints = new List<RaceCheckpoint>();

    [Tooltip("スタート位置。**上から順に、参加している人へ割り当てる**（通信で人数が増えたとき用）")]
    [SerializeField] private List<Transform> startGrid = new List<Transform>();

    [Header("操作")]
    [Tooltip("**やり直しのキー。** 押すとスタート地点に戻る")]
    [SerializeField] private Key restartKey = Key.R;

    /// <summary>いまの段階。</summary>
    public Phase State { get; private set; } = Phase.Waiting;

    /// <summary>カウントダウンの残り秒数。</summary>
    public float CountdownRemaining { get; private set; }

    /// <summary>スタートしてからの秒数。</summary>
    public float ElapsedTime { get; private set; }

    /// <summary>何周でゴールか。</summary>
    public int LapsToFinish => lapsToFinish;

    /// <summary>通過点の数。</summary>
    public int CheckpointCount => checkpoints.Count;

    /// <summary>「スタート！」を出しておく残り秒数。0より大きい間だけ出す。</summary>
    public float StartSignalRemaining { get; private set; }

    private int finishedCount;

    private void Awake()
    {
        Current = this;

        CollectCheckpointsIfEmpty();
    }

    private void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    private void Start()
    {
        ResetRace();
    }

    private void LateUpdate()
    {
        if (GamePause.IsPaused)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard[restartKey].wasPressedThisFrame)
        {
            ResetRace();
            return;
        }

        AdvancePhase();

        if (State == Phase.Running || State == Phase.Finished)
        {
            CheckCheckpoints();
        }

        // 次のフレームのために、今の位置を控えておく
        for (int i = 0; i < RaceRacer.All.Count; i++)
        {
            RaceRacer.All[i].CommitPosition();
        }
    }

    // ------------------------------------------------------------
    // 進行
    // ------------------------------------------------------------

    /// <summary>用意の状態に戻し、全員をスタート地点へ並べる。</summary>
    public void ResetRace()
    {
        State = Phase.Waiting;
        CountdownRemaining = readySeconds + countdownSeconds;
        ElapsedTime = 0f;
        StartSignalRemaining = 0f;
        finishedCount = 0;

        LineUpRacers();

        for (int i = 0; i < RaceRacer.All.Count; i++)
        {
            RaceRacer.All[i].ResetToStart(FirstCheckpoint());
        }

        if (checkpoints.Count == 0)
        {
            Debug.LogWarning($"{name}: 通過点が1つもありません。周回を数えられません。", this);
        }
    }

    /// <summary>すぐに走り出せる状態にする（用意とカウントダウンを飛ばす）。</summary>
    public void StartRace()
    {
        State = Phase.Running;
        CountdownRemaining = 0f;
        ElapsedTime = 0f;
        StartSignalRemaining = 1.2f;

        SetControlEnabled(true);
    }

    private void AdvancePhase()
    {
        if (StartSignalRemaining > 0f)
        {
            StartSignalRemaining -= Time.deltaTime;
        }

        switch (State)
        {
            case Phase.Waiting:
                if (!startAutomatically)
                {
                    return;
                }

                CountdownRemaining -= Time.deltaTime;

                if (CountdownRemaining <= countdownSeconds)
                {
                    State = Phase.Countdown;
                }

                return;

            case Phase.Countdown:
                CountdownRemaining -= Time.deltaTime;

                if (CountdownRemaining <= 0f)
                {
                    StartRace();
                }

                return;

            case Phase.Running:
                ElapsedTime += Time.deltaTime;
                return;
        }
    }

    private void SetControlEnabled(bool canControl)
    {
        for (int i = 0; i < RaceRacer.All.Count; i++)
        {
            RaceRacer racer = RaceRacer.All[i];

            // ゴールした人は、そのまま止めておく
            racer.ControlEnabled = canControl && !racer.Finished;
        }
    }

    // ------------------------------------------------------------
    // 周回を数える
    // ------------------------------------------------------------

    private void CheckCheckpoints()
    {
        if (checkpoints.Count == 0)
        {
            return;
        }

        for (int i = 0; i < RaceRacer.All.Count; i++)
        {
            RaceRacer racer = RaceRacer.All[i];

            if (racer.Finished)
            {
                continue;
            }

            RaceCheckpoint next = checkpoints[racer.NextCheckpoint % checkpoints.Count];

            if (next == null || !next.WasCrossed(racer.PreviousPosition, racer.TrackedPosition))
            {
                continue;
            }

            PassCheckpoint(racer, next);
        }
    }

    private void PassCheckpoint(RaceRacer racer, RaceCheckpoint passed)
    {
        racer.NextCheckpoint = (racer.NextCheckpoint + 1) % checkpoints.Count;

        // **スタート／ゴールの線を通ったら1周ぶん。**
        // 他の通過点を全部通ってからでないと、ここには来られない
        if (!passed.IsStartLine)
        {
            return;
        }

        racer.Lap++;

        if (racer.Lap < lapsToFinish)
        {
            Debug.Log($"[RACE] {racer.RacerName}：{racer.Lap}周目を終えた（{FormatTime(ElapsedTime)}）");
            return;
        }

        finishedCount++;

        racer.Finished = true;
        racer.FinishTime = ElapsedTime;
        racer.Rank = finishedCount;
        racer.ControlEnabled = false;

        Debug.Log($"[RACE] {racer.RacerName}：ゴール！ {racer.Rank}位 タイム {FormatTime(racer.FinishTime)}");

        if (finishedCount >= RaceRacer.All.Count)
        {
            State = Phase.Finished;
        }
    }

    /// <summary>
    /// **走り始めるときに、次に狙う通過点。**
    ///
    /// スタート地点は0番の線の**手前**にあるので、
    /// スタート直後に0番を通ってしまう。**それを1周と数えないため**に1番から始める。
    /// </summary>
    private int FirstCheckpoint()
    {
        return checkpoints.Count > 1 ? 1 : 0;
    }

    // ------------------------------------------------------------
    // 並べる・集める
    // ------------------------------------------------------------

    private void LineUpRacers()
    {
        if (startGrid.Count == 0)
        {
            return;
        }

        for (int i = 0; i < RaceRacer.All.Count && i < startGrid.Count; i++)
        {
            Transform slot = startGrid[i];

            if (slot != null)
            {
                RaceRacer.All[i].SetStartPoint(slot.position, slot.rotation);
            }
        }
    }

    /// <summary>通過点が入っていなければ、シーンから集めて番号順に並べる。</summary>
    private void CollectCheckpointsIfEmpty()
    {
        checkpoints.RemoveAll(point => point == null);

        if (checkpoints.Count > 0)
        {
            return;
        }

        checkpoints.AddRange(FindObjectsByType<RaceCheckpoint>(FindObjectsSortMode.None));
        checkpoints.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    // ------------------------------------------------------------
    // 表示用
    // ------------------------------------------------------------

    /// <summary>秒数を「1:23.45」の形にする。</summary>
    public static string FormatTime(float seconds)
    {
        if (seconds < 0f)
        {
            seconds = 0f;
        }

        int minutes = (int)(seconds / 60f);
        float rest = seconds - minutes * 60f;

        return $"{minutes}:{rest:00.00}";
    }

    /// <summary>いま画面の中央に出すべき大きな文字。無ければ空文字。</summary>
    public string BigMessage()
    {
        if (State == Phase.Countdown)
        {
            return Mathf.CeilToInt(CountdownRemaining).ToString();
        }

        if (StartSignalRemaining > 0f)
        {
            return "スタート！";
        }

        return string.Empty;
    }
}

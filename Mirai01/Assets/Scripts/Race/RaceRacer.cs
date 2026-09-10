using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **レースに出ている1台分の記録。**
///
/// 走る仕組み（<see cref="KartController"/>）とは分けてある。
/// **「どう動くか」と「今どこまで進んだか」は別のこと**で、
/// 分けておくと、あとから操作をAIに差し替えたり、
/// **通信で他の人のカートを動かしたり**しても、記録の付け方は変えずに済む。
///
/// 順番の管理は <see cref="RaceManager"/> がまとめて行う。
/// ここは**言われたことを覚えているだけ**にしてある。
/// </summary>
[DisallowMultipleComponent]
public class RaceRacer : MonoBehaviour
{
    /// <summary>
    /// **いま走っている全員。**
    ///
    /// 当たり判定で探すのではなく、自分から名乗り出る形にしている。
    /// 通信では「自分以外の当たり判定が消えている」ことがあり、
    /// **探しにいく方式は当てにならない**ため（`Documents/AIの申し送り.md` 参照）。
    /// </summary>
    public static readonly List<RaceRacer> All = new List<RaceRacer>();

    [Tooltip("画面に出す名前")]
    [SerializeField] private string racerName = "プレイヤー";

    [Tooltip("**この人の操作を画面に出すか。** 通信では、自分のカートだけ true にする")]
    [SerializeField] private bool isLocalPlayer = true;

    /// <summary>画面に出す名前。</summary>
    public string RacerName => racerName;

    /// <summary>自分が操作しているカートか。HUDがどれを映すか決めるのに使う。</summary>
    public bool IsLocalPlayer => isLocalPlayer;

    /// <summary>**走り終えた周回数。** スタート直後は0。</summary>
    public int Lap { get; set; }

    /// <summary>次に通らなければいけないチェックポイントの番号。</summary>
    public int NextCheckpoint { get; set; }

    /// <summary>ゴールしたか。</summary>
    public bool Finished { get; set; }

    /// <summary>ゴールしたときの、スタートからの秒数。</summary>
    public float FinishTime { get; set; }

    /// <summary>**ゴールした順位**（1から）。まだゴールしていなければ 0。</summary>
    public int Rank { get; set; }

    /// <summary>
    /// **操作を受け付けてよいか。**
    /// カウントダウン中とゴール後は false になり、<see cref="KartController"/> が止まる。
    /// </summary>
    public bool ControlEnabled { get; set; }

    /// <summary>前のフレームの位置。**線を跨いだかを調べる**のに使う。</summary>
    public Vector3 PreviousPosition { get; private set; }

    /// <summary>
    /// **どこを「この人の位置」とみなすか。**
    ///
    /// 空なら自分の位置。
    /// ロボットのように**中の体が動いて、入れ物は動かない**作りのときに、
    /// 走っている体を入れて使う（`RobotRacerLink`）。
    /// </summary>
    public Transform PositionSource { get; set; }

    /// <summary>いまの位置。周回を数えるときは、必ずこちらを使う。</summary>
    public Vector3 TrackedPosition =>
        PositionSource != null ? PositionSource.position : transform.position;

    /// <summary>いまの速さ（1秒あたりのメートル）。画面に出すため。</summary>
    public float CurrentSpeed { get; private set; }

    /// <summary>
    /// **やり直しのときに、代わりに呼ばれる処理。**
    ///
    /// 入っていなければ、自分の位置をスタート地点へ戻すだけ。
    /// 体がいくつかに分かれている作りでは、ここで全部まとめて戻す。
    /// </summary>
    public System.Action<Vector3, Quaternion> ResetHandler { get; set; }

    /// <summary>スタート地点。やり直しで戻す先。</summary>
    public Vector3 StartPosition { get; private set; }

    /// <summary>スタート時の向き。</summary>
    public Quaternion StartRotation { get; private set; }

    private void Awake()
    {
        StartPosition = transform.position;
        StartRotation = transform.rotation;
        PreviousPosition = transform.position;
    }

    private void Start()
    {
        // 位置を見る先が入ってから測り直す（RobotRacerLink が Awake で入れる）
        PreviousPosition = TrackedPosition;
    }

    private void OnEnable()
    {
        if (!All.Contains(this))
        {
            All.Add(this);
        }
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    /// <summary>
    /// **スタート地点をここに決め直す。**
    /// 並べ直したあと（スタート位置に着けたあと）に呼ぶ。
    /// </summary>
    public void SetStartPoint(Vector3 position, Quaternion rotation)
    {
        StartPosition = position;
        StartRotation = rotation;
    }

    /// <summary>今の位置を「前のフレームの位置」として控える。<see cref="RaceManager"/> が呼ぶ。</summary>
    public void CommitPosition()
    {
        Vector3 now = TrackedPosition;

        if (Time.deltaTime > 0f)
        {
            Vector3 moved = now - PreviousPosition;
            moved.y = 0f;

            CurrentSpeed = moved.magnitude / Time.deltaTime;
        }

        PreviousPosition = now;
    }

    /// <summary>記録を白紙に戻し、スタート地点へ戻す。</summary>
    public void ResetToStart(int firstCheckpoint)
    {
        Lap = 0;
        NextCheckpoint = firstCheckpoint;
        Finished = false;
        FinishTime = 0f;
        Rank = 0;
        ControlEnabled = false;
        CurrentSpeed = 0f;

        // 体が分かれている作りでは、戻し方をそちらに任せる
        if (ResetHandler != null)
        {
            ResetHandler(StartPosition, StartRotation);
            PreviousPosition = TrackedPosition;

            return;
        }

        // CharacterController が付いていると、位置を入れても押し戻されることがある。
        // 一度切ってから動かすのが確実
        CharacterController controller = GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;

        if (wasEnabled)
        {
            controller.enabled = false;
        }

        transform.SetPositionAndRotation(StartPosition, StartRotation);

        if (wasEnabled)
        {
            controller.enabled = true;
        }

        PreviousPosition = StartPosition;

        KartController kart = GetComponent<KartController>();

        if (kart != null)
        {
            kart.StopImmediately();
        }
    }
}

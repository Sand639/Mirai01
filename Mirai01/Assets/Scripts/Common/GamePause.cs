using UnityEngine;

/// <summary>
/// **ゲームが止まっているか**を、どこからでも見られるようにしたもの。
///
/// ポーズ画面（<see cref="PauseMenu"/>）が開いている間だけ true になる。
///
/// ## なぜ必要か
///
/// 止めている間は `Time.timeScale` が 0 になるので、**歩く・落ちるは自動的に止まる。**
/// けれど、**マウスでの視点移動は止まらない**（動いた量をそのまま使っているため）。
/// カーソルの出し入れも、放っておくと**カメラ側とポーズ画面で取り合いになる。**
///
/// そこで「いま止まっているか」を1か所で持ち、
/// **カメラ側がそれを見て、自分から手を引く**形にしてある。
/// </summary>
public static class GamePause
{
    /// <summary>いまゲームが止まっているか。</summary>
    public static bool IsPaused { get; private set; }

    /// <summary>止める・動かすが切り替わったフレーム。まだ一度も切り替わっていなければ -1。</summary>
    private static int lastChangedFrame = -1;

    /// <summary>このフレームに、止める・動かすが切り替わったか。</summary>
    public static bool ChangedThisFrame => Time.frameCount == lastChangedFrame;

    /// <summary>
    /// **いま、遊びの操作を受け付けてはいけないか。**
    /// 入力を読む側（プレイヤー・カメラ・複製配置など）は、`IsPaused` ではなくこちらを見る。
    ///
    /// ## なぜ「切り替わったフレーム」も含めるか
    ///
    /// ポーズ画面は Escape や左クリックで閉じるが、**閉じるのと同じフレームのうちに**
    /// 「止まっていない」へ戻る。そのあとに動く側は、**同じ Escape・同じクリックをもう一度拾う。**
    /// `wasPressedThisFrame` はそのフレーム中ずっと true のままだからである。
    ///
    /// 実際に、**ポーズ画面を閉じた瞬間に複製の下書きが消える／物が置かれる**という形で出る
    /// （`リスクリスト.md` 2026/9/14 登録）。1フレームだけ入力を捨てれば、この取り合いは起きない。
    /// </summary>
    public static bool BlocksInput => IsPaused || ChangedThisFrame;

    /// <summary>
    /// 止める・動かすを切り替える。
    /// **`Time.timeScale` はここでしか触らない**（あちこちで触ると戻し忘れる）。
    /// </summary>
    public static void SetPaused(bool paused)
    {
        if (IsPaused == paused)
        {
            return;
        }

        IsPaused = paused;
        lastChangedFrame = Time.frameCount;
        Time.timeScale = paused ? 0f : 1f;
    }

    /// <summary>
    /// 再生を始めるたびに、止まっていない状態から始める。
    ///
    /// **この値はゲームを通してひとつしかない**ので、
    /// 止めたまま再生を止めると、次に始めたときも止まったままになりかねない。
    /// その事故を防ぐためのもの。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnStart()
    {
        IsPaused = false;
        lastChangedFrame = -1;
        Time.timeScale = 1f;
    }
}

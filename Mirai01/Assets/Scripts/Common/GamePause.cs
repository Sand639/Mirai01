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
        Time.timeScale = 1f;
    }
}

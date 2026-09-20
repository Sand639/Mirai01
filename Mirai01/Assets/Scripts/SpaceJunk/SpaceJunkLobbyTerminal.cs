using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **ロビーに置く「設定端末」。** 近づいて `E` を押すと、ホストの詳細設定が開く。
///
/// ## ホストだけが使える
///
/// 参加者が近づいても、**押す案内も出ないし開かない。**
/// 設定を変えられるのはホストだけなので、開けても意味が無いため。
///
/// ## なぜ端末にしたのか
///
/// 前は設定がロビーの画面にずっと出っぱなしで、**画面が設定で埋まっていた**。
/// 端末を置いて「開きたいときだけ開く」形にすると、
/// ロビーでは歩き回れて、設定は必要なときだけ前に出る（2026/9/17・大槻さん）。
///
/// ## 開いている間は動けない
///
/// 設定を触っている最中に歩いたりフックを撃ったりしないよう、
/// **開いている間は自分の移動とフックを止める。**
///
/// 画面を描くのは <see cref="SpaceJunkLobbyUI"/>。こちらは「開いているか」だけを持つ。
/// </summary>
public class SpaceJunkLobbyTerminal : MonoBehaviour
{
    /// <summary>ロビーに置かれている端末。画面を描くほうから探すために持っている。</summary>
    public static SpaceJunkLobbyTerminal Current { get; private set; }

    [Header("操作")]
    [Tooltip("この距離まで近づくと使える（メートル）")]
    [SerializeField] private float interactRange = 3f;

    [Tooltip("開く／閉じるキー")]
    [SerializeField] private Key interactKey = Key.E;

    [Header("見た目")]
    [Tooltip("近づいたときに色を変える見た目。空でもよい")]
    [SerializeField] private Renderer highlightRenderer;

    [Tooltip("ホストが近づいているときの色")]
    [SerializeField] private Color nearColor = new Color(0.4f, 1f, 0.6f);

    /// <summary>詳細設定が開いているか。</summary>
    public bool IsOpen { get; private set; }

    /// <summary>ホストがこの端末のそばにいるか（案内を出すかどうかの判断に使う）。</summary>
    public bool IsHostNearby { get; private set; }

    /// <summary>押すキーの名前（案内の文言に使う）。</summary>
    public string InteractKeyName => interactKey.ToString();

    private Color originalColor = Color.white;

    private void Awake()
    {
        Current = this;

        // 色を変える前に元の色を控えておく（AIの申し送り参照）
        if (highlightRenderer != null)
        {
            originalColor = highlightRenderer.sharedMaterial.color;
        }
    }

    private void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    private void Update()
    {
        NetworkManager manager = NetworkManager.Singleton;
        bool isHost = manager != null && manager.IsListening && manager.IsServer;

        Transform player = FindLocalPlayer();

        IsHostNearby = isHost
                    && player != null
                    && Vector3.Distance(player.position, transform.position) <= interactRange;

        if (IsHostNearby && WasPressed(interactKey))
        {
            IsOpen = !IsOpen;
        }

        // 離れた／ホストでなくなったら閉じる
        if (!IsHostNearby && IsOpen)
        {
            IsOpen = false;
        }

        // Escape でも閉じられるようにする（ポーズ画面と取り合いにならないよう、開いているときだけ）
        if (IsOpen && WasPressed(Key.Escape))
        {
            IsOpen = false;
        }

        ApplyHighlight();
        SetLocalPlayerControlEnabled(!IsOpen);
    }

    /// <summary>
    /// キーが押された瞬間か。
    ///
    /// **このプロジェクトは Input System（新）だけを使う設定**（Active Input Handling）なので、
    /// 古い `Input.GetKeyDown` は動かない。`Keyboard.current` から読むこと。
    /// （2026/9/20・E キーが効かなかった原因がこれだった）
    /// </summary>
    private static bool WasPressed(Key key)
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[key].wasPressedThisFrame;
    }

    /// <summary>このPCで操作しているプレイヤーの位置。</summary>
    private static Transform FindLocalPlayer()
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.IsOwner)
            {
                return player.transform;
            }
        }

        return null;
    }

    private void ApplyHighlight()
    {
        if (highlightRenderer == null)
        {
            return;
        }

        highlightRenderer.material.color = IsHostNearby ? nearColor : originalColor;
    }

    /// <summary>設定を開いている間、自分の移動とフックを止める。</summary>
    private void SetLocalPlayerControlEnabled(bool enabledState)
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player == null || !player.IsOwner)
            {
                continue;
            }

            FishingPlayerController move = player.GetComponent<FishingPlayerController>();
            if (move != null)
            {
                move.enabled = enabledState;
            }

            HookController hook = player.GetComponent<HookController>();
            if (hook != null)
            {
                hook.enabled = enabledState;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 使える距離をシーン画面に出す
        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}

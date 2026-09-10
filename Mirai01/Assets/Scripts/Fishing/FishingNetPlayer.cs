using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **オンラインの釣りプレイヤー1人分。**
///
/// 役割は3つ。
///   ① **参加番号とチームを決める**（ホストが決めて全員に配る）
///   ② **自分の1人だけが操作できるようにする**（他の人のぶんは操作スクリプトを止める）
///   ③ **フックと糸を他の人にも見せる**（自分のフックの位置を送り、他の人はそれを再現する）
///
/// 移動そのものは既存の <see cref="FishingPlayerController"/> がそのまま動かし、
/// 位置は `NetworkTransform` が配る（`動きは本人` の形）。
/// このスクリプトは通信のための上乗せだけを持つ。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class FishingNetPlayer : NetworkBehaviour
{
    /// <summary>いまシーンにいるオンラインプレイヤー全員。人数とチーム分けに使う。</summary>
    public static readonly List<FishingNetPlayer> All = new List<FishingNetPlayer>();

    [Header("自分だけが動かすもの")]
    [Tooltip("自分のぶんだけ有効にするスクリプト。他の人のぶんは止める")]
    [SerializeField] private MonoBehaviour[] ownerOnlyScripts;

    [Tooltip("他の人のぶんでは切る CharacterController（切らないと送られてきた位置に移せない）")]
    [SerializeField] private CharacterController characterController;

    [Header("見た目")]
    [Tooltip("チームの色に塗る見た目")]
    [SerializeField] private Renderer[] teamRenderers;

    [Header("フックと糸")]
    [Tooltip("フックの先端。他の人のぶんは、送られてきた位置に置くだけ")]
    [SerializeField] private Transform hookVisual;

    [Tooltip("糸の見た目")]
    [SerializeField] private HookLine line;

    [Tooltip("糸の起点（手元）")]
    [SerializeField] private Transform handPoint;

    [Header("出てくる場所")]
    [Tooltip("中心からどれだけ離れた場所に出てくるか（メートル）")]
    [SerializeField] private float spawnRadius = 6f;

    // ---- ホストが決めて全員に配るもの ----

    /// <summary>参加番号（0から）。**ホストが決める。**</summary>
    private readonly NetworkVariable<int> playerIndex = new NetworkVariable<int>(-1);

    // ---- 本人が送るもの（他の人が見るため） ----

    /// <summary>フックの先端の位置。**本人が送る。**</summary>
    private readonly NetworkVariable<Vector3> hookPosition = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    /// <summary>糸が出ているか。**本人が送る。**</summary>
    private readonly NetworkVariable<bool> lineVisible = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    /// <summary>参加番号（0から）。まだ決まっていなければ -1。</summary>
    public int PlayerIndex => playerIndex.Value;

    /// <summary>所属チーム。</summary>
    public int TeamIndex => FishingTeams.TeamOf(playerIndex.Value);

    /// <summary>画面に出す名前。</summary>
    public string DisplayName =>
        playerIndex.Value < 0 ? "参加中…" : $"プレイヤー{playerIndex.Value + 1}";

    private HookController hookController;

    private void Awake()
    {
        hookController = GetComponent<HookController>();

        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }
    }

    public override void OnNetworkSpawn()
    {
        // ホストが参加番号を決める。**今いる人数がそのまま次の番号になる**
        if (IsServer)
        {
            playerIndex.Value = Mathf.Min(All.Count, FishingTeams.MaxPlayers - 1);
        }

        All.Add(this);

        playerIndex.OnValueChanged += OnPlayerIndexChanged;
        ApplyTeamColor();

        Debug.Log($"[FISH] プレイヤー {OwnerClientId} が出てきました" +
                  $"（参加番号 {playerIndex.Value} ／ 自分が操作する：{IsOwner}）");

        if (IsOwner)
        {
            MoveToSpawnPoint();
            FollowWithCamera();
        }
        else
        {
            // 他の人のぶん。**このPCでは操作しない。**
            SetOwnerScriptsEnabled(false);

            if (characterController != null)
            {
                characterController.enabled = false;
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        playerIndex.OnValueChanged -= OnPlayerIndexChanged;
        All.Remove(this);
    }

    private void OnPlayerIndexChanged(int before, int after)
    {
        ApplyTeamColor();
    }

    /// <summary>釣り会場に入って、配置とカメラを合わせ終わったか。</summary>
    private bool placedInMatch;

    private void Update()
    {
        if (IsOwner)
        {
            // **プレイヤーはシーンをまたいで生き続ける**（ロビーで生まれた本体がそのまま来る）ので、
            // 釣り会場に着いたことに気づいたら、そこで出てくる場所とカメラを合わせ直す。
            // ホストからの合図を待つ形にすると、読み込みの速さで順番が変わって取りこぼすため、
            // **試合のまとめ役がこのPCに現れたかどうか**で判断している
            bool inMatch = FishingMatch.Current != null;

            if (inMatch && !placedInMatch)
            {
                placedInMatch = true;
                MoveToSpawnPoint();
                FollowWithCamera();
            }
            else if (!inMatch && placedInMatch)
            {
                // ロビーへ戻ったとき（次にまた会場へ入れるようにしておく）
                placedInMatch = false;
            }

            SendHookState();
        }
        else
        {
            ShowRemoteHookState();
        }
    }

    // ------------------------------------------------------------
    // フックと糸の共有
    // ------------------------------------------------------------

    /// <summary>自分のフックの位置と、糸が出ているかを送る。</summary>
    private void SendHookState()
    {
        if (hookController == null || hookVisual == null)
        {
            return;
        }

        hookPosition.Value = hookVisual.position;
        lineVisible.Value = hookController.Phase != HookController.HookPhase.Idle
                         && hookController.Phase != HookController.HookPhase.Charging;
    }

    /// <summary>他の人のフックと糸を、送られてきた値で再現する。</summary>
    private void ShowRemoteHookState()
    {
        if (hookVisual != null)
        {
            hookVisual.position = hookPosition.Value;
        }

        if (line != null && handPoint != null)
        {
            line.Show(lineVisible.Value);
            if (lineVisible.Value)
            {
                line.SetEnds(handPoint.position, hookPosition.Value);
            }
        }
    }

    // ------------------------------------------------------------
    // 見た目と出てくる場所
    // ------------------------------------------------------------

    /// <summary>
    /// チームの色に塗る。
    /// **参加番号から計算で決めているので、色を通信で送る必要がない**
    /// （どのPCで見ても同じ人が同じ色になる）。
    /// </summary>
    private void ApplyTeamColor()
    {
        if (teamRenderers == null || playerIndex.Value < 0)
        {
            return;
        }

        Color color = FishingTeams.TeamColor(TeamIndex);

        foreach (Renderer renderer in teamRenderers)
        {
            if (renderer != null)
            {
                renderer.material.color = color;
            }
        }
    }

    /// <summary>自分のぶんだけ操作スクリプトを有効／無効にする。</summary>
    private void SetOwnerScriptsEnabled(bool enabledState)
    {
        if (ownerOnlyScripts == null)
        {
            return;
        }

        foreach (MonoBehaviour script in ownerOnlyScripts)
        {
            if (script != null)
            {
                script.enabled = enabledState;
            }
        }
    }

    /// <summary>参加番号ごとに、円周上の違う場所へ移す。全員が重なって出てくるのを防ぐため。</summary>
    private void MoveToSpawnPoint()
    {
        int index = Mathf.Max(0, playerIndex.Value);
        float angle = index * (360f / FishingTeams.MaxPlayers);
        Vector3 position = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * spawnRadius);

        if (characterController != null)
        {
            characterController.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, angle + 180f, 0f));
            characterController.enabled = true;
        }
        else
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, angle + 180f, 0f));
        }
    }

    /// <summary>
    /// **シーンに置いてあるもの（カメラ・UI）と自分を結びつける。**
    ///
    /// プレイヤーはプレハブから生まれるので、**シーンの中身をあらかじめ入れておけない。**
    /// 自分のぶんが生まれた時点で、ここで探して渡す。
    /// </summary>
    private void FollowWithCamera()
    {
        TopDownCameraFollow follow = FindFirstObjectByType<TopDownCameraFollow>();

        if (follow != null)
        {
            follow.SetTarget(transform);
        }

        // 狙いの計算に使うカメラ
        PlayerAimController aim = GetComponent<PlayerAimController>();
        if (aim != null && Camera.main != null)
        {
            aim.SetCamera(Camera.main);
        }

        // チャージ量とスキルチェックのゲージ
        if (hookController != null)
        {
            HookChargeUI ui = FindFirstObjectByType<HookChargeUI>();
            if (ui != null)
            {
                hookController.SetUI(ui);
            }
        }
    }
}

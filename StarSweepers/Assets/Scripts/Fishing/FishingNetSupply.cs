using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **オンラインの物資1つ分。** 誰が引っ掛けているかを整理し、投げを成立させる。
///
/// ## 誰が動かすか（ここが一番大事）
///
/// | 場面 | 動かす人 | 理由 |
/// | --- | --- | --- |
/// | 引き寄せ中 | **引っ掛けた本人** | ゲージと物資の動きがずれると、スキルチェックが成立しない |
/// | 投げたあとの飛び方 | **ホスト** | どこに落ちたか・どのゴールに入ったかを、全員で一致させる必要がある |
/// | 二重に引っ掛けられないか | **ホスト** | 早い者勝ちをホストが決める |
///
/// 引っ掛けた瞬間に**持ち主をその人へ移す**（`ChangeOwnership`）ので、
/// 引き寄せの動きは既存の <see cref="ThrowController"/> がそのまま動かせる。
/// 投げるときはホストへ知らせ、**ホストが力を加えて持ち主を取り戻す。**
///
/// 物理を回すのは「そのときの持ち主」だけ。それ以外のPCでは止めてある
/// （全員が物理を回すと、少しずつずれて位置がバラバラになるため）。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(HookableObject))]
public class FishingNetSupply : NetworkBehaviour
{
    /// <summary>いま引っ掛けている人の参加番号。**ホストが決める。** -1で誰も掛けていない。</summary>
    private readonly NetworkVariable<int> hookedByPlayerIndex = new NetworkVariable<int>(-1);

    /// <summary>最後に投げた人の参加番号。**共通ゴールの点を誰に入れるか**の判断に使う。</summary>
    private readonly NetworkVariable<int> lastThrownByPlayerIndex = new NetworkVariable<int>(-1);

    // 爆発前の「まだ届いていなかった鉤・投げ・解除」の指示を、爆発後に採用しない。
    private readonly NetworkVariable<uint> explosionRevision = new NetworkVariable<uint>(0);
    private uint localClaimRevision;
    private uint localClaimAttempt;

    /// <summary>いま誰かに引っ掛けられているか。</summary>
    public bool IsClaimed => hookedByPlayerIndex.Value >= 0;

    /// <summary>最後に投げた人の参加番号。誰も投げていなければ -1。</summary>
    public int LastThrownByPlayerIndex => lastThrownByPlayerIndex.Value;

    private HookableObject hookable;
    private Rigidbody body;

    private void Awake()
    {
        hookable = GetComponent<HookableObject>();
        body = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        ApplyPhysicsAuthority();
    }

    public override void OnGainedOwnership()
    {
        ApplyPhysicsAuthority();
    }

    public override void OnLostOwnership()
    {
        ApplyPhysicsAuthority();
    }

    /// <summary>
    /// **物理を回すのは、いまの持ち主のPCだけ。** それ以外では止める。
    /// 止めた側は `NetworkTransform` が配ってくる位置に従うだけになる。
    /// </summary>
    private void ApplyPhysicsAuthority()
    {
        if (body == null)
        {
            return;
        }

        // 引き寄せ中は ThrowController が自分で止める（キネマティックにする）ので、
        // ここでは「持ち主かどうか」だけで決める
        body.isKinematic = !IsOwner;

        if (!IsOwner)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    // ------------------------------------------------------------
    // 引っ掛ける（本人 → ホスト）
    // ------------------------------------------------------------

    /// <summary>
    /// 引っ掛けたいとホストへ頼む。<see cref="HookController"/> から呼ばれる。
    /// **早い者勝ちの判定はホストが行う**ので、断られることがある。
    /// </summary>
    public void RequestClaim(int playerIndex)
    {
        localClaimRevision = explosionRevision.Value;
        localClaimAttempt += 1;
        RequestClaimServerRpc(playerIndex, localClaimRevision, localClaimAttempt);
    }

    /// <summary>
    /// ホスト側の受け口。まだ誰も掛けていなければ、**持ち主をその人へ移す。**
    ///
    /// `RequireOwnership = false` は「持ち主でなくても呼んでよい」の意味。
    /// 引っ掛ける前の持ち主はホストなので、これが無いと参加者から頼めない。
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestClaimServerRpc(int playerIndex, uint claimRevision, uint claimAttempt,
        ServerRpcParams rpcParams = default)
    {
        if (claimRevision != explosionRevision.Value || IsClaimed || hookable.IsVanished)
        {
            // すでに誰かが掛けている。頼んできた人に断りを返す
            DenyClaimClientRpc(claimRevision, claimAttempt, RpcToSender(rpcParams));
            return;
        }

        hookedByPlayerIndex.Value = playerIndex;

        ulong sender = rpcParams.Receive.SenderClientId;
        if (sender != NetworkManager.ServerClientId)
        {
            NetworkObject.ChangeOwnership(sender);
        }
    }

    /// <summary>頼んだ本人だけに「取られていた」と伝える。</summary>
    [ClientRpc]
    private void DenyClaimClientRpc(uint claimRevision, uint claimAttempt, ClientRpcParams rpcParams = default)
    {
        // 爆発後に掛け直していたら、古い依頼への断りで新しい引き寄せを取り消さない。
        if (claimRevision != localClaimRevision || claimAttempt != localClaimAttempt)
        {
            return;
        }

        HookController hook = FindLocalHookController();
        if (hook != null)
        {
            hook.CancelAttachBecauseTaken(hookable);
        }
    }

    // ------------------------------------------------------------
    // 投げる・離す（本人 → ホスト）
    // ------------------------------------------------------------

    /// <summary>
    /// 投げをホストへ知らせる。<see cref="ThrowController"/> から呼ばれる。
    /// **力を加えるのはホスト。** そうしないと、どこへ飛んだかが人によって変わる。
    /// </summary>
    public void RequestThrow(Vector3 direction, float force, float lift, int playerIndex)
    {
        RequestThrowServerRpc(direction, force, lift, playerIndex, localClaimRevision);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestThrowServerRpc(Vector3 direction, float force, float lift,
        int playerIndex, uint claimRevision, ServerRpcParams rpcParams = default)
    {
        // 掛けている本人からの指示だけを受け付ける
        if (claimRevision != explosionRevision.Value || hookedByPlayerIndex.Value != playerIndex)
        {
            return;
        }

        TakeBackOwnership();

        hookedByPlayerIndex.Value = -1;
        lastThrownByPlayerIndex.Value = playerIndex;

        if (body != null && !body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.AddForce(direction * force + Vector3.up * lift, ForceMode.Impulse);
        }
    }

    /// <summary>
    /// 引っ掛けをやめる（ミスしたとき・爆発したとき）ことをホストへ知らせる。
    /// <paramref name="pullForce"/> だけプレイヤー側へ引き寄せて終わる。
    /// </summary>
    public void RequestRelease(Vector3 direction, float pullForce, int playerIndex)
    {
        RequestReleaseServerRpc(direction, pullForce, playerIndex, localClaimRevision);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestReleaseServerRpc(Vector3 direction, float pullForce,
        int playerIndex, uint claimRevision, ServerRpcParams rpcParams = default)
    {
        if (claimRevision != explosionRevision.Value || hookedByPlayerIndex.Value != playerIndex)
        {
            return;
        }

        TakeBackOwnership();
        hookedByPlayerIndex.Value = -1;

        if (body != null && !body.isKinematic && pullForce > 0f)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.AddForce(direction * pullForce, ForceMode.Impulse);
        }
    }

    /// <summary>
    /// **ほかの人のフックが当たったので、つかんでいる人の引っ掛けを外してほしい**とホストへ頼む。
    /// 宇宙ごみ式（<see cref="ThrowStyle.TwoButtons"/>）の <see cref="HookController"/> だけが呼ぶ（2026/9/22）。
    /// 釣りでは呼ばれないので、釣りの動きは変わらない。
    /// </summary>
    public void RequestInterfere(int playerIndex)
    {
        RequestInterfereServerRpc(playerIndex);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestInterfereServerRpc(int playerIndex)
    {
        // 誰もつかんでいない／自分でつかんでいるなら何もしない
        if (!IsClaimed || hookedByPlayerIndex.Value == playerIndex)
        {
            return;
        }

        Debug.Log($"[FISH] 参加番号 {playerIndex} のフックが当たったので、" +
                  $"参加番号 {hookedByPlayerIndex.Value} の引っ掛けを外しました。");

        // 爆風と同じ外し方をする。**力は加えない**（その場に落ちる）
        ServerApplyExplosion(Vector3.zero);
    }

    /// <summary>
    /// 爆風で脱鉤して吹き飛ばす。各PCの爆弾が呼んでもホストだけが処理する。
    /// ほかの人のフックが当たって外すとき（<see cref="RequestInterfere"/>）も、力 0 でここを使う。
    /// </summary>
    public void ServerApplyExplosion(Vector3 velocity)
    {
        if (!IsSpawned || !IsServer || hookable.IsVanished || body == null)
        {
            return;
        }

        explosionRevision.Value += 1;
        ReleaseForExplosionClientRpc(explosionRevision.Value);
        // ホストと専用サーバーはここで手元の状態を戻す。
        ThrowController.ReleaseTargetForExplosion(hookable);
        hookedByPlayerIndex.Value = -1;
        TakeBackOwnership();
        body.AddForce(velocity, ForceMode.VelocityChange);
    }

    [ClientRpc]
    private void ReleaseForExplosionClientRpc(uint releasedRevision)
    {
        // 爆発後の新しい鉤まで、遅れて届いた脱鉤の合図で外さない。
        if (!IsServer && localClaimRevision < releasedRevision)
        {
            ThrowController.ReleaseTargetForExplosion(hookable);
        }
    }

    /// <summary>ホストが持ち主を取り戻し、物理を自分で回せる状態にする。</summary>
    private void TakeBackOwnership()
    {
        if (NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
        {
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
        }

        ApplyPhysicsAuthority();
    }

    // ------------------------------------------------------------
    // 消す（ホスト → 全員）
    // ------------------------------------------------------------

    /// <summary>
    /// 消えてから、全員の画面から実際に削除するまでの秒数。
    ///
    /// **すぐ削除しない理由：** 爆発物は各PCが自分で導火線を数えて爆発する。
    /// ホストの爆発と同時に削除すると、少し遅れて数えている参加者の画面では
    /// **爆発する前に消えてしまい、スタンや爆発の見た目が出ない**ため。
    /// </summary>
    private const float DespawnDelaySeconds = 1f;

    private bool despawnScheduled;

    /// <summary>
    /// **ホストだけが呼ぶ。** 全員の画面で見た目を消し、少し待ってから削除する。
    /// <see cref="HookableObject.Vanish"/> から呼ばれる。
    /// </summary>
    public void ServerDespawnSoon()
    {
        if (!IsServer || despawnScheduled)
        {
            return;
        }

        despawnScheduled = true;
        hookedByPlayerIndex.Value = -1;
        HideClientRpc();
        StartCoroutine(DespawnLater());
    }

    private System.Collections.IEnumerator DespawnLater()
    {
        yield return new WaitForSeconds(DespawnDelaySeconds);

        if (IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
    }

    /// <summary>参加者の画面でも見た目を消す（ゴールに入ったときなど、ホストだけが判定した場合）。</summary>
    [ClientRpc]
    private void HideClientRpc()
    {
        if (!IsServer)
        {
            hookable.Vanish();
        }
    }

    /// <summary>ホストが点を入れたあとに、投げた人の記録を消す。</summary>
    public void ClearLastThrower()
    {
        if (IsServer)
        {
            lastThrownByPlayerIndex.Value = -1;
        }
    }

    // ------------------------------------------------------------
    // 補助
    // ------------------------------------------------------------

    private static ClientRpcParams RpcToSender(ServerRpcParams rpcParams)
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { rpcParams.Receive.SenderClientId }
            }
        };
    }

    /// <summary>このPCで自分が操作しているプレイヤーの HookController を探す。</summary>
    private static HookController FindLocalHookController()
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.IsOwner)
            {
                return player.GetComponent<HookController>();
            }
        }
        return null;
    }
}

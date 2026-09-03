using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **つながった・切れたを記録に残すだけ**の部品。
///
/// 展示当日に「参加できない」と言われたとき、
/// ログを見れば**どこまで進んで失敗したのか**が分かるようにしておくためのもの。
///
/// ゲームの動きには一切影響しない。
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class LanConnectionLogger : MonoBehaviour
{
    private NetworkManager manager;

    private void Awake()
    {
        manager = GetComponent<NetworkManager>();
    }

    private void OnEnable()
    {
        if (manager == null)
        {
            return;
        }

        manager.OnServerStarted += HandleServerStarted;
        manager.OnClientConnectedCallback += HandleClientConnected;
        manager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    private void OnDisable()
    {
        if (manager == null)
        {
            return;
        }

        manager.OnServerStarted -= HandleServerStarted;
        manager.OnClientConnectedCallback -= HandleClientConnected;
        manager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    private void HandleServerStarted()
    {
        Debug.Log("[LAN] ホストとして待ち受けを始めました");
    }

    private void HandleClientConnected(ulong clientId)
    {
        int count = manager.IsServer ? manager.ConnectedClientsIds.Count : -1;

        Debug.Log(count >= 0
            ? $"[LAN] 番号 {clientId} がつながりました（いま {count} 人）"
            : $"[LAN] つながりました（自分の番号：{clientId}）");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        string reason = string.IsNullOrEmpty(manager.DisconnectReason)
            ? "（理由の記載なし）"
            : manager.DisconnectReason;

        Debug.Log($"[LAN] 番号 {clientId} が切断されました {reason}");
    }
}

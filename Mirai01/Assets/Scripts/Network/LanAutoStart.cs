using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// **起動時の引数を見て、自動でホストまたは参加を始める。**
///
/// 展示当日、4台のPCで毎回ボタンを押して回るのは手間なので、
/// ショートカットに引数を書いておけば、起動しただけでつながるようにしてある。
///
/// 使い方（ショートカットの「リンク先」の末尾に足す）：
///
///   ゲーム.exe -host                     … このPCがホストになる
///   ゲーム.exe -client 192.168.0.10      … そのIPのホストへ参加する
///   ゲーム.exe -client 192.168.0.10 -port 7777
///
/// 引数が無ければ何もしない（画面のボタンで操作する）。
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class LanAutoStart : MonoBehaviour
{
    [Tooltip("引数が無いときでも、エディタでの再生時は自動でホストになる")]
    [SerializeField] private bool autoHostInEditor = false;

    /// <summary>引数で自動的に始めたか。画面表示の切り替えに使える。</summary>
    public bool StartedFromCommandLine { get; private set; }

    private void Start()
    {
        string[] args = System.Environment.GetCommandLineArgs();

        string address = null;
        ushort port = 0;
        bool wantHost = false;
        bool wantClient = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-host":
                    wantHost = true;
                    break;

                case "-client":
                    wantClient = true;
                    if (i + 1 < args.Length)
                    {
                        address = args[i + 1];
                    }
                    break;

                case "-port":
                    if (i + 1 < args.Length && ushort.TryParse(args[i + 1], out ushort parsed))
                    {
                        port = parsed;
                    }
                    break;
            }
        }

        if (!wantHost && !wantClient)
        {
            if (autoHostInEditor && Application.isEditor)
            {
                StartAs(true, null, 0);
            }

            return;
        }

        StartAs(wantHost, address, port);
    }

    private void StartAs(bool asHost, string address, ushort port)
    {
        NetworkManager manager = GetComponent<NetworkManager>();
        var transport = manager.NetworkConfig.NetworkTransport as UnityTransport;

        if (transport == null)
        {
            Debug.LogError("UnityTransport が見つかりません。自動接続を中止します。");
            return;
        }

        string useAddress = string.IsNullOrEmpty(address)
            ? transport.ConnectionData.Address
            : address;

        ushort usePort = port == 0 ? transport.ConnectionData.Port : port;

        transport.SetConnectionData(useAddress, usePort, asHost ? "0.0.0.0" : null);

        bool started = asHost ? manager.StartHost() : manager.StartClient();
        StartedFromCommandLine = true;

        Debug.Log(started
            ? $"[LAN] 起動時の引数で {(asHost ? "ホスト" : "参加")} を開始しました（{useAddress}:{usePort}）"
            : $"[LAN] 開始に失敗しました（{useAddress}:{usePort}）");
    }
}

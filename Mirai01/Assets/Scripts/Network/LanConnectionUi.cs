using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// LANでつなぐための、**仮の操作画面**。
///
/// 画面左上に「ホストで開始」「参加」のボタンと、つながっている人数を出す。
/// **このPCのIPアドレスも出す**ので、他のPCの人はそれを見て入力すればよい。
///
/// これは通信の土台を確かめるための簡易表示で、
/// **本番のタイトル画面はあとで作り直す**前提のもの。
/// （見た目を作り込むより、確実に動いて状態が見えることを優先している）
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class LanConnectionUi : MonoBehaviour
{
    [Tooltip("参加するときの、つなぎ先のIPアドレス。同じPCで試すなら 127.0.0.1")]
    [SerializeField] private string address = "127.0.0.1";

    [Tooltip("使うポート番号。4台とも同じ値にすること")]
    [SerializeField] private ushort port = 7777;

    [Tooltip("画面表示の大きさ。展示用の大きい画面では上げる")]
    [Range(1f, 4f)]
    [SerializeField] private float uiScale = 1.5f;

    [Tooltip("OFFにすると画面に何も出さなくなる（本番の画面ができたら切る）")]
    [SerializeField] private bool showUi = true;

    private NetworkManager manager;
    private string myAddresses = "調べています…";

    private void Awake()
    {
        manager = GetComponent<NetworkManager>();

        // 2台目以降のウィンドウが裏に回っても通信を続けるために必要
        Application.runInBackground = true;

        myAddresses = CollectLocalAddresses();
    }

    private void OnGUI()
    {
        if (!showUi)
        {
            return;
        }

        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

        GUILayout.BeginArea(new Rect(12f, 12f, 320f, 400f), GUI.skin.box);

        if (manager == null)
        {
            GUILayout.Label("NetworkManager が見つかりません");
        }
        else if (!manager.IsClient && !manager.IsServer)
        {
            DrawDisconnected();
        }
        else
        {
            DrawConnected();
        }

        GUILayout.EndArea();
        GUI.matrix = saved;
    }

    /// <summary>まだつながっていないときの表示。</summary>
    private void DrawDisconnected()
    {
        GUILayout.Label("■ LANでつなぐ");
        GUILayout.Space(4f);

        GUILayout.Label($"このPCのIP：\n{myAddresses}");
        GUILayout.Space(4f);

        if (GUILayout.Button("ホストで開始（この1台が親）"))
        {
            Apply("0.0.0.0");
            manager.StartHost();
        }

        GUILayout.Space(8f);
        GUILayout.Label("参加する（親のIPを入れる）");

        address = GUILayout.TextField(address);

        GUILayout.BeginHorizontal();
        GUILayout.Label("ポート", GUILayout.Width(50f));
        string portText = GUILayout.TextField(port.ToString());
        if (ushort.TryParse(portText, out ushort parsed))
        {
            port = parsed;
        }
        GUILayout.EndHorizontal();

        if (GUILayout.Button("参加する"))
        {
            Apply(null);
            manager.StartClient();
        }
    }

    /// <summary>つながっているときの表示。</summary>
    private void DrawConnected()
    {
        string role = manager.IsHost ? "ホスト（親）" : manager.IsServer ? "サーバー" : "参加者";

        GUILayout.Label($"■ {role} として動作中");
        GUILayout.Label($"自分の番号：{manager.LocalClientId}");

        if (manager.IsServer)
        {
            GUILayout.Label($"つながっている人数：{manager.ConnectedClientsIds.Count} 人");

            StringBuilder builder = new StringBuilder("番号：");
            foreach (ulong id in manager.ConnectedClientsIds)
            {
                builder.Append(id).Append(' ');
            }
            GUILayout.Label(builder.ToString());
        }
        else
        {
            GUILayout.Label(manager.IsConnectedClient ? "接続できています" : "接続中…");
        }

        GUILayout.Space(8f);

        if (GUILayout.Button("切断する"))
        {
            manager.Shutdown();
        }
    }

    /// <summary>入力されたIPとポートを通信部品に反映する。</summary>
    private void Apply(string listenAddress)
    {
        if (manager.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            transport.SetConnectionData(address, port, listenAddress);
        }
    }

    /// <summary>
    /// このPCが持っているIPアドレスを集める。
    /// 他のPCの人が、ここに出た数字を入力して参加する。
    /// </summary>
    private static string CollectLocalAddresses()
    {
        List<string> found = new List<string>();

        try
        {
            foreach (IPAddress ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    found.Add(ip.ToString());
                }
            }
        }
        catch (System.Exception error)
        {
            return $"取得できませんでした（{error.GetType().Name}）";
        }

        return found.Count > 0 ? string.Join("\n", found) : "見つかりませんでした";
    }
}

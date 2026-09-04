using System.Text;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// つながっている間、**通信の様子を画面に出す**表示。
///
/// 一番大事なのは **遅れ（ping）** の数字。
/// 「4人で対戦して成立するか」は、**この数字を見ないと感覚の話になってしまう。**
///
/// 目安：
///   〜30ms   … ほぼ気にならない（LANはだいたいここ）
///   30〜80ms … 対戦でも成立する。判定がシビアな遊びだと気になり始める
///   80ms〜   … 遅れが分かる。当たり判定の作り方を考える必要がある
///
/// これは確認用の表示なので、**本番の画面には出さない**（Show Hud を切る）。
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class NetworkStatusHud : MonoBehaviour
{
    [Tooltip("OFFにすると何も表示しない（本番では切る）")]
    [SerializeField] private bool showHud = true;

    [Tooltip("画面表示の大きさ")]
    [Range(1f, 4f)]
    [SerializeField] private float uiScale = 1.5f;

    [Tooltip("表示を更新する間隔（秒）。細かすぎると読めないので少し間を置く")]
    [Range(0.05f, 1f)]
    [SerializeField] private float refreshSeconds = 0.25f;

    private NetworkManager manager;
    private string cachedText = string.Empty;
    private float nextRefreshTime;

    private void Awake()
    {
        manager = GetComponent<NetworkManager>();
    }

    private void Update()
    {
        if (!showHud || manager == null || Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime = Time.unscaledTime + refreshSeconds;
        cachedText = BuildText();
    }

    private void OnGUI()
    {
        if (!showHud || string.IsNullOrEmpty(cachedText))
        {
            return;
        }

        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

        float width = 300f;
        float x = (Screen.width / uiScale) - width - 12f;

        GUILayout.BeginArea(new Rect(x, 12f, width, 320f), GUI.skin.box);
        GUILayout.Label(cachedText);
        GUILayout.EndArea();

        GUI.matrix = saved;
    }

    /// <summary>表示する文章を組み立てる。</summary>
    private string BuildText()
    {
        if (!manager.IsClient && !manager.IsServer)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder();

        builder.AppendLine("■ 通信の様子");
        builder.AppendLine();

        AppendPing(builder);
        builder.AppendLine();
        AppendScores(builder);

        return builder.ToString();
    }

    /// <summary>遅れ（往復にかかる時間）を出す。</summary>
    private void AppendPing(StringBuilder builder)
    {
        NetworkTransport transport = manager.NetworkConfig.NetworkTransport;

        if (transport == null)
        {
            return;
        }

        if (manager.IsServer)
        {
            builder.AppendLine("遅れ（ホストから見た各自）");

            foreach (ulong id in manager.ConnectedClientsIds)
            {
                if (id == manager.LocalClientId)
                {
                    builder.AppendLine($"  番号 {id}（自分）： ―");
                    continue;
                }

                builder.AppendLine($"  番号 {id}： {transport.GetCurrentRtt(id)} ms");
            }
        }
        else
        {
            ulong rtt = transport.GetCurrentRtt(NetworkManager.ServerClientId);
            builder.AppendLine($"ホストまでの遅れ： {rtt} ms");
            builder.AppendLine($"  {DescribeRtt(rtt)}");
        }
    }

    /// <summary>数字だけだと判断できないので、言葉でも添える。</summary>
    private static string DescribeRtt(ulong rtt)
    {
        if (rtt <= 30)
        {
            return "ほぼ気にならない";
        }

        if (rtt <= 80)
        {
            return "対戦でも成立する範囲";
        }

        return "遅れが分かる。作りを考える必要あり";
    }

    /// <summary>全員の得点を出す。参加者側でも、同じ値が見えているはず。</summary>
    private static void AppendScores(StringBuilder builder)
    {
        builder.AppendLine("得点（ホストが決めた値）");

        if (PlayerScore.All.Count == 0)
        {
            builder.AppendLine("  まだ誰もいません");
            return;
        }

        foreach (PlayerScore score in PlayerScore.All)
        {
            if (score == null)
            {
                continue;
            }

            string mark = score.IsOwner ? " ←自分" : string.Empty;
            builder.AppendLine($"  番号 {score.OwnerClientId}： {score.Score} 点{mark}");
        }
    }
}

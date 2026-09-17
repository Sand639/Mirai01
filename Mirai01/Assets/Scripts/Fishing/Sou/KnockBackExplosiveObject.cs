using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 導火線・見た目・物資の消滅は通常の爆弾と同じ。
/// プレイヤーにはスタンを与えず、爆弾から離れる向きに吹き飛ばす。
/// ExplosiveObject を継承するため、既存のフック・通信・カウントダウンがそのまま使える。
/// </summary>
public class KnockBackExplosiveObject : ExplosiveObject
{
    [Header("吹き飛ばす強さ（スタンなし）")]
    [Tooltip("横方向に吹き飛ばす速さ（1秒あたりのメートル）")]
    [Min(0f)]
    [SerializeField] private float knockbackSpeed = 10f;

    [Tooltip("上向きに飛ばす速さ。0なら横方向だけ（1秒あたりのメートル）")]
    [Min(0f)]
    [SerializeField] private float knockbackLift = 5f;

    protected override void ApplyPlayerEffect(Collider hit, HashSet<GameObject> handled)
    {
        FishingPlayerController player = hit.GetComponentInParent<FishingPlayerController>();
        if (player == null || !player.isActiveAndEnabled || !handled.Add(player.gameObject))
        {
            return;
        }

        Vector3 direction = player.transform.position - transform.position;
        direction.y = 0f;
        // 真上で爆発した場合も、止まらずプレイヤーの前方へ飛ばす。
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = player.transform.forward;
            direction.y = 0f;
        }
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector3.forward;
        }

        player.Launch(direction.normalized * knockbackSpeed + Vector3.up * knockbackLift);
    }
}

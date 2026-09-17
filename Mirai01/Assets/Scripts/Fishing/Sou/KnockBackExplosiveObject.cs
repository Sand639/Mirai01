using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 導火線・見た目は通常の爆弾と同じ。
/// プレイヤーにはスタンを与えず、物資も消さずに爆弾から離れる向きに吹き飛ばす。
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

    [Header("物資を吹き飛ばす強さ")]
    [Tooltip("鉤で引っ掛けられる物資に加える横方向の速さ（m/s）。重さによらず同じ勢いを加える")]
    [Min(0f)]
    [SerializeField] private float objectKnockbackSpeed = 12f;

    [Tooltip("物資に加える上方向の速さ（m/s）。0なら横方向だけ")]
    [Min(0f)]
    [SerializeField] private float objectKnockbackLift = 6f;

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

    protected override void ApplyHookableEffect(HookableObject other)
    {
        if (other.IsVanished || other.Body == null)
        {
            return;
        }

        Vector3 direction = other.Body.worldCenterOfMass - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector3.forward;
        }
        Vector3 velocity = direction.normalized * objectKnockbackSpeed
                         + Vector3.up * objectKnockbackLift;

        FishingNetSupply netSupply = other.GetComponent<FishingNetSupply>();
        if (netSupply != null && netSupply.IsSpawned)
        {
            // 爆発は各PCで起きるが、物資を飛ばすのはホストだけ。
            netSupply.ServerApplyExplosion(velocity);
            return;
        }

        ThrowController.ReleaseTargetForExplosion(other);
        other.Body.isKinematic = false;
        other.Body.AddForce(velocity, ForceMode.VelocityChange);
    }
}

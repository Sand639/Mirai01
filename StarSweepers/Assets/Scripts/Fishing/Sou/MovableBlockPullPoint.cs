using UnityEngine;

/// <summary>MovableBlock 四周其中一個可被 Hook 勾住的拉點。</summary>
[RequireComponent(typeof(BoxCollider))]
public class MovableBlockPullPoint : MonoBehaviour, IHookPullable
{
    [SerializeField] private MovableBlock block;
    [SerializeField] private MovableBlockPullSide side = MovableBlockPullSide.Front;

    private bool isHooked;

    public Component HookComponent => this;
    public Vector3 HookAnchorPoint => transform.position;
    public bool IsHooked => isHooked;
    public bool CanBeHooked
    {
        get
        {
            EnsureBlock();
            return !isHooked && block != null && !block.IsMoving;
        }
    }

    private void Awake()
    {
        if (block == null)
        {
            EnsureBlock();
        }

        Collider pullCollider = GetComponent<Collider>();
        pullCollider.isTrigger = true;
    }

    public void SetHooked(bool hooked)
    {
        isHooked = hooked;
    }

    public void CompletePull(HookPullContext context)
    {
        EnsureBlock();
        if (block != null)
        {
            block.Pull(context.PlayerPosition, side);
        }

        isHooked = false;
    }

    private void EnsureBlock()
    {
        if (block == null)
        {
            block = GetComponentInParent<MovableBlock>();
        }
    }
}

using UnityEngine;
using TarodevController; // your namespace that contains PlayerAnimator

public class AnimationEventRelay : MonoBehaviour
{
    [SerializeField] private PlayerAnimator _playerAnimator;

    private void Reset()
    {
        // Auto-find on the parent that holds PlayerAnimator (Visual)
        if (_playerAnimator == null) _playerAnimator = GetComponentInParent<PlayerAnimator>();
    }

    // Animation Event function name (no params, public void)
    public void AnimEvent_MeleeHit()
    {
        _playerAnimator?.AnimEvent_MeleeHit();
    }
}

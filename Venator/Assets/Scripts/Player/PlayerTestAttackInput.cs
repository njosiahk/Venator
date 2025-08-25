using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerTestAttackInput : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputActionReference attackAction;
    [Tooltip("Optional: your Move action so we can remember last non-zero X.")]
    [SerializeField] private InputActionReference moveAction;

    [Header("Hit Test")]
    [SerializeField] private float range = 2f;
    [SerializeField] private LayerMask hittableLayers = ~0;
    [SerializeField] private Collider2D referenceCollider;

    [Header("Facing Sources (all optional, we auto-fall back)")]
    [Tooltip("If your art flips with SpriteRenderer.flipX, drag it here.")]
    [SerializeField] private SpriteRenderer sprite;
    [Tooltip("If you flip by scaling a transform (e.g., sprite root), drag it here.")]
    [SerializeField] private Transform facingTransform;
    [Tooltip("If true, flipX = LEFT; if false, flipX = RIGHT. Toggle if direction is inverted.")]
    [SerializeField] private bool flipXFacesLeft = true;

    [Header("Ray Origin Offset (local)")]
    [SerializeField] private Vector2 attackOriginOffset = new(0.8f, 0.15f);

    private float lastMoveSign = 1f;

    void OnEnable()
    {
        if (attackAction != null)
        {
            attackAction.action.performed += OnAttackPerformed;
            if (!attackAction.action.enabled) attackAction.action.Enable();
        }
        if (moveAction != null)
        {
            moveAction.action.performed += OnMove;
            moveAction.action.canceled += OnMove;
            if (!moveAction.action.enabled) moveAction.action.Enable();
        }
    }

    void OnDisable()
    {
        if (attackAction != null)
        {
            attackAction.action.performed -= OnAttackPerformed;
            attackAction.action.Disable();
        }
        if (moveAction != null)
        {
            moveAction.action.performed -= OnMove;
            moveAction.action.canceled -= OnMove;
            moveAction.action.Disable();
        }
    }

    void OnMove(InputAction.CallbackContext ctx)
    {
        // Supports both Vector2 (WASD) and 1D axis composites.
        float x;
        if (ctx.valueType == typeof(Vector2)) x = ctx.ReadValue<Vector2>().x;
        else if (ctx.valueType == typeof(float)) x = ctx.ReadValue<float>();
        else x = 0f;

        if (Mathf.Abs(x) > 0.01f) lastMoveSign = Mathf.Sign(x);
    }

    void OnAttackPerformed(InputAction.CallbackContext ctx)
    {
        Vector2 dir = GetFacingDir();
        Vector2 origin = GetAttackOrigin(dir);

        Debug.DrawRay(origin, dir * range, Color.yellow, 0.25f);

        var hit = Physics2D.Raycast(origin, dir, range, hittableLayers);
        if (hit.collider && hit.collider.TryGetComponent(out EnemyCombatant enemy))
        {
            ushort meleeId = Combat.DamageSourceId.Melee_Knife;
            var payload = Combat.HitPayload.Melee(
                owner: transform,
                id: meleeId,
                charged: false,      // Step 4 will toggle this
                dir: GetFacingDir(),
                hp: 1,
                posture: 0f
            );
            enemy.ReceiveHit(payload);
        }
    }

    Vector2 GetAttackOrigin(Vector2 dir)
    {
        Vector2 center = referenceCollider ? (Vector2)referenceCollider.bounds.center
                                           : (Vector2)transform.position;
        return center + new Vector2(attackOriginOffset.x * Mathf.Sign(dir.x),
                                    attackOriginOffset.y);
    }

    Vector2 GetFacingDir()
    {
        // 1) Prefer SpriteRenderer.flipX (most common in 2D)
        if (sprite != null)
        {
            bool left = sprite.flipX == flipXFacesLeft;
            return left ? Vector2.left : Vector2.right;
        }
        // 2) Next, try transform scale (use lossyScale to handle parent flips)
        if (facingTransform != null)
        {
            float sx = facingTransform.lossyScale.x;
            if (Mathf.Abs(sx) > 0.0001f) return sx >= 0 ? Vector2.right : Vector2.left;
        }
        // 3) Then this GameObject's scale
        float selfX = transform.lossyScale.x;
        if (Mathf.Abs(selfX) > 0.0001f) return selfX >= 0 ? Vector2.right : Vector2.left;

        // 4) Fallback: last non-zero move input sign
        return lastMoveSign >= 0 ? Vector2.right : Vector2.left;
    }

    void OnDrawGizmosSelected()
    {
        Vector2 dir = Application.isPlaying ? GetFacingDir() : Vector2.right;
        Vector2 origin = GetAttackOrigin(dir);
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(origin, 0.05f);
        Gizmos.DrawLine(origin, origin + dir * range);
    }
}

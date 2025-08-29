using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Combat;

/*
 * PlayerMeleeHitbox (no PlayerInput)
 * - Measures hold (started/canceled) on Attack (Button).
 * - When hold crosses weapon.chargeThreshold → disables Move + any extra actions you list.
 * - Restores actions on release. Does NOT touch Rigidbody2D velocity (momentum preserved).
 * - Perfect timing uses WeaponStats.perfectStart/perfectEnd.
 */
public class PlayerMeleeHitbox : MonoBehaviour
{
    [Header("Input (New Input System)")]
    [Tooltip("Attack action (Z). Action Type: Button.")]
    [SerializeField] private InputActionReference attackAction;
    [SerializeField] private TarodevController.PlayerController playerController; // drag your PlayerController here


    [Tooltip("Move action used by your controller. We disable it while charging.")]
    [SerializeField] private InputActionReference moveAction;

    [Tooltip("Optional: other actions to disable while charging (Jump/Roll/Crouch…).")]
    [SerializeField] private InputActionReference[] actionsToSuppressWhileCharging;

    [Header("Facing (choose one)")]
    [SerializeField] private SpriteRenderer sprite;     // flipX path
    [SerializeField] private Transform facingTransform; // scale.x path
    [SerializeField] private bool flipXFacesLeft = true;

    [Header("Weapon")]
    [SerializeField] private WeaponStats weapon;        // Fists stats

    [Header("Geometry Reference")]
    [SerializeField] private Collider2D referenceCollider;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    // --- internal ---
    private bool _attacking;
    private bool _holding;
    private bool _chargeLockActive;
    private float _pressStartTime;
    private float _lastMoveSign = 1f;

    private Collider2D[] _hits;
    private int[] _processedRootIds;

    void Awake()
    {
        int cap = 16;
        _hits = new Collider2D[cap];
        _processedRootIds = new int[cap * 2];
    }

    void OnEnable()
    {
        // Hook Attack started/canceled so we can measure hold
        if (attackAction != null)
        {
            attackAction.action.started += OnAttackStarted;
            attackAction.action.canceled += OnAttackCanceled;
            if (!attackAction.action.enabled) attackAction.action.Enable();
        }
        // Listen to Move just to remember last facing when idle (optional)
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
            attackAction.action.started -= OnAttackStarted;
            attackAction.action.canceled -= OnAttackCanceled;
            attackAction.action.Disable();
        }
        if (moveAction != null)
        {
            moveAction.action.performed -= OnMove;
            moveAction.action.canceled -= OnMove;
            // leave it enabled; we only disabled it during charge
        }
        if (_chargeLockActive) RestoreSuppressedActions();
        _holding = false;
        _chargeLockActive = false;
    }

    // ---------------- INPUT ----------------
    private void OnAttackStarted(InputAction.CallbackContext _)
    {
        if (_attacking) return;
        _holding = true;
        _pressStartTime = Time.time;
        _chargeLockActive = false;
        StartCoroutine(ChargeMonitor());
    }

    private void OnAttackCanceled(InputAction.CallbackContext _)
    {
        if (!_holding) return;
        _holding = false;

        float heldFor = Time.time - _pressStartTime;
        bool charged = weapon != null && heldFor >= weapon.chargeThreshold;

        // Restore controls if we locked them during charge
        if (_chargeLockActive) { RestoreSuppressedActions(); _chargeLockActive = false; }

        // Perfect window: [perfectStart, perfectEnd] since press
        bool isPerfect = charged && weapon.hasPerfectCharge &&
                         heldFor >= weapon.perfectStart &&
                         heldFor <= weapon.perfectEnd;

        if (!_attacking) StartCoroutine(AttackRoutine(charged, isPerfect));
    }

    private void OnMove(InputAction.CallbackContext ctx)
    {
        float x;
        if (ctx.valueType == typeof(Vector2)) x = ctx.ReadValue<Vector2>().x;
        else if (ctx.valueType == typeof(float)) x = ctx.ReadValue<float>();
        else x = 0f;
        if (Mathf.Abs(x) > 0.01f) _lastMoveSign = Mathf.Sign(x);
    }

    private void SuppressActions()
    {
        if (playerController != null) playerController.InputLocked = true;  // lock steering
    }

    private void RestoreSuppressedActions()
    {
        if (playerController != null) playerController.InputLocked = false; // unlock steering
    }


    private IEnumerator ChargeMonitor()
    {
        if (weapon == null) yield break;
        while (_holding && !_chargeLockActive)
        {
            if (Time.time - _pressStartTime >= weapon.chargeThreshold)
            {
                SuppressActions();        // lock Movement (and others you added)
                _chargeLockActive = true;
                break;
            }
            yield return null;
        }
    }

    // -------------- ATTACK -----------------
    private IEnumerator AttackRoutine(bool charged, bool perfect)
    {
        _attacking = true;

        float windup = charged ? weapon.windupCharged : weapon.windupLight;
        if (windup > 0f) yield return new WaitForSeconds(windup);

        Vector2 dir = GetFacingDir();
        Vector2 center = GetHitboxCenter(dir, weapon.boxOffset);
        Vector2 size = weapon.boxSize;

        int count = Physics2D.OverlapBoxNonAlloc(center, size, 0f, _hits, weapon.hittableLayers);

        for (int i = 0; i < _processedRootIds.Length; i++) _processedRootIds[i] = 0;
        int processed = 0;

        int hp = charged ? weapon.hpDamageCharged : weapon.hpDamageLight;
        float post = charged ? weapon.postureDamageCharged : weapon.postureDamageLight;

        DamageTags extra = charged ? DamageTags.Charged : DamageTags.None;
        if (perfect)
        {
            extra |= DamageTags.Perfect;
            if (weapon.perfectOverridesDamage)
            {
                hp = weapon.hpDamagePerfect;
                post = weapon.postureDamagePerfect;
            }
        }

        for (int i = 0; i < count; i++)
        {
            var col = _hits[i];
            if (!col) continue;
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;

            var receiver = col.GetComponentInParent<IHitReceiver>();
            if (receiver == null) continue;

            int rootId = col.transform.root.GetInstanceID();
            bool seen = false;
            for (int j = 0; j < processed; j++) if (_processedRootIds[j] == rootId) { seen = true; break; }
            if (seen) continue;
            _processedRootIds[processed++] = rootId;

            var payload = HitPayload.Melee(
                owner: transform,
                id: weapon.meleeSourceId,
                charged: charged,
                dir: dir,
                hp: hp,
                posture: post
            );
            payload.tags |= (extra & DamageTags.Perfect);
            receiver.ReceiveHit(payload);
        }

        if (weapon.recovery > 0f) yield return new WaitForSeconds(weapon.recovery);
        _attacking = false;
    }

    // -------------- HELPERS ----------------
    private Vector2 GetFacingDir()
    {
        if (sprite != null)
        {
            bool left = sprite.flipX == flipXFacesLeft;
            return left ? Vector2.left : Vector2.right;
        }
        if (facingTransform != null)
        {
            float sx = facingTransform.lossyScale.x;
            if (Mathf.Abs(sx) > 0.0001f) return sx >= 0 ? Vector2.right : Vector2.left;
        }
        float selfX = transform.lossyScale.x;
        if (Mathf.Abs(selfX) > 0.0001f) return selfX >= 0 ? Vector2.right : Vector2.left;
        return _lastMoveSign >= 0 ? Vector2.right : Vector2.left;
    }

    private Vector2 GetHitboxCenter(Vector2 dir, Vector2 offset)
    {
        Vector2 basePos = referenceCollider ? (Vector2)referenceCollider.bounds.center
                                            : (Vector2)transform.position;
        return basePos + new Vector2(offset.x * Mathf.Sign(dir.x), offset.y);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos || weapon == null) return;
        Vector2 dir = Application.isPlaying ? GetFacingDir() : Vector2.right;
        Vector2 center = GetHitboxCenter(dir, weapon.boxOffset);
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(center, weapon.boxSize);
    }
}

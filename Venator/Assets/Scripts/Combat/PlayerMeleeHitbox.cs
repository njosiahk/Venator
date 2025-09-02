using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Combat;                    // HitPayload / DamageTags / WeaponStats
using TarodevController;        // PlayerController + your PlayerAnimator

/*
 * PlayerMeleeHitbox
 * -----------------
 * Press:
 *   - Fires Animator trigger "AttackStart" via PlayerAnimator.OnMeleeStartup()
 *   - Begins watching for chargeThreshold; once crossed → lock movement + set Charging=true
 *
 * Release:
 *   - Computes Charged/Perfect from hold time
 *   - Exits charge loop (Charging=false), triggers "AttackRelease" via PlayerAnimator.OnMeleeRelease()
 *   - Actual damage is applied on the Attack_Main clip's Animation Event → AnimEvent_MeleeHit()
 *     which calls back into this script via PlayerAnimator → Animation_Hit()
 *   - If you don't add an anim event, enable "useAnimationEvent=false" to use a small timed fallback
 *
 * Movement Lock:
 *   - We DO NOT disable InputActions. We toggle PlayerController.InputLocked while charging.
 *   - Momentum is preserved (we never touch Rigidbody2D velocity).
 */

public class PlayerMeleeHitbox : MonoBehaviour
{
    // ---------- Input ----------
    [Header("Input")]
    [Tooltip("Attack action (Z). Action Type should be Button.")]
    [SerializeField] private InputActionReference attackAction;

    // ---------- Facing ----------
    [Header("Facing (choose one)")]
    [Tooltip("Assign if you flip with SpriteRenderer.flipX.")]
    [SerializeField] private SpriteRenderer sprite;
    [Tooltip("Assign if you flip by scaling a transform (localScale.x).")]
    [SerializeField] private Transform facingTransform;
    [Tooltip("If true, flipX means LEFT; toggle if your art is inverted.")]
    [SerializeField] private bool flipXFacesLeft = true;

    // ---------- Weapon & Geometry ----------
    [Header("Weapon & Hitbox")]
    [Tooltip("Per-weapon stats (timings, damages, hitbox shape, layers).")]
    [SerializeField] private WeaponStats weapon;            // Fists asset for now
    [Tooltip("If set, hitbox centers on this collider's bounds; else on transform.position.")]
    [SerializeField] private Collider2D referenceCollider;

    // ---------- Animation Bridge ----------
    [Header("Animation")]
    [Tooltip("Your existing PlayerAnimator that sets Animator params and forwards anim events.")]
    [SerializeField] private PlayerAnimator _playerAnimator;
    [Tooltip("Apply damage on the animation event (recommended). If false, use small timed delay.")]
    [SerializeField] private bool useAnimationEvent = true;
    [Tooltip("Fallback delay if not using an anim event (seconds).")]
    [SerializeField, Min(0f)] private float fallbackHitDelayLight = 0.06f;
    [SerializeField, Min(0f)] private float fallbackHitDelayCharged = 0.10f;

    // --- Safety if the anim event is skipped (roll/fall/transition) ---
    [SerializeField, Tooltip("If the hit event doesn't arrive within this time after release, we auto-resolve.")]
    private float impactEventTimeout = 0.25f;

    [SerializeField, Tooltip("If true we still apply the hit on timeout; if false we cancel it.")]
    private bool applyHitOnTimeout = false;

    private Coroutine _impactGuard;   // watchdog handle


    // ---------- Movement Lock ----------
    [Header("Movement Lock")]
    [Tooltip("Your Tarodev PlayerController; we toggle InputLocked while CHARGING.")]
    [SerializeField] private PlayerController playerController;

    // ---------- Debug ----------
    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    // ---------- Internal state ----------
    private bool _attacking;            // true from press until recovery completes
    private bool _holding;              // Attack currently held
    private bool _chargeLockActive;     // we've crossed threshold and locked controls
    private float _pressStartTime;       // Time.time when Attack started
    private float _lastMoveSign = 1f;    // fallback facing if no sprite/transform provided

    // Reusable buffers (alloc-free OverlapBox)
    private readonly Collider2D[] _hits = new Collider2D[16];
    private readonly int[] _processedRootIds = new int[32];

    // Pending-attack context (computed on release; consumed on Animation_Hit)
    private bool _pendingHasAttack;
    private bool _pendingCharged;
    private bool _pendingPerfect;
    private int _pendingHP;
    private float _pendingPosture;

    // ===================== Unity hooks =====================

    void OnEnable()
    {
        if (attackAction != null)
        {
            attackAction.action.started += OnAttackStarted;   // button down
            attackAction.action.canceled += OnAttackCancelled;  // button up
            if (!attackAction.action.enabled) attackAction.action.Enable();
        }
    }

    void OnDisable()
    {
        if (attackAction != null)
        {
            attackAction.action.started -= OnAttackStarted;
            attackAction.action.canceled -= OnAttackCancelled;
            attackAction.action.Disable();
        }
        // Safety: if disabled mid-charge, restore everything
        if (_chargeLockActive) UnlockControls();
        _playerAnimator?.OnMeleeChargingChanged(false);
        _holding = false;
        _attacking = false;
        _chargeLockActive = false;
        _pendingHasAttack = false;
    }

    // ===================== Input flow =====================

    // Button DOWN
    private void OnAttackStarted(InputAction.CallbackContext _)
    {
        if (_attacking) return; // ignore if mid-attack

        _attacking = true;
        _holding = true;
        _pressStartTime = Time.time;
        _chargeLockActive = false;

        // Play the quick startup clip now
        _playerAnimator?.OnMeleeStartup();

        // Begin watching for the charge threshold; once crossed we lock steering + show charge loop
        StartCoroutine(ChargeMonitor());
    }

    // Button UP
    private void OnAttackCancelled(InputAction.CallbackContext _)
    {
        if (!_holding) return;
        _holding = false;

        float heldFor = Time.time - _pressStartTime;
        bool charged = weapon != null && heldFor >= weapon.chargeThreshold;
        bool perfect = charged && weapon.hasPerfectCharge &&
                        heldFor >= weapon.perfectStart && heldFor <= weapon.perfectEnd;

        if (_chargeLockActive) { UnlockControls(); _chargeLockActive = false; }
        _playerAnimator?.OnMeleeChargingChanged(false);

        ResolvePendingAttack(charged, perfect);
        _playerAnimator?.OnMeleeRelease(charged, perfect);

        if (!useAnimationEvent)
        {
            float delay = charged ? fallbackHitDelayCharged : fallbackHitDelayLight;
            StartCoroutine(FallbackHitAfter(delay));
        }
        else
        {
            // Guard: if the event never arrives, resolve after a short timeout
            if (_impactGuard != null) StopCoroutine(_impactGuard);
            _impactGuard = StartCoroutine(ImpactTimeoutGuard());
        }
    }

    // Watches for charge threshold crossing; once crossed, enter "charging" (locks steering)
    private IEnumerator ChargeMonitor()
    {
        if (weapon == null) yield break;
        while (_holding && !_chargeLockActive)
        {
            if (Time.time - _pressStartTime >= weapon.chargeThreshold)
            {
                LockControls();
                _chargeLockActive = true;
                _playerAnimator?.OnMeleeChargingChanged(true); // enter Charge_Loop
                break; // remain in loop until release
            }
            yield return null;
        }
    }

    private void LockControls() { if (playerController != null) playerController.InputLocked = true; }
    private void UnlockControls() { if (playerController != null) playerController.InputLocked = false; }

    // ===================== Attack core =====================

    // Compute the numbers once (on release); actual hit occurs later (anim event / fallback)
    private void ResolvePendingAttack(bool charged, bool perfect)
    {
        _pendingCharged = charged;
        _pendingPerfect = perfect;

        int hp = charged ? weapon.hpDamageCharged : weapon.hpDamageLight;
        float post = charged ? weapon.postureDamageCharged : weapon.postureDamageLight;

        if (perfect && weapon.perfectOverridesDamage)
        {
            hp = weapon.hpDamagePerfect;
            post = weapon.postureDamagePerfect;
        }

        _pendingHP = hp;
        _pendingPosture = post;
        _pendingHasAttack = true;
    }

    // Animation Event entry point (set this on the Attack_Main clip via PlayerAnimator.AnimEvent_MeleeHit)
    public void Animation_Hit()
    {
        if (!_pendingHasAttack) return;
        if (_impactGuard != null) { StopCoroutine(_impactGuard); _impactGuard = null; } // event arrived → stop guard
        DoHit(_pendingCharged, _pendingPerfect, _pendingHP, _pendingPosture);
        _pendingHasAttack = false;
        StartCoroutine(RecoveryThenReady());
    }


    // Fallback timing if you haven't added the animation event yet
    private IEnumerator FallbackHitAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        Animation_Hit(); // reuse the same path
    }

    private IEnumerator ImpactTimeoutGuard()
    {
        float deadline = Time.time + impactEventTimeout;
        while (Time.time < deadline && _pendingHasAttack) yield return null;

        if (_pendingHasAttack)
        {
            if (applyHitOnTimeout)
            {
                // still apply the hit at current pose
                Animation_Hit();
            }
            else
            {
                // cancel the hit and just finish recovery so the player can attack again
                _pendingHasAttack = false;
                if (_impactGuard != null) { StopCoroutine(_impactGuard); _impactGuard = null; }
                StartCoroutine(RecoveryThenReady());
            }
        }
        _impactGuard = null;
    }

    // Perform the overlap, dedupe, and send HitPayloads
    private void DoHit(bool charged, bool perfect, int hp, float posture)
    {
        // Compute hitbox at the moment of impact (matches animation pose/time)
        Vector2 dir = GetFacingDir();
        Vector2 center = GetHitboxCenter(dir, weapon.boxOffset);
        Vector2 size = weapon.boxSize;

        int count = Physics2D.OverlapBoxNonAlloc(center, size, 0f, _hits, weapon.hittableLayers);

        // Reset dedupe buffer
        for (int i = 0; i < _processedRootIds.Length; i++) _processedRootIds[i] = 0;
        int processed = 0;

        DamageTags extra = charged ? DamageTags.Charged : DamageTags.None;
        if (perfect) extra |= DamageTags.Perfect;

        int hitCount = 0;

        for (int i = 0; i < count; i++)
        {
            var col = _hits[i];
            if (!col) continue;

            // don't hit self/children
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;

            var receiver = col.GetComponentInParent<IHitReceiver>();
            if (receiver == null) continue;

            // dedupe per entity (root instance)
            int rootId = col.transform.root.GetInstanceID();
            bool seen = false;
            for (int j = 0; j < processed; j++)
            {
                if (_processedRootIds[j] == rootId) { seen = true; break; }
            }
            if (seen) continue;
            _processedRootIds[processed++] = rootId;

            // build and send payload
            var payload = HitPayload.Melee(
                owner: transform,
                id: weapon.meleeSourceId,
                charged: charged,
                dir: dir,
                hp: hp,
                posture: posture
            );
            payload.tags |= (extra & DamageTags.Perfect); // add Perfect if present

            receiver.ReceiveHit(payload);
            hitCount++;
        }

        // Let animator/VFX react to outcome (shake, SFX, etc.)
        _playerAnimator?.OnMeleeAttackPerformed(charged, perfect, hitCount);
    }

    private IEnumerator RecoveryThenReady()
    {
        if (weapon.recovery > 0f) yield return new WaitForSeconds(weapon.recovery);
        _attacking = false; // ready for next press
    }

    // ===================== Helpers =====================

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
        // Fallback if neither is assigned
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

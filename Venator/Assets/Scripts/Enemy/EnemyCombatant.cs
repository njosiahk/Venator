using UnityEngine;
using Combat;

public class EnemyCombatant : MonoBehaviour, IHitReceiver
{
    [Header("Vitals")]
    [SerializeField] int maxHealth = 1;
    int health;

    [Header("Overkill Rule")]
    [Tooltip("Excess damage required (below 0 HP) for Overkill when weakened.")]
    [SerializeField] int overkillExcessThreshold = 2;

    [Tooltip("If HP before the killing blow is <= this, enemy is 'weakened' for Overkill checks.")]
    [SerializeField] int overkillEligibleAtOrBelowHP = 1;

    // (Step 5 posture system will set this true when broken)
    bool postureBroken = false;

    [Header("Debug Logging")]
    [Tooltip("Log all hits, including non-lethal (HP before/after, source, tags).")]
    [SerializeField] bool verboseHitLogs = true;

    [Tooltip("Log death summary (kind:id, tags, excess).")]
    [SerializeField] bool verboseDeathLogs = true;

    public bool IsDead { get; private set; }
    public DeathRecord LastDeath { get; private set; }

    void Awake() => health = maxHealth;

    /// <summary>
    /// Apply a hit using the shared payload system.
    /// Logs non-lethal and lethal hits when verboseHitLogs is enabled.
    /// </summary>
    public bool ReceiveHit(HitPayload p)
    {
        if (IsDead)
        {
            if (verboseHitLogs)
                Debug.Log($"{name} ignored hit (already dead) from {p.source.kind}:{p.source.id}");
            return false;
        }

        // Centralized exception rules (e.g., always allow vs enemies by default)
        if (!DamageRules.IsAllowedFor(ReceiverType.Enemy, transform, ref p))
        {
            if (verboseHitLogs)
                Debug.Log($"{name} blocked hit by rules: {p.source.kind}:{p.source.id} (tags={p.tags})");
            return false;
        }

        int hpBefore = health;
        health -= p.healthDamage;

        // Lethal
        if (health <= 0)
        {
            int excess = -health;
            bool weakened = postureBroken || (hpBefore <= overkillEligibleAtOrBelowHP);
            if (weakened && excess >= overkillExcessThreshold)
                p.tags |= DamageTags.Overkill; // Overkill = mechanic tag decided by defender

            if (verboseHitLogs)
                Debug.Log($"{name} LETHAL hit by {p.source.kind}:{p.source.id} (tags={p.tags}) dmg={p.healthDamage}  HP {hpBefore}->0  Excess={excess}");

            RecordDeath(p, excess);
            Destroy(gameObject); // Step 6 will replace with corpse conversion if Overkill
            return true;
        }

        // Non-lethal
        if (verboseHitLogs)
            Debug.Log($"{name} hit by {p.source.kind}:{p.source.id} (tags={p.tags}) dmg={p.healthDamage}  HP {hpBefore}->{health}");

        // TODO: stagger/FX hooks can go here (kept minimal for performance)
        return false;
    }

    void RecordDeath(in HitPayload killingBlow, int excess)
    {
        IsDead = true;
        LastDeath = new DeathRecord
        {
            kind = killingBlow.source.kind,
            id = killingBlow.source.id,
            tags = killingBlow.tags,
            attacker = killingBlow.source.owner,
            sourceObject = killingBlow.source.sourceObject,
            position = transform.position,
            time = Time.time,
            excessDamage = excess
        };

        if (verboseDeathLogs)
            Debug.Log($"{name} died via {LastDeath.kind}:{LastDeath.id} Tags={LastDeath.tags} Excess={excess}");
    }
}

/* Shared receiver interface so player/enemy use the same API. */
public interface IHitReceiver
{
    bool ReceiveHit(Combat.HitPayload payload);
}

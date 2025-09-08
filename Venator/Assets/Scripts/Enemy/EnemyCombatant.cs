using System;
using UnityEngine;

namespace Combat
{
    /// <summary>
    /// Thin wrapper around CombatantVitals:
    /// - Subscribes to Vitals events to drive animations / logs
    /// - Records DeathRecord (incl. Overkill + excess damage)
    /// - Backward-compatible ReceiveHit shim (obsolete)
    /// </summary>
    [DisallowMultipleComponent]
    public class EnemyCombatant : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CombatantVitals vitals;
        [SerializeField] private Animator animator; // optional, leave null if you don't drive enemy anims yet

        [Header("Animator Parameter Names (optional)")]
        [SerializeField] private string hitTrigger = "Hit";
        [SerializeField] private string brokenBool = "Broken";
        [SerializeField] private string deadBool = "Dead";

        [Header("Debug")]
        [SerializeField] private bool verboseHitLogs = false;
        [SerializeField] private bool verboseDeathLogs = true;

        /// <summary>Last lethal hit info, filled on death.</summary>
        public DeathRecord LastDeath { get; private set; }

        /// <summary>Expose vitals for other systems.</summary>
        public CombatantVitals Vitals => vitals;

        private void Reset()
        {
            if (!vitals) vitals = GetComponent<CombatantVitals>();
            if (!animator) animator = GetComponentInChildren<Animator>();
        }

        private void Awake()
        {
            if (!vitals) vitals = GetComponent<CombatantVitals>();
            if (!vitals)
            {
                // Ensure there's always a single IHitReceiver on this object (the Vitals).
                vitals = gameObject.AddComponent<CombatantVitals>();
            }
            if (!animator) animator = GetComponentInChildren<Animator>();
        }

        private void OnEnable()
        {
            if (vitals == null) return;
            vitals.Damaged += OnDamaged;
            vitals.PostureBroken += OnPostureBroken;
            vitals.PostureRecovered += OnPostureRecovered;
            vitals.Died += OnDied;
        }

        private void OnDisable()
        {
            if (vitals == null) return;
            vitals.Damaged -= OnDamaged;
            vitals.PostureBroken -= OnPostureBroken;
            vitals.PostureRecovered -= OnPostureRecovered;
            vitals.Died -= OnDied;
        }

        // ----------------- Event Handlers -----------------

        private void OnDamaged(HitPayload p)
        {
            if (animator && !string.IsNullOrEmpty(hitTrigger))
                animator.SetTrigger(hitTrigger);

            if (verboseHitLogs)
                Debug.Log($"{name} took HP:{p.healthDamage} / PO:{p.postureDamage} from {p.source.kind}:{p.source.id} (tags={p.tags})", this);
        }

        private void OnPostureBroken(HitPayload p)
        {
            if (animator && !string.IsNullOrEmpty(brokenBool))
                animator.SetBool(brokenBool, true);
        }

        private void OnPostureRecovered()
        {
            if (animator && !string.IsNullOrEmpty(brokenBool))
                animator.SetBool(brokenBool, false);
        }

        private void OnDied(HitPayload p, bool overkill)
        {
            // Record a structured death entry for logging/fear systems
            LastDeath = new DeathRecord
            {
                kind = p.source.kind,
                id = p.source.id,
                tags = p.tags | (overkill ? DamageTags.Overkill : DamageTags.None),
                attacker = p.source.owner,
                sourceObject = p.source.sourceObject,
                position = transform.position,
                time = Time.time,
                excessDamage = vitals != null ? vitals.LastExcessDamage : 0
            };

            if (animator && !string.IsNullOrEmpty(deadBool))
                animator.SetBool(deadBool, true);

            if (verboseDeathLogs)
                Debug.Log($"{name} died via {LastDeath.kind}:{LastDeath.id} tags={LastDeath.tags} excess={LastDeath.excessDamage}", this);

            // Let external systems (AI, pooling, loot) react here if they want.
            Died?.Invoke(this);
        }

        // ----------------- Public Events -----------------

        /// <summary>Raised after vitals report death (good place to disable AI, drop loot, pool).</summary>
        public event Action<EnemyCombatant> Died;

        // ----------------- Backward-compat Shim -----------------

        /// <summary>
        /// Obsolete shim so old code calling EnemyCombatant.ReceiveHit(...) still works.
        /// Prefer: GetComponent&lt;CombatantVitals&gt;().ReceiveHit(payload)
        /// </summary>
        [Obsolete("Use CombatantVitals (IHitReceiver) instead. This forwards to vitals.")]
        public bool ReceiveHit(HitPayload payload)
        {
            return vitals != null && vitals.ReceiveHit(payload);
        }
    }
}

using UnityEngine;
using System;

namespace Combat
{
    /// HP + Posture + Break (stun) + regen. Single source of truth.
    [DisallowMultipleComponent]
    public class CombatantVitals : MonoBehaviour, IHitReceiver
    {
        [Header("Who am I? (for DamageRules)")]
        [SerializeField] private ReceiverType receiverType = ReceiverType.Enemy;

        [Header("Health")]
        [SerializeField] private int maxHP = 100;
        [SerializeField] private int weakenedHPThreshold = 25;
        public int CurrentHP { get; private set; }
        public int MaxHP => maxHP;

        [Header("Posture")]
        [SerializeField] private float maxPosture = 100f;
        [SerializeField] private float regenDelay = 1.25f;
        [SerializeField] private float regenRate = 45f;
        [SerializeField] private float brokenDuration = 1.2f;
        [SerializeField, Tooltip("Posture restored immediately when break ends (0..1 of max).")]
        private float postBreakRestoreFraction = 0.5f;

        public float CurrentPosture { get; private set; }
        public float MaxPosture => maxPosture;
        public bool IsBroken { get; private set; }
        public bool IsDead { get; private set; }

        [Header("Overkill")]
        [SerializeField, Tooltip("Excess damage needed beyond remaining HP to flag Overkill when weakened/broken.")]
        private int overkillExcessThreshold = 20;

        [Header("Optional: ignore hits from these layers (e.g., friendly fire)")]
        [SerializeField] private LayerMask rejectFromLayers;

        // bookkeeping
        private float _lastHitTime;
        private float _brokenEndsAt;

        // provenance (for logs & fear later)
        public Transform LastAttacker { get; private set; }
        public ushort LastSourceId { get; private set; }
        public int LastExcessDamage { get; private set; }

        // events
        public event Action<HitPayload> Damaged;
        public event Action<HitPayload> PostureBroken;
        public event Action PostureRecovered;
        public event Action<HitPayload, bool /*overkill*/> Died;

        void Awake()
        {
            CurrentHP = Mathf.Max(1, maxHP);
            CurrentPosture = maxPosture;
        }

        void Update()
        {
            if (IsDead) return;

            // end break
            if (IsBroken && Time.time >= _brokenEndsAt)
            {
                IsBroken = false;
                CurrentPosture = Mathf.Clamp(CurrentPosture, maxPosture * postBreakRestoreFraction, maxPosture);
                PostureRecovered?.Invoke();
            }

            // regen posture
            if (!IsBroken && CurrentPosture < maxPosture && (Time.time - _lastHitTime) >= regenDelay)
            {
                CurrentPosture = Mathf.Min(maxPosture, CurrentPosture + regenRate * Time.deltaTime);
            }
        }

        public bool ReceiveHit(HitPayload payload)
        {
            if (IsDead) return false;

            // optional ignore by attacker layer
            var attacker = payload.source.owner;
            if (rejectFromLayers.value != 0 && attacker != null)
            {
                if (((1 << attacker.gameObject.layer) & rejectFromLayers.value) != 0) return false;
            }

            // global receiver-side rules (e.g., Player can’t be executed)
            if (!DamageRules.IsAllowedFor(receiverType, transform, ref payload)) return false;

            // provenance
            LastAttacker = attacker;
            LastSourceId = payload.source.id;

            // posture first
            if (!IsBroken && payload.postureDamage > 0f)
            {
                CurrentPosture -= payload.postureDamage;
                _lastHitTime = Time.time;

                if (CurrentPosture <= 0f)
                {
                    CurrentPosture = 0f;
                    IsBroken = true;
                    _brokenEndsAt = Time.time + brokenDuration;
                    PostureBroken?.Invoke(payload);
                }
            }
            else if (payload.postureDamage > 0f)
            {
                _lastHitTime = Time.time;
            }

            // hp
            int hpBefore = CurrentHP;
            int hpDamage = Mathf.Max(0, payload.healthDamage);
            CurrentHP = Mathf.Max(0, CurrentHP - hpDamage);

            Damaged?.Invoke(payload);

            if (CurrentHP == 0 && !IsDead)
            {
                IsDead = true;

                bool weakened = IsBroken || (hpBefore <= weakenedHPThreshold);
                int excess = Mathf.Max(0, hpDamage - hpBefore);
                LastExcessDamage = excess;
                bool overkill = weakened && (excess >= overkillExcessThreshold);

                Died?.Invoke(payload, overkill);
                return true;
            }

            return false;
        }

        // helpers
        public void SetHP(int newMax, int? setCurrent = null)
        {
            maxHP = Mathf.Max(1, newMax);
            CurrentHP = Mathf.Clamp(setCurrent ?? CurrentHP, 0, maxHP);
        }
        public void SetPosture(float newMax, float? setCurrent = null)
        {
            maxPosture = Mathf.Max(1f, newMax);
            CurrentPosture = Mathf.Clamp(setCurrent ?? CurrentPosture, 0f, maxPosture);
        }
        public void ForceBreak(float durationOverride = -1f)
        {
            if (IsDead) return;
            IsBroken = true; CurrentPosture = 0f;
            _brokenEndsAt = Time.time + (durationOverride > 0f ? durationOverride : brokenDuration);
            PostureBroken?.Invoke(default);
        }
        public void KillSilently()
        {
            if (IsDead) return;
            CurrentHP = 0; IsDead = true;
            Died?.Invoke(default, false);
        }
    }
}

using UnityEngine;
using System;

namespace Combat
{
    // ---------- Interface (declared here so everything compiles immediately) ----------
    public interface IHitReceiver
    {
        /// Apply a hit to this object. Return true if the hit was lethal.
        bool ReceiveHit(HitPayload payload);
    }

    /// <summary>
    /// Single source of truth for HP + Posture with Break (stun) + regen + Overkill flagging.
    /// Implements IHitReceiver so all attacks can just send a HitPayload.
    /// </summary>
    [DisallowMultipleComponent]
    public class CombatantVitals : MonoBehaviour, IHitReceiver
    {
        [Header("Who am I? (optional)")]
        [SerializeField] private ReceiverType receiverType = ReceiverType.Enemy; // used only for your own logic/UI

        // -------------------- Health --------------------
        [Header("Health")]
        [SerializeField] private int maxHP = 100;
        [SerializeField, Tooltip("If HP was at/below this before the lethal hit, count as 'weakened'.")]
        private int weakenedHPThreshold = 25;
        public int CurrentHP { get; private set; }
        public int MaxHP => maxHP;

        // -------------------- Posture -------------------
        [Header("Posture")]
        [SerializeField] private float maxPosture = 100f;
        [SerializeField, Tooltip("Seconds after last hit before regen starts")]
        private float regenDelay = 1.25f;
        [SerializeField, Tooltip("Posture per second while regenerating")]
        private float regenRate = 45f;
        [SerializeField, Tooltip("How long the 'broken' (stunned) state lasts")]
        private float brokenDuration = 1.2f;
        [SerializeField, Range(0f, 1f), Tooltip("Posture restored immediately when break ends")]
        private float postBreakRestoreFraction = 0.5f;

        public float CurrentPosture { get; private set; }
        public float MaxPosture => maxPosture;
        public bool IsBroken { get; private set; }
        public bool IsDead { get; private set; }

        // -------------------- Overkill ------------------
        [Header("Overkill")]
        [SerializeField, Tooltip("Excess damage beyond remaining HP needed to flag Overkill when weakened/broken")]
        private int overkillExcessThreshold = 20;

        // -------------------- Filtering -----------------
        [Header("Hittable Filtering (optional)")]
        [SerializeField, Tooltip("Ignore hits whose attacker (payload.source.owner) is on these layers")]
        private LayerMask rejectFromLayers;

        // -------------------- Provenance ----------------
        public Transform LastAttacker { get; private set; }
        public ushort LastSourceId { get; private set; } // maps to your DamageSourceId
        public int LastExcessDamage { get; private set; }

        // -------------------- Events --------------------
        public event Action<HitPayload> Damaged;
        public event Action<HitPayload> PostureBroken;
        public event Action PostureRecovered;
        public event Action<HitPayload, bool /*overkill*/> Died;

        // -------------------- Internals -----------------
        private float _lastHitTime;
        private float _brokenEndsAt;

        private void Awake()
        {
            CurrentHP = Mathf.Max(1, maxHP);
            CurrentPosture = maxPosture;
        }

        private void Update()
        {
            if (IsDead) return;

            // Break timer
            if (IsBroken && Time.time >= _brokenEndsAt)
            {
                IsBroken = false;
                // bring posture up a bit so we don't re-break instantly
                CurrentPosture = Mathf.Clamp(CurrentPosture, maxPosture * postBreakRestoreFraction, maxPosture);
                PostureRecovered?.Invoke();
            }

            // Posture regen (not while broken)
            if (!IsBroken && CurrentPosture < maxPosture)
            {
                if (Time.time - _lastHitTime >= regenDelay)
                    CurrentPosture = Mathf.Min(maxPosture, CurrentPosture + regenRate * Time.deltaTime);
            }
        }

        // -------------------- IHitReceiver --------------------
        public bool ReceiveHit(HitPayload payload)
        {
            if (IsDead) return false;

            // Optional attacker layer filter
            var attacker = payload.source.owner;
            if (rejectFromLayers.value != 0 && attacker != null)
            {
                if (((1 << attacker.gameObject.layer) & rejectFromLayers.value) != 0)
                    return false;
            }

            // Record provenance for logs/fear
            LastAttacker = attacker;
            LastSourceId = payload.source.id;

            // 1) Apply posture
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
                // even when broken, reset regen delay on posture hits
                _lastHitTime = Time.time;
            }

            // 2) Apply HP
            int hpBefore = CurrentHP;
            int hpDamage = Mathf.Max(0, payload.healthDamage);
            CurrentHP = Mathf.Max(0, CurrentHP - hpDamage);

            Damaged?.Invoke(payload);

            // 3) Death check + Overkill flag
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

        // -------------------- Helpers --------------------
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
            IsBroken = true;
            CurrentPosture = 0f;
            _brokenEndsAt = Time.time + (durationOverride > 0f ? durationOverride : brokenDuration);
            PostureBroken?.Invoke(default);
        }

        public void KillSilently()
        {
            if (IsDead) return;
            CurrentHP = 0;
            IsDead = true;
            Died?.Invoke(default, false);
        }
    }
}

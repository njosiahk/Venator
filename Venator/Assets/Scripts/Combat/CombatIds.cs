using UnityEngine;

namespace Combat
{
    /* -- Categories stay small and stable; add a new one only if really needed. -- */
    public enum DamageKind : byte
    {
        Melee = 1,        // fists, knife, blade...
        Throwable = 2,    // thrown knife/pan/brick/explosive/bottle
        Projectile = 3,   // bullets, shrapnel, etc.
        Explosion = 4,    // explosive AOE
        PhysicsImpact = 5,// flung enemy, falling enemy
        Environmental = 6,// crushers, hazards, lasers
        Special = 7       // executions, scripted kills
    }

    /* -- IDs are compact and extendable. Use ranges per category for clarity. -- */
    public static class DamageSourceId
    {
        /* Melee 1–99 */
        public const ushort Melee_Fists = 1;
        public const ushort Melee_Knife = 2;
        public const ushort Melee_Blade = 3;
        // (Add future melee here: 4..99)

        /* Projectile 100–199
        Includes throwables (knife/pan/brick/explosive/bottle), bullets, and CORPSES (launched). */
        public const ushort Proj_ThrownKnife = 101;
        public const ushort Proj_ThrownPan = 102; // can reflect bullets as a mechanic
        public const ushort Proj_ThrownBrick = 103;
        public const ushort Proj_ThrownExplosive = 104; // explosion damage will be Explosion_* when it detonates
        public const ushort Proj_ThrownBottle = 105;
        public const ushort Proj_Bullet = 110;
        public const ushort Proj_Corpse = 120;   // launched corpse projectile (from Overkill)
        // (Add more projectiles here: 121..199)

        /* Explosion 200–299 */
        public const ushort Explosion_Generic = 200;
        public const ushort Explosion_ThrownExplosive = 201;
        // (Add more explosions here: 202..299)

        /* PhysicsImpact 300–399 (LIVE body collisions)
        NOTE: this is for *living* bodies being thrown/launched, NOT corpses. */
        public const ushort Phys_SlamWall = 301; // thrown/boosted/live body hits wall/obstacle
        public const ushort Phys_SlamGround = 302; //fall damage
        // (Add more physics impacts here: 304..399)

        /* Special 400–499 (executions, scripted kills) */
        public const ushort Fists_Execution = 401;
        public const ushort Knife_Execution = 401;
        public const ushort Blade_Execution = 401;
        // (Add future specials here: 402..499)

        /* Environmental 500–599 */
        public const ushort Env_Spikes = 501;
        public const ushort Env_Crusher = 502;
        public const ushort Env_Laser = 503;
        // (Add more environmental hazards here: 504..599)
    }

    /* ------------- TAGS (ORTHOGONAL MODIFIERS) -------------
       Overkill is a mechanic: set on death if (weakened && excessDamage >= threshold). */
    [System.Flags]
    public enum DamageTags : ushort
    {
        None = 0,
        Charged = 1 << 0, // held melee
        Boosted = 1 << 1, // boosted throw etc.
        Reflected = 1 << 2, // projectile was reflected
        Overkill = 1 << 3,  // set by defender on lethal if rule matched
        Perfect = 1 << 4 // perfect charged attack
        // (Add future tags here)
    }

    /* ------------- SOURCE DESCRIPTOR -------------
   Holds WHAT caused damage, WHICH object did it, and WHO owns it. */
    public struct HitSource
    {
        public DamageKind kind;     // Melee / Projectile / Explosion / PhysicsImpact / Environmental / Special
        public ushort id;       // Specific ID (see DamageSourceId)
        public Object sourceObject; // e.g., thrown brick GO, bullet GO, corpse GO; melee can just use attacker
        public Transform owner;    // who performed it: player/enemy/etc.
    }

    /* ------------- PAYLOAD -------------
       Lightweight packet passed to receivers. */
    public struct HitPayload
    {
        public HitSource source;
        public Vector2 direction;     // for knockback or launch
        public int healthDamage;  // posture lives here later too
        public float postureDamage;
        public DamageTags tags;
    
        /* Factory helpers (add more as you add kinds) */

        // Melee (tap/charged controlled by 'charged' + tags)
        public static HitPayload Melee(Transform owner, ushort id, bool charged,
                                       Vector2 dir, int hp, float posture)
        {
            return new HitPayload
            {
                source = new HitSource { kind = DamageKind.Melee, id = id, sourceObject = owner, owner = owner },
                direction = dir,
                healthDamage = hp,
                postureDamage = posture,
                tags = charged ? DamageTags.Charged : DamageTags.None
            };
        }

        // Projectile (includes thrown items, bullets, reflected bullets, CORPSES)
        public static HitPayload Projectile(Transform owner, Object projectileObj, ushort id,
                                            Vector2 dir, int hp, float posture, DamageTags extraTags = DamageTags.None)
        {
            return new HitPayload
            {
                source = new HitSource { kind = DamageKind.Projectile, id = id, sourceObject = projectileObj, owner = owner },
                direction = dir,
                healthDamage = hp,
                postureDamage = posture,
                tags = extraTags
            };
        }

        // Explosion (AoE pulses)
        public static HitPayload Explosion(Transform owner, Object explosiveObj, ushort id,
                                           Vector2 dir, int hp)
        {
            return new HitPayload
            {
                source = new HitSource { kind = DamageKind.Explosion, id = id, sourceObject = explosiveObj, owner = owner },
                direction = dir,
                healthDamage = hp,
                postureDamage = 0f,
                tags = DamageTags.None
            };
        }

        // PhysicsImpact (LIVE body slams)
        public static HitPayload PhysicsImpact(Transform ownerLiveBody, Object liveBodyObj, ushort id,
                                               Vector2 dir, int hp)
        {
            return new HitPayload
            {
                source = new HitSource { kind = DamageKind.PhysicsImpact, id = id, sourceObject = liveBodyObj, owner = ownerLiveBody },
                direction = dir,
                healthDamage = hp,
                postureDamage = 0f,
                tags = DamageTags.None
            };
        }

        // Special (e.g., execution)
        public static HitPayload Special(Transform owner, Object obj, ushort id, Vector2 dir, int hp)
        {
            return new HitPayload
            {
                source = new HitSource { kind = DamageKind.Special, id = id, sourceObject = obj, owner = owner },
                direction = dir,
                healthDamage = hp,
                postureDamage = 0f,
                tags = DamageTags.None
            };
        }
    }

    /* ------------- DEATH RECORD -------------
       Stored by the defender upon lethal hit. */
    public struct DeathRecord
    {
        public DamageKind kind;
        public ushort id;
        public DamageTags tags;
        public Transform attacker;
        public Object sourceObject;
        public Vector2 position;
        public float time;
        public int excessDamage; // how far below 0 HP we went
    }

    /* Who is being hit (for exception rules). */
    public enum ReceiverType : byte { Enemy = 1, Player = 2, Other = 3 }
}


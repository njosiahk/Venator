using UnityEngine;
using Combat; // for DamageSourceId

/*  WeaponStats holds all tunable numbers for one melee weapon.
 *  Make one asset per weapon (Fists, Knife, Blade). You can hot-swap at runtime.
 */
[CreateAssetMenu(menuName = "Combat/Weapon Stats", fileName = "WeaponStats_")]
public class WeaponStats : ScriptableObject
{
    [Header("Meta")]
    [Tooltip("Shown in debug or UI")]
    public string displayName = "Knife";

    [Tooltip("DamageSourceId for this melee weapon (e.g., Melee_Knife)")]
    public ushort meleeSourceId = DamageSourceId.Melee_Knife;

    [Header("Charge Detection")]
    [Tooltip("Hold time (seconds) to count as CHARGED")]
    [Min(0f)] public float chargeThreshold = 0.28f;

    [Header("Timings")]
    [Tooltip("Windup (s) before a LIGHT hit connects")]
    [Min(0f)] public float windupLight = 0.06f;

    [Tooltip("Windup (s) before a CHARGED hit connects")]
    [Min(0f)] public float windupCharged = 0.10f;

    [Tooltip("Recovery (s) lockout after any hit")]
    [Min(0f)] public float recovery = 0.06f;

    [Header("Damage")]
    [Tooltip("HP damage dealt by a LIGHT hit")]
    public int hpDamageLight = 1;

    [Tooltip("HP damage dealt by a CHARGED hit")]
    public int hpDamageCharged = 99;

    [Tooltip("Posture damage dealt by a LIGHT hit")]
    public float postureDamageLight = 0f;

    [Tooltip("Posture damage dealt by a CHARGED hits")]
    public float postureDamageCharged = 0f;

    [Header("Perfect Charge")]
    public bool hasPerfectCharge = true;

    [Tooltip("Perfect window START time (sec since press).")]
    public float perfectStart = 0.40f;

    [Tooltip("Perfect window END time (sec since press).")]
    public float perfectEnd = 0.45f;

    public bool perfectOverridesDamage = true;
    public int hpDamagePerfect = 150;
    public float postureDamagePerfect = 1.75f;

    [Header("Hitbox")]
    [Tooltip("Box size (world units) used for this weapon's swing")]
    public Vector2 boxSize = new(1.2f, 0.8f);

    [Tooltip("Box center offset from the player's collider/transform; X mirrors with facing")]
    public Vector2 boxOffset = new(0.8f, 0.15f);

    [Header("Filters")]
    [Tooltip("Layers this melee can hit (usually the Enemy layer)")]
    public LayerMask hittableLayers;

    void OnValidate()
    {
        if (perfectEnd < perfectStart) perfectEnd = perfectStart;
        if (hasPerfectCharge && perfectStart < chargeThreshold) perfectStart = chargeThreshold;
    }
}

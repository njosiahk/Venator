using UnityEngine;
using Combat;

public class PlayerHealth : MonoBehaviour, IHitReceiver
{
    [SerializeField] int maxHealth = 3;
    int health;

    void Awake() => health = maxHealth;

    public bool ReceiveHit(HitPayload p)
    {
        if (!DamageRules.IsAllowedFor(ReceiverType.Player, transform, ref p))
            return false;

        health -= p.healthDamage;
        Debug.Log($"Player took {p.healthDamage} from {p.source.kind}:{p.source.id} (tags={p.tags}). HP={health}");

        if (health <= 0)
        {
            Debug.Log("Player died");
            // TODO: respawn/lose state here
            return true;
        }
        return false;
    }
}

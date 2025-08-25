using UnityEngine;
using Combat;

public static class DamageRules
{
    /* Validate/adjust a payload for a given receiver before applying it. */
    public static bool IsAllowedFor(ReceiverType receiver, Transform receiverTransform, ref HitPayload p)
    {
        if (receiver == ReceiverType.Player)
        {
            // Player cannot be executed.
            if (p.source.kind == DamageKind.Special)
                return false;

            /* Player shouldn't take self-slam damage from PhysicsImpact.
               We only block if the IMPACTING live body owner *is the player*.
               - If a THROWN ENEMY slams into the player, this remains allowed. */
            if (p.source.kind == DamageKind.PhysicsImpact && p.source.owner == receiverTransform)
                return false;
        }

        // (Add more receiver-specific exceptions here as your design evolves)
        return true;
    }
}

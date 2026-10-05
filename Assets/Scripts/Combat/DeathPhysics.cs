using UnityEngine;

/// <summary>
/// Takes a dead unit out of the physics world, shared by the death reactions.
/// </summary>
public static class DeathPhysics
{
    /// <summary>
    /// Disables every <see cref="Collider2D"/> on <paramref name="deadObject"/> itself (not its children) and, when
    /// it has a <see cref="Rigidbody2D"/>, stops it and turns its simulation off.
    /// </summary>
    public static void Disable(GameObject deadObject)
    {
        Collider2D[] colliders = deadObject.GetComponents<Collider2D>();
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
            {
                colliders[i].enabled = false;
            }
        }

        if (!deadObject.TryGetComponent(out Rigidbody2D rb))
        {
            return;
        }

        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.simulated = false;
    }
}

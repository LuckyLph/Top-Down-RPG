using UnityEngine;

public static class DeathPhysics
{
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

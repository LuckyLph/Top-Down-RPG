using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// Creates the <see cref="SwordSlashAttack"/> for a weapon swing through the container and hands it the
/// <see cref="HitService"/> its hits go through. Used for local and replayed remote swings alike.
/// </summary>
public sealed class SlashSpawner
{
    private readonly IObjectResolver resolver;
    private readonly HitService hitService;

    /// <summary>
    /// Captures the container slashes are instantiated through and the hit service they use.
    /// </summary>
    public SlashSpawner(IObjectResolver resolver, HitService hitService)
    {
        this.resolver = resolver;
        this.hitService = hitService;
    }

    /// <summary>
    /// Instantiates and activates the weapon's slash prefab and initialises it for <paramref name="owner"/> swinging
    /// toward <paramref name="direction"/>. Logs an error and returns null when the weapon is missing, has no slash
    /// prefab, or that prefab has no <see cref="SwordSlashAttack"/> on its root.
    /// </summary>
    public SwordSlashAttack Spawn(Transform owner, PlayerWeapon weapon, Vector2 direction, SpriteRenderer ownerSpriteRenderer = null)
    {
        if (weapon == null || weapon.SlashPrefab == null)
        {
            Debug.LogError($"Cannot spawn a slash: weapon '{(weapon != null ? weapon.DisplayName : "null")}' has no slash prefab.");
            return null;
        }

        SwordSlashAttack slashPrefab = weapon.SlashPrefab.GetComponent<SwordSlashAttack>();
        if (slashPrefab == null)
        {
            Debug.LogError($"Slash prefab '{weapon.SlashPrefab.name}' has no {nameof(SwordSlashAttack)} on its root.", weapon.SlashPrefab);
            return null;
        }

        SwordSlashAttack slash = resolver.Instantiate(slashPrefab);
        slash.gameObject.SetActive(true);
        slash.gameObject.name = $"{weapon.DisplayName}Slash";
        slash.Initialize(owner, weapon, direction, hitService, ownerSpriteRenderer);
        return slash;
    }
}

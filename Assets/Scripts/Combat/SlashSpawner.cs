using UnityEngine;
using VContainer;
using VContainer.Unity;

public sealed class SlashSpawner
{
    private readonly IObjectResolver resolver;

    public SlashSpawner(IObjectResolver resolver)
    {
        this.resolver = resolver;
    }

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
        slash.Initialize(owner, weapon, direction, ownerSpriteRenderer);
        return slash;
    }
}

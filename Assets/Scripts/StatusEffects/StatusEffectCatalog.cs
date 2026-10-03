using UnityEngine;

/// <summary>
/// Every status definition in a fixed order. A status is identified on the wire by its index here, which is safe
/// because join approval refuses clients on another build version.
/// </summary>
[CreateAssetMenu(fileName = "StatusEffectCatalog", menuName = "TopDownRPG/Combat/Status Effect Catalog")]
public sealed class StatusEffectCatalog : ScriptableObject
{
    [SerializeField] private StatusEffectDefinition[] effects = new StatusEffectDefinition[0];

    public int Count => effects != null ? effects.Length : 0;

    public StatusEffectDefinition Get(int index)
    {
        return index >= 0 && index < Count ? effects[index] : null;
    }

    /// <summary>
    /// The index of <paramref name="definition"/>, or -1 when it is not in the catalog.
    /// </summary>
    public int IndexOf(StatusEffectDefinition definition)
    {
        if (definition == null)
        {
            return -1;
        }

        for (int i = 0; i < Count; i++)
        {
            if (effects[i] == definition)
            {
                return i;
            }
        }

        return -1;
    }

    public static StatusEffectCatalog Create(params StatusEffectDefinition[] definitions)
    {
        StatusEffectCatalog catalog = CreateInstance<StatusEffectCatalog>();
        catalog.effects = definitions ?? new StatusEffectDefinition[0];
        catalog.hideFlags = HideFlags.HideAndDontSave;
        return catalog;
    }
}

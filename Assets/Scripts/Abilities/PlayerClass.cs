using UnityEngine;

/// <summary>
/// A player class: a name and exactly four abilities (the Q W E R slots).
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Abilities/Player Class", fileName = "Class")]
public sealed class PlayerClass : ScriptableObject
{
    public const int AbilityCount = 4;

    [SerializeField] private string displayName = "Class";
    [SerializeField, Tooltip("Exactly four abilities, for Q, W, E and R. Empty entries are empty slots.")]
    private AbilityDefinition[] abilities = new AbilityDefinition[AbilityCount];

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

    private void OnValidate()
    {
        abilities = AbilitySlotArrays.Resize(abilities, AbilityCount);
    }

    public AbilityDefinition GetAbility(int index)
    {
        return abilities != null && index >= 0 && index < abilities.Length ? abilities[index] : null;
    }

    public static PlayerClass Create(string name, params AbilityDefinition[] classAbilities)
    {
        PlayerClass playerClass = CreateInstance<PlayerClass>();
        playerClass.name = name;
        playerClass.displayName = name;
        playerClass.abilities = AbilitySlotArrays.Resize(classAbilities, AbilityCount);
        playerClass.hideFlags = HideFlags.HideAndDontSave;
        return playerClass;
    }
}

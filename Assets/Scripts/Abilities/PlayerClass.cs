using UnityEngine;

/// <summary>
/// A player class: exactly four abilities (the Q W E R slots).
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Abilities/Player Class", fileName = "Class")]
public sealed class PlayerClass : ScriptableObject
{
    public const int AbilityCount = 4;

    [SerializeField, Tooltip("Exactly four abilities, for Q, W, E and R. Empty entries are empty slots.")]
    private AbilityDefinition[] abilities = new AbilityDefinition[AbilityCount];

    /// <summary>
    /// Keeps the ability list at exactly <see cref="AbilityCount"/> entries in the inspector.
    /// </summary>
    private void OnValidate()
    {
        abilities = AbilitySlotArrays.Resize(abilities, AbilityCount);
    }

    /// <summary>
    /// The ability for class slot <paramref name="index"/> (0 is Q), or null when empty or out of range.
    /// </summary>
    public AbilityDefinition GetAbility(int index)
    {
        return abilities != null && index >= 0 && index < abilities.Length ? abilities[index] : null;
    }

    /// <summary>
    /// Builds an unsaved, hidden runtime class with <paramref name="classAbilities"/> truncated or padded to four
    /// slots, for tests that must not touch project assets.
    /// </summary>
    public static PlayerClass Create(string name, params AbilityDefinition[] classAbilities)
    {
        PlayerClass playerClass = CreateInstance<PlayerClass>();
        playerClass.name = name;
        playerClass.abilities = AbilitySlotArrays.Resize(classAbilities, AbilityCount);
        playerClass.hideFlags = HideFlags.HideAndDontSave;
        return playerClass;
    }
}

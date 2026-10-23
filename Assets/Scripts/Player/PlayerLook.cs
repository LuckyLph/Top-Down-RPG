using System;
using UnityEngine;
using VContainer;

/// <summary>
/// The look a player is drawn with (Docs/AsepriteCharacters.md, Phase 2): one of its <see cref="CharacterLookCatalog"/>'s
/// looks shown on its character visuals. The owner follows the local settings' choice
/// (<see cref="UserSettings.CharacterLook"/>, by name; the catalog's first look for an empty or unknown name) and raises
/// <see cref="Chosen"/> with each look it shows, so the network sync can tell the other machines, which show it on their
/// copy (<see cref="Show"/>). Visual only: nothing in gameplay reads it.
/// </summary>
[DisallowMultipleComponent]
public class PlayerLook : MonoBehaviour
{
    [SerializeField, Tooltip("Every look a player can be drawn with; a look is named on the wire by its index here.")]
    private CharacterLookCatalog catalog;

    [SerializeField, Tooltip("The character's visuals, which draw the look.")]
    private CharacterVisuals visuals;

    private UserSettings settings;
    private bool following;
    private bool subscribed;

    /// <summary>
    /// Raised on the owner with the index of each look it shows from the local settings.
    /// </summary>
    public event Action<int> Chosen;

    public CharacterLookCatalog Catalog => catalog;

    /// <summary>
    /// The index of the look shown, or -1 while the visuals keep the prefab's own.
    /// </summary>
    public int Index { get; private set; } = -1;

    /// <summary>
    /// Finds the visuals when unassigned, and fails loudly without a catalog or visuals.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
        if (catalog == null || visuals == null)
        {
            Debug.LogError($"{name} needs a {nameof(CharacterLookCatalog)} and {nameof(CharacterVisuals)} to show its look.", this);
        }
    }

    /// <summary>
    /// Fills an empty visuals reference from this object's children.
    /// </summary>
    private void OnValidate()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Follows the local settings again while the owner follows them.
    /// </summary>
    private void OnEnable()
    {
        if (following)
        {
            Subscribe();
        }
    }

    /// <summary>
    /// Stops following the local settings while disabled.
    /// </summary>
    private void OnDisable()
    {
        Unsubscribe();
    }

    /// <summary>
    /// Takes the local settings the owner's choice is read from.
    /// </summary>
    [Inject]
    public void Construct(UserSettings userSettings)
    {
        settings = userSettings;
    }

    /// <summary>
    /// Shows the catalog's look at <paramref name="index"/> on the visuals; returns false, changing nothing, for an
    /// index the catalog lacks.
    /// </summary>
    public bool Show(int index)
    {
        CharacterLook look = catalog != null ? catalog.Get(index) : null;
        if (look == null || visuals == null)
        {
            return false;
        }

        visuals.SetLook(look.AnimationSet, look.Locomotion);
        Index = index;
        return true;
    }

    /// <summary>
    /// On the owner: shows the look the local settings name and follows every change to it, raising
    /// <see cref="Chosen"/> each time, until <see cref="StopFollowing"/>.
    /// </summary>
    public void FollowLocalChoice()
    {
        following = true;
        if (enabled && gameObject.activeInHierarchy)
        {
            Subscribe();
        }

        ShowChoice();
    }

    /// <summary>
    /// Stops following the local settings (the player despawns).
    /// </summary>
    public void StopFollowing()
    {
        following = false;
        Unsubscribe();
    }

    /// <summary>
    /// Shows the look the local settings name, the catalog's first for an empty or unknown name, and raises
    /// <see cref="Chosen"/>.
    /// </summary>
    private void ShowChoice()
    {
        int index = catalog != null ? Mathf.Max(0, catalog.IndexOf(settings?.CharacterLook)) : -1;
        if (Show(index))
        {
            Chosen?.Invoke(index);
        }
    }

    /// <summary>
    /// Follows changes to the local settings' look.
    /// </summary>
    private void Subscribe()
    {
        if (!subscribed && settings != null)
        {
            settings.CharacterLookChanged += ShowChoice;
            subscribed = true;
        }
    }

    /// <summary>
    /// Stops following changes to the local settings' look.
    /// </summary>
    private void Unsubscribe()
    {
        if (subscribed)
        {
            settings.CharacterLookChanged -= ShowChoice;
            subscribed = false;
        }
    }

    /// <summary>
    /// Falls back to the <see cref="CharacterVisuals"/> under this object when none is assigned.
    /// </summary>
    private void ResolveReferences()
    {
        if (visuals == null)
        {
            visuals = GetComponentInChildren<CharacterVisuals>(true);
        }
    }

    /// <summary>
    /// Test setup without the prefab: sets the catalog and the visuals.
    /// </summary>
    internal void Configure(CharacterLookCatalog lookCatalog, CharacterVisuals lookVisuals)
    {
        catalog = lookCatalog;
        visuals = lookVisuals;
    }
}

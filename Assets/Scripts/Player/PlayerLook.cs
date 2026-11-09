using System;
using UnityEngine;
using VContainer;

/// <summary>
/// The look a player is drawn with (Docs/AsepriteCharacters.md, Phase 2): one of its <see cref="CharacterLookCatalog"/>'s
/// looks shown on its character visuals, in the colours it picked of its <see cref="CharacterColours"/>
/// (Docs/PlayerCharacter.md, Phase 5) on its <see cref="CharacterPalette"/>. The owner follows the local settings'
/// choices (<see cref="UserSettings.CharacterLook"/>, by name, the catalog's first look for an empty or unknown name; and
/// <see cref="UserSettings.CharacterColour"/> per colour choice, by name, the choice's own swatch for an empty or
/// unknown one) and raises <see cref="Chosen"/> with each look and <see cref="ColoursChosen"/> with each set of colours
/// it shows, so the network sync can tell the other machines, which show them on their copy (<see cref="Show"/>,
/// <see cref="ShowColours"/>). A look whose sheets carry no palette map keeps its own colours whatever was picked.
/// Visual only: nothing in gameplay reads it.
/// </summary>
[DisallowMultipleComponent]
public class PlayerLook : MonoBehaviour
{
    [SerializeField, Tooltip("Every look a player can be drawn with; a look is named on the wire by its index here.")]
    private CharacterLookCatalog catalog;

    [SerializeField, Tooltip("The character's visuals, which draw the look.")]
    private CharacterVisuals visuals;

    [SerializeField, Tooltip("The colours a player can pick; a choice and its swatch are named on the wire by their index here. None draws every look in its own colours.")]
    private CharacterColours colours;

    [SerializeField, Tooltip("The palette on the character's sprite renderer, which draws the colours. Found under this object when unassigned.")]
    private CharacterPalette palette;

    private UserSettings settings;
    private bool following;
    private bool subscribed;

    /// <summary>
    /// Raised on the owner with the index of each look it shows from the local settings.
    /// </summary>
    public event Action<int> Chosen;

    /// <summary>
    /// Raised on the owner with the colours it shows from the local settings, each time they change.
    /// </summary>
    public event Action<CharacterColourPicks> ColoursChosen;

    public CharacterLookCatalog Catalog => catalog;
    public CharacterColours Colours => colours;

    /// <summary>
    /// The colours shown; the default (every choice's own swatch) until others show.
    /// </summary>
    public CharacterColourPicks ShownColours { get; private set; }

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
    /// Shows <paramref name="picks"/> of the colours on the palette: the look's own colours for the default picks or
    /// without colours.
    /// </summary>
    public void ShowColours(CharacterColourPicks picks)
    {
        ShownColours = picks;
        if (palette != null)
        {
            palette.Show(colours, picks);
        }
    }

    /// <summary>
    /// The colours the local settings pick: for each colour choice the swatch they name, its own for an empty or unknown
    /// name or without settings.
    /// </summary>
    public CharacterColourPicks LocalColours()
    {
        CharacterColourPicks picks = default;
        if (colours == null || settings == null)
        {
            return picks;
        }

        for (int i = 0; i < colours.Count && i < CharacterColourPicks.MaxChoices; i++)
        {
            CharacterColourChoice choice = colours.Get(i);
            if (choice != null)
            {
                picks = colours.Pick(picks, i, settings.CharacterColour(choice.Name));
            }
        }

        return picks;
    }

    /// <summary>
    /// On the owner: shows the look and the colours the local settings name and follows every change to them, raising
    /// <see cref="Chosen"/> and <see cref="ColoursChosen"/> each time, until <see cref="StopFollowing"/>.
    /// </summary>
    public void FollowLocalChoice()
    {
        following = true;
        if (enabled && gameObject.activeInHierarchy)
        {
            Subscribe();
        }

        ShowChoice();
        ShowColourChoice();
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
    /// Shows the colours the local settings pick and raises <see cref="ColoursChosen"/>.
    /// </summary>
    private void ShowColourChoice()
    {
        CharacterColourPicks picks = LocalColours();
        ShowColours(picks);
        ColoursChosen?.Invoke(picks);
    }

    /// <summary>
    /// Follows changes to the local settings' look and colours.
    /// </summary>
    private void Subscribe()
    {
        if (!subscribed && settings != null)
        {
            settings.CharacterLookChanged += ShowChoice;
            settings.CharacterColoursChanged += ShowColourChoice;
            subscribed = true;
        }
    }

    /// <summary>
    /// Stops following changes to the local settings' look and colours.
    /// </summary>
    private void Unsubscribe()
    {
        if (subscribed)
        {
            settings.CharacterLookChanged -= ShowChoice;
            settings.CharacterColoursChanged -= ShowColourChoice;
            subscribed = false;
        }
    }

    /// <summary>
    /// Falls back to the <see cref="CharacterVisuals"/> and <see cref="CharacterPalette"/> under this object when none is
    /// assigned.
    /// </summary>
    private void ResolveReferences()
    {
        if (visuals == null)
        {
            visuals = GetComponentInChildren<CharacterVisuals>(true);
        }

        if (palette == null)
        {
            palette = GetComponentInChildren<CharacterPalette>(true);
        }
    }

    /// <summary>
    /// Test setup without the prefab: sets the catalog and the visuals, and the colours and the palette.
    /// </summary>
    internal void Configure(CharacterLookCatalog lookCatalog, CharacterVisuals lookVisuals, CharacterColours lookColours = null, CharacterPalette lookPalette = null)
    {
        catalog = lookCatalog;
        visuals = lookVisuals;
        colours = lookColours;
        palette = lookPalette;
    }
}

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
/// A look whose character wears parts (Docs/PlayerCharacter.md, Phase 6) shows the outfit its
/// <see cref="CharacterPartPicks"/> pick (<see cref="ShowParts"/>; the owner follows
/// <see cref="UserSettings.CharacterPart"/> per slot, by name, the slot's first option for an empty or unknown one, and
/// raises <see cref="PartsChosen"/>): its own set for its own parts, else the outfit the injected
/// <see cref="CharacterOutfits"/> compose, the set shown before staying until it is ready. Its weapon's slot shows the
/// gear of the weapon its <see cref="PlayerWeaponController"/> has equipped (Docs/PlayerCharacter.md, Phase 7), on
/// every machine from the weapon that machine already shows, whatever the picks say, so nothing about it goes on the
/// wire. The player lets go of its outfit when it shows another or is destroyed. Visual only: nothing in gameplay
/// reads it.
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

    [SerializeField, Tooltip("The weapon whose gear the look shows outside its attacks. Found on this object when unassigned; none shows the look's own gear.")]
    private PlayerWeaponController weapon;

    private UserSettings settings;
    private CharacterOutfits outfits;
    private CharacterOutfitLease outfit;
    private bool following;
    private bool subscribed;
    private bool weaponShown;
    private string gear = string.Empty;

    /// <summary>
    /// Raised on the owner with the index of each look it shows from the local settings.
    /// </summary>
    public event Action<int> Chosen;

    /// <summary>
    /// Raised on the owner with the colours it shows from the local settings, each time they change.
    /// </summary>
    public event Action<CharacterColourPicks> ColoursChosen;

    /// <summary>
    /// Raised on the owner with the parts it shows from the local settings, each time they change.
    /// </summary>
    public event Action<CharacterPartPicks> PartsChosen;

    public CharacterLookCatalog Catalog => catalog;
    public CharacterColours Colours => colours;

    /// <summary>
    /// The colours shown; the default (every choice's own swatch) until others show.
    /// </summary>
    public CharacterColourPicks ShownColours { get; private set; }

    /// <summary>
    /// The parts shown; the default (every slot's first option, the look's own) until others show.
    /// </summary>
    public CharacterPartPicks ShownParts { get; private set; }

    /// <summary>
    /// The parts the look wears: the parts shown, its weapon's slot showing the equipped weapon's gear.
    /// </summary>
    public CharacterPartPicks WornParts => Worn(Parts);

    /// <summary>
    /// The parts the look shown can wear, or null.
    /// </summary>
    public CharacterParts Parts => ShownLook?.Parts;

    /// <summary>
    /// The look shown: the catalog's at <see cref="Index"/>, or its first, the prefab's own, while none was shown.
    /// </summary>
    private CharacterLook ShownLook => catalog != null ? catalog.Get(Index >= 0 ? Index : 0) : null;

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
    /// Follows the weapon's gear, and the local settings again while the owner follows them.
    /// </summary>
    private void OnEnable()
    {
        if (weapon != null)
        {
            weapon.EquippedWeaponChanged += ShowWeapon;
            if (weapon.CurrentWeapon != null)
            {
                ShowWeapon(weapon.CurrentWeapon);
            }
        }

        if (following)
        {
            Subscribe();
        }
    }

    /// <summary>
    /// Stops following the weapon's gear and the local settings while disabled.
    /// </summary>
    private void OnDisable()
    {
        if (weapon != null)
        {
            weapon.EquippedWeaponChanged -= ShowWeapon;
        }

        Unsubscribe();
    }

    /// <summary>
    /// Lets go of the outfit worn.
    /// </summary>
    private void OnDestroy()
    {
        outfit?.Release();
        outfit = null;
    }

    /// <summary>
    /// Takes the local settings the owner's choice is read from, and the outfits that compose its parts.
    /// </summary>
    [Inject]
    public void Construct(UserSettings userSettings, CharacterOutfits characterOutfits)
    {
        settings = userSettings;
        outfits = characterOutfits;
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

        Index = index;
        Wear(look);
        return true;
    }

    /// <summary>
    /// Shows the gear of <paramref name="equipped"/> (none for no weapon) in the look's weapon slot: the option it
    /// names, else one drawing nothing (Docs/PlayerCharacter.md, Phase 7). Called on every machine as the weapon
    /// controller equips a weapon, the owner's switch and a copy's alike. Puts the outfit on again only when the gear
    /// changed.
    /// </summary>
    public void ShowWeapon(PlayerWeapon equipped)
    {
        string next = equipped != null ? equipped.Gear : string.Empty;
        if (weaponShown && next == gear)
        {
            return;
        }

        CharacterParts parts = Parts;
        CharacterPartPicks before = Worn(parts);
        weaponShown = true;
        gear = next;
        CharacterLook look = ShownLook;
        if (look != null && visuals != null && !Worn(parts).Equals(before))
        {
            Wear(look);
        }
    }

    /// <summary>
    /// Shows the outfit <paramref name="picks"/> pick of the look's parts: its own set for its own parts, else the
    /// composed outfit once ready. The weapon's slot keeps showing the equipped weapon's gear.
    /// </summary>
    public void ShowParts(CharacterPartPicks picks)
    {
        ShownParts = picks;
        CharacterLook look = ShownLook;
        if (look != null && visuals != null)
        {
            Wear(look);
        }
    }

    /// <summary>
    /// The parts the local settings pick of the look shown's parts: for each slot the option they name, the first for
    /// an empty or unknown name or without settings; the weapon's slot is left to the weapon.
    /// </summary>
    public CharacterPartPicks LocalParts()
    {
        CharacterPartPicks picks = default;
        CharacterParts parts = Parts;
        if (parts == null || settings == null)
        {
            return picks;
        }

        foreach (CharacterPartSlot slot in parts.Slots)
        {
            if (slot == null || slot.FollowsWeapon)
            {
                continue;
            }

            int option = slot.IndexOf(settings.CharacterPart(slot.Slot.ToString()));
            picks = picks.With(slot.Slot, Mathf.Max(0, option));
        }

        return picks;
    }

    /// <summary>
    /// Puts <paramref name="look"/> on the visuals in the parts shown: its own set at once for its own parts or without
    /// outfits, else the outfit once composed (the set shown before staying meanwhile; the look's own when the look
    /// changed). Lets go of the outfit worn before.
    /// </summary>
    private void Wear(CharacterLook look)
    {
        CharacterOutfitLease previous = outfit;
        outfit = null;
        CharacterParts parts = look.Parts;
        CharacterPartPicks worn = Worn(parts);
        if (parts == null || outfits == null || parts.IsOwn(worn))
        {
            visuals.SetLook(look.AnimationSet, look.Locomotion);
            previous?.Release();
            return;
        }

        if (visuals.AnimationSet != parts.Look && (previous == null || previous.Key.Parts != parts))
        {
            visuals.SetLook(look.AnimationSet, look.Locomotion);
        }

        CharacterOutfitLease lease = null;
        lease = outfits.Wear(parts, worn, set =>
        {
            if (set != null && (lease == null || outfit == lease) && visuals != null)
            {
                visuals.SetLook(set, look.Locomotion);
            }
        });
        outfit = lease;
        if (lease.Set != null)
        {
            visuals.SetLook(lease.Set, look.Locomotion);
        }

        previous?.Release();
    }

    /// <summary>
    /// The parts shown with <paramref name="parts"/>' weapon slot showing the equipped weapon's gear; the look's own
    /// gear until a weapon shows, and the parts shown as they are for a look without a weapon slot.
    /// </summary>
    private CharacterPartPicks Worn(CharacterParts parts)
    {
        CharacterPartSlot slot = parts != null ? parts.Get(CharacterSlot.Weapon) : null;
        return slot == null ? ShownParts : ShownParts.With(CharacterSlot.Weapon, weaponShown ? slot.GearOption(gear) : 0);
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
    /// On the owner: shows the look, the colours and the parts the local settings name and follows every change to them,
    /// raising <see cref="Chosen"/>, <see cref="ColoursChosen"/> and <see cref="PartsChosen"/> each time, until
    /// <see cref="StopFollowing"/>.
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
    /// <see cref="Chosen"/>; then the parts they pick of it.
    /// </summary>
    private void ShowChoice()
    {
        int index = catalog != null ? Mathf.Max(0, catalog.IndexOf(settings?.CharacterLook)) : -1;
        if (Show(index))
        {
            Chosen?.Invoke(index);
        }

        ShowPartChoice();
    }

    /// <summary>
    /// Shows the parts the local settings pick and raises <see cref="PartsChosen"/>.
    /// </summary>
    private void ShowPartChoice()
    {
        CharacterPartPicks picks = LocalParts();
        ShowParts(picks);
        PartsChosen?.Invoke(picks);
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
    /// Follows changes to the local settings' look, colours and parts.
    /// </summary>
    private void Subscribe()
    {
        if (!subscribed && settings != null)
        {
            settings.CharacterLookChanged += ShowChoice;
            settings.CharacterColoursChanged += ShowColourChoice;
            settings.CharacterPartsChanged += ShowPartChoice;
            subscribed = true;
        }
    }

    /// <summary>
    /// Stops following changes to the local settings' look, colours and parts.
    /// </summary>
    private void Unsubscribe()
    {
        if (subscribed)
        {
            settings.CharacterLookChanged -= ShowChoice;
            settings.CharacterColoursChanged -= ShowColourChoice;
            settings.CharacterPartsChanged -= ShowPartChoice;
            subscribed = false;
        }
    }

    /// <summary>
    /// Falls back to the <see cref="CharacterVisuals"/> and <see cref="CharacterPalette"/> under this object, and the
    /// <see cref="PlayerWeaponController"/> on it, when none is assigned.
    /// </summary>
    private void ResolveReferences()
    {
        if (weapon == null)
        {
            weapon = GetComponent<PlayerWeaponController>();
        }

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
    /// Test setup without the prefab: sets the catalog and the visuals, and the colours, the palette, the outfits and
    /// the weapon whose gear shows.
    /// </summary>
    internal void Configure(CharacterLookCatalog lookCatalog, CharacterVisuals lookVisuals, CharacterColours lookColours = null, CharacterPalette lookPalette = null, CharacterOutfits lookOutfits = null, PlayerWeaponController lookWeapon = null)
    {
        weapon = lookWeapon;
        catalog = lookCatalog;
        visuals = lookVisuals;
        colours = lookColours;
        palette = lookPalette;
        outfits = lookOutfits;
    }
}

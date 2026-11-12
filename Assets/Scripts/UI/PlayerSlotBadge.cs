using UnityEngine;

/// <summary>
/// A player's slot badge beside its world bar (Docs/UiRevamp.md, Phase 5), on the <c>WorldBarPlayer</c> component: the
/// skin's small badge in its slot's colour role, read from its <see cref="UiTheme"/>. It follows the player's
/// <see cref="PlayerSlot"/>, so it shows on every machine once the host gave the player a slot, and never offline, and
/// it hides while the player is dead, as the bar does.
/// </summary>
[DisallowMultipleComponent]
public class PlayerSlotBadge : MonoBehaviour
{
    [SerializeField] private PlayerSlot slot;
    [SerializeField] private Health health;
    [SerializeField] private SpriteRenderer badge;
    [SerializeField] private UiTheme theme;

    private int shownNumber = -1;
    private bool shownAlive;
    private bool subscribed;

    internal bool IsShown => badge != null && badge.enabled;

    /// <summary>
    /// Finds the player's slot and health when unassigned; logs an error and disables itself without a slot, a badge
    /// renderer or a theme.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
        if (slot == null || badge == null || theme == null)
        {
            Debug.LogError($"{name} needs a {nameof(PlayerSlot)} above it, a badge renderer and a UI theme.", this);
            enabled = false;
        }
    }

    /// <summary>
    /// Fills in the player's slot and health in the inspector when none are assigned.
    /// </summary>
    private void OnValidate()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Follows the slot and shows it.
    /// </summary>
    private void OnEnable()
    {
        if (slot != null && !subscribed)
        {
            slot.Changed += HandleChanged;
            subscribed = true;
        }

        shownNumber = -1;
        Refresh();
    }

    /// <summary>
    /// Stops following the slot.
    /// </summary>
    private void OnDisable()
    {
        if (slot != null && subscribed)
        {
            slot.Changed -= HandleChanged;
        }

        subscribed = false;
    }

    /// <summary>
    /// Polls whether the player lives, after gameplay updates, since a network sync sets health without an event.
    /// </summary>
    private void LateUpdate()
    {
        Refresh();
    }

    /// <summary>
    /// Shows the badge in the slot's colour while the player has a slot and lives; hides it otherwise. Changes the
    /// renderer only when the slot or the player's life changed.
    /// </summary>
    internal void Refresh()
    {
        if (slot == null || badge == null)
        {
            return;
        }

        int number = slot.Number;
        bool alive = health == null || !health.IsDead;
        if (number == shownNumber && alive == shownAlive)
        {
            return;
        }

        shownNumber = number;
        shownAlive = alive;
        bool shown = alive && PlayerSlots.IsSlot(number);
        badge.enabled = shown;
        if (shown && theme != null)
        {
            badge.color = theme.Colour(PlayerSlots.Role(number));
        }
    }

    /// <summary>
    /// Assigns the slot, health, renderer and theme in code, for tests, and follows the slot.
    /// </summary>
    internal void Configure(PlayerSlot playerSlot, Health playerHealth, SpriteRenderer badgeRenderer, UiTheme uiTheme)
    {
        OnDisable();
        slot = playerSlot;
        health = playerHealth;
        badge = badgeRenderer;
        theme = uiTheme;
        OnEnable();
    }

    /// <summary>
    /// Shows the slot as it changed.
    /// </summary>
    private void HandleChanged(PlayerSlot changed)
    {
        Refresh();
    }

    /// <summary>
    /// Uses the nearest <see cref="PlayerSlot"/> and <see cref="Health"/> on this object or its parents when none is
    /// assigned.
    /// </summary>
    private void ResolveReferences()
    {
        if (slot == null)
        {
            slot = GetComponentInParent<PlayerSlot>();
        }

        if (health == null)
        {
            health = GetComponentInParent<Health>();
        }
    }
}

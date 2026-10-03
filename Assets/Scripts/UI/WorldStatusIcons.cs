using UnityEngine;

/// <summary>
/// Up to a few small debuff icons above a unit's <see cref="WorldHealthBar"/>, one per active debuff: its icon
/// sprite (or a plain square) in its colour. Updated when the unit's statuses change, on every machine.
/// </summary>
[DisallowMultipleComponent]
public class WorldStatusIcons : MonoBehaviour
{
    [SerializeField] private StatusEffects statuses;
    [SerializeField] private SpriteRenderer[] icons = new SpriteRenderer[0];
    [SerializeField, Tooltip("Shown for a debuff without an icon sprite.")] private Sprite fallbackSprite;

    private readonly StatusEffectDefinition[] scratch = new StatusEffectDefinition[8];
    private bool subscribed;

    internal int ShownCount { get; private set; }

    private void Awake()
    {
        ResolveStatuses();
        if (statuses == null || icons == null || icons.Length == 0)
        {
            Debug.LogError($"{name} needs {nameof(StatusEffects)} above it and at least one icon renderer.", this);
            enabled = false;
        }
    }

    private void OnValidate()
    {
        ResolveStatuses();
    }

    private void OnEnable()
    {
        if (statuses != null && !subscribed)
        {
            statuses.Changed += Refresh;
            subscribed = true;
        }

        Refresh();
    }

    private void OnDisable()
    {
        if (statuses != null && subscribed)
        {
            statuses.Changed -= Refresh;
        }

        subscribed = false;
    }

    /// <summary>
    /// Shows the unit's distinct debuffs, in the order they landed, on the first icons and hides the rest.
    /// </summary>
    internal void Refresh()
    {
        ShownCount = 0;
        if (statuses != null && icons != null)
        {
            int limit = Mathf.Min(icons.Length, scratch.Length);
            for (int i = 0; i < statuses.Count && ShownCount < limit; i++)
            {
                StatusEffectDefinition definition = statuses.GetSnapshot(i).Definition;
                if (definition.Kind != StatusKind.Debuff || IsShown(definition))
                {
                    continue;
                }

                SpriteRenderer icon = icons[ShownCount];
                scratch[ShownCount] = definition;
                icon.sprite = definition.Icon != null ? definition.Icon : fallbackSprite;
                icon.color = definition.Color;
                icon.enabled = true;
                ShownCount++;
            }
        }

        for (int i = ShownCount; icons != null && i < icons.Length; i++)
        {
            if (icons[i] != null)
            {
                icons[i].enabled = false;
            }
        }
    }

    internal void Configure(StatusEffects unitStatuses, SpriteRenderer[] iconRenderers, Sprite fallback)
    {
        statuses = unitStatuses;
        icons = iconRenderers;
        fallbackSprite = fallback;
    }

    private bool IsShown(StatusEffectDefinition definition)
    {
        for (int i = 0; i < ShownCount; i++)
        {
            if (scratch[i] == definition)
            {
                return true;
            }
        }

        return false;
    }

    private void ResolveStatuses()
    {
        if (statuses == null)
        {
            statuses = GetComponentInParent<StatusEffects>();
        }
    }
}

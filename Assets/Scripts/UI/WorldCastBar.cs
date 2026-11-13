using UnityEngine;
using VContainer;

/// <summary>
/// A player's cast bar, under its <see cref="WorldHealthBar"/> on every machine (Docs/UiRevamp.md, Phase 4): a sliced
/// frame and a sliced fill that grows from the left over the cast time on the game clock. On the machine that owns the
/// player it follows <see cref="PlayerAbilities.CastStarted"/> and <see cref="PlayerAbilities.CastEnded"/>; on a copy
/// it follows the owner's cast messages, filling from when the cast arrives and staying full until the owner's
/// completion or cancel arrives, or until <see cref="remoteHoldSeconds"/> past the cast time. A cast without a cast
/// time never shows, and the bar hides while the player is dead. Hidden until a cast shows it.
/// </summary>
[DisallowMultipleComponent]
public class WorldCastBar : MonoBehaviour
{
    [SerializeField] private PlayerAbilities abilities;
    [SerializeField] private Health health;
    [SerializeField] private SpriteRenderer frame;
    [SerializeField] private SpriteRenderer fill;
    [SerializeField, Min(0f), Tooltip("Seconds a cast seen from another machine stays full past its cast time while the owner's completion is on its way.")]
    private float remoteHoldSeconds = 0.5f;

    private IClock clock;
    private float fullWidth;
    private float fillHeight;
    private float leftEdge;
    private bool showing;
    private bool remote;
    private float startTime;
    private float castTime;
    private bool subscribed;

    internal float FillFraction { get; private set; }
    internal bool IsShown => showing && frame != null && frame.enabled;

    /// <summary>
    /// Takes the game clock the cast fills on.
    /// </summary>
    [Inject]
    public void Construct(IClock gameClock)
    {
        clock = gameClock;
    }

    /// <summary>
    /// Finds the player's abilities and health when unassigned, caches the fill's full size and left edge and hides
    /// the bar. Logs an error and disables itself without abilities, a frame or a sliced fill.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
        if (abilities == null || frame == null || fill == null || fill.drawMode == SpriteDrawMode.Simple)
        {
            Debug.LogError($"{name} needs {nameof(PlayerAbilities)} above it, a frame renderer and a sliced fill renderer.", this);
            enabled = false;
            return;
        }

        CacheFillGeometry();
        Hide();
    }

    /// <summary>
    /// Fills in the player's abilities and health in the inspector when none are assigned.
    /// </summary>
    private void OnValidate()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Fails loudly when no clock was injected: the bar cannot fill without one.
    /// </summary>
    private void Start()
    {
        if (clock == null)
        {
            Debug.LogError($"{name}'s {nameof(WorldCastBar)} was not injected with an {nameof(IClock)}; it cannot show casts.", this);
        }
    }

    /// <summary>
    /// Follows the owner's casts and a copy's.
    /// </summary>
    private void OnEnable()
    {
        if (abilities == null || subscribed)
        {
            return;
        }

        abilities.CastStarted += ShowOwn;
        abilities.CastEnded += HandleCastEnded;
        abilities.RemoteCastStarted += ShowCopy;
        abilities.RemoteCastCompleted += Hide;
        abilities.RemoteCastCancelled += Hide;
        subscribed = true;
    }

    /// <summary>
    /// Stops following the casts and hides the bar.
    /// </summary>
    private void OnDisable()
    {
        if (abilities != null && subscribed)
        {
            abilities.CastStarted -= ShowOwn;
            abilities.CastEnded -= HandleCastEnded;
            abilities.RemoteCastStarted -= ShowCopy;
            abilities.RemoteCastCompleted -= Hide;
            abilities.RemoteCastCancelled -= Hide;
        }

        subscribed = false;
        Hide();
    }

    /// <summary>
    /// Fills the bar for the cast showing, after gameplay updates.
    /// </summary>
    private void LateUpdate()
    {
        Refresh();
    }

    /// <summary>
    /// Sets the fill to the share of the cast time gone by; hides the bar once the player is dead, or once a copy's
    /// cast has waited its hold past the cast time.
    /// </summary>
    internal void Refresh()
    {
        if (!showing || clock == null)
        {
            return;
        }

        if (health != null && health.IsDead)
        {
            Hide();
            return;
        }

        float elapsed = clock.Time - startTime;
        if (remote && elapsed > castTime + remoteHoldSeconds)
        {
            Hide();
            return;
        }

        SetFill(Mathf.Clamp01(elapsed / castTime));
    }

    /// <summary>
    /// Assigns the abilities, health, renderers and clock in code, for tests that build the bar without a prefab or a
    /// container: caches the fill geometry, hides the bar and follows the casts.
    /// </summary>
    internal void Configure(PlayerAbilities playerAbilities, Health playerHealth, SpriteRenderer frameRenderer, SpriteRenderer fillRenderer, IClock gameClock)
    {
        OnDisable();
        abilities = playerAbilities;
        health = playerHealth;
        frame = frameRenderer;
        fill = fillRenderer;
        clock = gameClock;
        CacheFillGeometry();
        Hide();
        OnEnable();
    }

    /// <summary>
    /// Shows the owner's <paramref name="cast"/>, which its <see cref="PlayerAbilities.CastEnded"/> ends.
    /// </summary>
    private void ShowOwn(AbilityCast cast)
    {
        Show(cast, false);
    }

    /// <summary>
    /// Shows another machine's <paramref name="cast"/> on this copy, which its completion or cancel ends.
    /// </summary>
    private void ShowCopy(AbilityCast cast)
    {
        Show(cast, true);
    }

    /// <summary>
    /// Hides the bar when the owner's cast ends, completed or cancelled.
    /// </summary>
    private void HandleCastEnded(int slot, bool completed)
    {
        Hide();
    }

    /// <summary>
    /// Starts filling the bar for <paramref name="cast"/> from now, or hides it for a cast without a cast time, without
    /// a clock or while the player is dead.
    /// </summary>
    private void Show(AbilityCast cast, bool fromCopy)
    {
        float time = cast.Ability != null ? cast.Ability.CastTime : 0f;
        if (time <= 0f || clock == null || (health != null && health.IsDead))
        {
            Hide();
            return;
        }

        showing = true;
        remote = fromCopy;
        castTime = time;
        startTime = clock.Time;
        frame.enabled = true;
        fill.enabled = true;
        SetFill(0f);
    }

    /// <summary>
    /// Hides the frame and the fill until the next cast.
    /// </summary>
    private void Hide()
    {
        showing = false;
        FillFraction = 0f;
        if (frame != null)
        {
            frame.enabled = false;
        }

        if (fill != null)
        {
            fill.enabled = false;
        }
    }

    /// <summary>
    /// Sizes the fill to <paramref name="fraction"/> of its full width, its left edge kept in place.
    /// </summary>
    private void SetFill(float fraction)
    {
        FillFraction = fraction;
        fill.size = new Vector2(fullWidth * fraction, fillHeight);
        Transform fillTransform = fill.transform;
        Vector3 position = fillTransform.localPosition;
        position.x = leftEdge + fullWidth * fraction * 0.5f;
        fillTransform.localPosition = position;
    }

    /// <summary>
    /// Records the fill's authored size as the full bar and its left edge, so filling keeps it anchored left.
    /// </summary>
    private void CacheFillGeometry()
    {
        fullWidth = fill.size.x;
        fillHeight = fill.size.y;
        leftEdge = fill.transform.localPosition.x - fullWidth * 0.5f;
    }

    /// <summary>
    /// Uses the nearest <see cref="PlayerAbilities"/> and <see cref="Health"/> on this object or its parents when none
    /// is assigned.
    /// </summary>
    private void ResolveReferences()
    {
        if (abilities == null)
        {
            abilities = GetComponentInParent<PlayerAbilities>();
        }

        if (health == null)
        {
            health = GetComponentInParent<Health>();
        }
    }
}

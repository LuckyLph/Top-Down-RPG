using System;
using UnityEngine;
using VContainer;

/// <summary>
/// The lantern every player carries (Docs/Lighting.md, Phase 9), shown burning or out on its
/// <see cref="CharacterVisuals"/>, which draws the lantern's layer and casts its light only while it burns. The owner
/// decides whether it burns (<see cref="DecideLocally"/>): it lights itself as the night lights come up, once the share
/// they burn at in the world shown (<see cref="PropLighting.NightLightsShown"/>) reaches <see cref="AutoShare"/>, at
/// sunset outdoors and at once indoors, and goes out as they fade at sunrise; the lantern key toggles it at any time
/// (<see cref="Toggle"/>), the player's choice holding until the next of those. Each change raises
/// <see cref="Changed"/>, so the network sync can tell the other machines, which show it on their copy
/// (<see cref="Show"/>). Every machine flickers the light it shows at the prop lights' flicker step, as they flicker
/// (<see cref="PropLighting.StrengthAt"/>). Visual only: nothing in gameplay reads it.
/// </summary>
[DisallowMultipleComponent]
public class PlayerLantern : MonoBehaviour
{
    [SerializeField, Tooltip("The character's visuals, which draw the lantern and cast its light.")]
    private CharacterVisuals visuals;

    [SerializeField, Min(0f), Tooltip("The lantern's light at full: its strength at the middle.")]
    private float intensity = 0.8f;

    [SerializeField, Range(0f, 0.5f), Tooltip("How far the light's strength flickers either way, as a share of it, at the prop lights' flicker steps.")]
    private float flicker = 0.04f;

    [SerializeField, Range(0f, 1f), Tooltip("The night lights' share at which the lantern lights itself, and below which it goes out: their share at sunset.")]
    private float autoShare = 0.7f;

    private PropLighting nightLights;
    private bool deciding;
    private bool night;
    private int seed;

    /// <summary>
    /// Raised on the owner with each change to whether the lantern burns.
    /// </summary>
    public event Action<bool> Changed;

    /// <summary>
    /// Whether the lantern burns.
    /// </summary>
    public bool Lit { get; private set; }

    public float Intensity => intensity;
    public float Flicker => flicker;
    public float AutoShare => autoShare;

    /// <summary>
    /// Whether this machine decides when the lantern burns: its owner's.
    /// </summary>
    public bool Deciding => deciding;

    /// <summary>
    /// Finds the visuals when unassigned, picks the light's flicker seed and fails loudly without visuals.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
        seed = GetHashCode();
        if (visuals == null)
        {
            Debug.LogError($"{name} needs {nameof(CharacterVisuals)} to show its lantern.", this);
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
    /// On the owner, follows the night lights; on every machine, shows the lantern burning or out, its light
    /// flickering.
    /// </summary>
    private void Update()
    {
        if (deciding && nightLights != null)
        {
            Decide(nightLights.NightLightsShown);
        }

        Present();
    }

    /// <summary>
    /// Takes the night lights' presenter, whose share the owner follows and whose flicker step the light flickers at.
    /// </summary>
    [Inject]
    public void Construct(PropLighting propLighting)
    {
        nightLights = propLighting;
    }

    /// <summary>
    /// On the owner: decides from now on whether the lantern burns, starting from whether the night lights burn now,
    /// and raises <see cref="Changed"/> when that changes it.
    /// </summary>
    public void DecideLocally()
    {
        deciding = true;
        night = nightLights != null && nightLights.NightLightsShown >= autoShare;
        SetLit(night);
    }

    /// <summary>
    /// Stops deciding (the player despawns).
    /// </summary>
    public void StopDeciding()
    {
        deciding = false;
    }

    /// <summary>
    /// On the owner, lights the lantern or puts it out, the lantern key's choice, held until the night lights next come
    /// up or fade; nothing elsewhere.
    /// </summary>
    public void Toggle()
    {
        if (deciding)
        {
            SetLit(!Lit);
        }
    }

    /// <summary>
    /// On another machine's copy, shows the lantern burning or out as its owner decided.
    /// </summary>
    public void Show(bool lit)
    {
        Lit = lit;
        Present();
    }

    /// <summary>
    /// On the owner, lights the lantern as the night lights' share <paramref name="share"/> reaches
    /// <see cref="AutoShare"/>, and puts it out as it falls below, each only as it crosses.
    /// </summary>
    internal void Decide(float share)
    {
        bool now = share >= autoShare;
        if (now == night)
        {
            return;
        }

        night = now;
        SetLit(now);
    }

    /// <summary>
    /// The light's strength at flicker step <paramref name="step"/>: its intensity flickered by a hash of the step and
    /// this lantern.
    /// </summary>
    internal float StrengthAt(int step)
    {
        return PropLighting.StrengthAt(intensity, flicker, 1f, step, seed);
    }

    /// <summary>
    /// Test setup without the prefab: sets the visuals and the light's intensity and flicker.
    /// </summary>
    internal void Configure(CharacterVisuals lanternVisuals, float lightIntensity, float lightFlicker)
    {
        visuals = lanternVisuals;
        intensity = lightIntensity;
        flicker = lightFlicker;
    }

    /// <summary>
    /// Sets whether the lantern burns, raising <see cref="Changed"/> when that changes.
    /// </summary>
    private void SetLit(bool lit)
    {
        if (Lit == lit)
        {
            return;
        }

        Lit = lit;
        Present();
        Changed?.Invoke(lit);
    }

    /// <summary>
    /// Shows the lantern burning or out on the visuals, its light at its strength at the flicker step now.
    /// </summary>
    private void Present()
    {
        if (visuals != null)
        {
            visuals.ShowLantern(Lit, Lit ? StrengthAt(nightLights != null ? nightLights.FlickerStep : 0) : 0f);
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
}

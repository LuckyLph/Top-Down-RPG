using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// The UI zoom (Docs/DesignSystem.md, Foundations: Scale): how many display pixels one UI px is drawn as. The readable
/// zoom is the display height divided by <see cref="UiScaleSettings.LinesPerZoom"/>, rounded up; it is lowered until
/// <see cref="UiScaleSettings.MinimumLayout"/> fits the display, then raised by the UI size step of the local
/// <see cref="UserSettings"/> where the result still fits. A Main-scope entry point: each tick it compares the display
/// size and the step with the last ones and raises <see cref="Changed"/> when the zoom or the steps that fit change.
/// Local only, never replicated.
/// </summary>
public sealed class UiScale : IStartable, ITickable
{
    private readonly UiScaleSettings settings;
    private readonly UserSettings userSettings;
    private int width = -1;
    private int height = -1;
    private int step = -1;

    /// <summary>
    /// Creates the zoom of <paramref name="scaleSettings"/> with the UI size step of <paramref name="localSettings"/>.
    /// </summary>
    public UiScale(UiScaleSettings scaleSettings, UserSettings localSettings)
    {
        settings = scaleSettings;
        userSettings = localSettings;
    }

    /// <summary>
    /// Raised when <see cref="Factor"/> or <see cref="StepsThatFit"/> changes.
    /// </summary>
    public event Action Changed;

    /// <summary>The UI zoom: display pixels per UI px.</summary>
    public int Factor { get; private set; } = 1;

    /// <summary>How many UI size steps above Normal the display fits now.</summary>
    public int StepsThatFit { get; private set; }

    /// <summary>
    /// The readable zoom for a display <paramref name="displayHeight"/> pixels tall, at least 1.
    /// </summary>
    public static int ReadableZoom(int displayHeight, UiScaleSettings scaleSettings)
    {
        int lines = scaleSettings.LinesPerZoom;
        return Mathf.Max(1, (displayHeight + lines - 1) / lines);
    }

    /// <summary>
    /// The largest zoom at which the minimum layout fits the display, at least 1.
    /// </summary>
    public static int FittingZoom(int displayWidth, int displayHeight, UiScaleSettings scaleSettings)
    {
        Vector2Int layout = scaleSettings.MinimumLayout;
        return Mathf.Max(1, Mathf.Min(displayWidth / layout.x, displayHeight / layout.y));
    }

    /// <summary>
    /// How many UI size steps above Normal fit the display, up to <see cref="UiScaleSettings.MaxSizeStep"/>.
    /// </summary>
    public static int StepsFor(int displayWidth, int displayHeight, UiScaleSettings scaleSettings)
    {
        int fitting = FittingZoom(displayWidth, displayHeight, scaleSettings);
        int normal = Mathf.Min(ReadableZoom(displayHeight, scaleSettings), fitting);
        return Mathf.Clamp(fitting - normal, 0, scaleSettings.MaxSizeStep);
    }

    /// <summary>
    /// The UI zoom for a display of <paramref name="displayWidth"/> by <paramref name="displayHeight"/> at UI size step
    /// <paramref name="sizeStep"/>: the readable zoom lowered to fit, plus the step where it still fits.
    /// </summary>
    public static int ZoomFor(int displayWidth, int displayHeight, int sizeStep, UiScaleSettings scaleSettings)
    {
        int normal = Mathf.Min(ReadableZoom(displayHeight, scaleSettings), FittingZoom(displayWidth, displayHeight, scaleSettings));
        return normal + Mathf.Clamp(sizeStep, 0, StepsFor(displayWidth, displayHeight, scaleSettings));
    }

    /// <summary>
    /// Takes the display size and the UI size step as they are at the start.
    /// </summary>
    public void Start()
    {
        Tick();
    }

    /// <summary>
    /// Takes the display size and the UI size step again when either changed.
    /// </summary>
    public void Tick()
    {
        int sizeStep = userSettings != null ? userSettings.UiSizeStep : 0;
        if (Screen.width != width || Screen.height != height || sizeStep != step)
        {
            Apply(Screen.width, Screen.height, sizeStep);
        }
    }

    /// <summary>
    /// Takes a display of <paramref name="displayWidth"/> by <paramref name="displayHeight"/> and the UI size step
    /// <paramref name="sizeStep"/>, raising <see cref="Changed"/> when the zoom or the steps that fit change.
    /// </summary>
    internal void Apply(int displayWidth, int displayHeight, int sizeStep)
    {
        width = displayWidth;
        height = displayHeight;
        step = sizeStep;
        int factor = ZoomFor(displayWidth, displayHeight, sizeStep, settings);
        int steps = StepsFor(displayWidth, displayHeight, settings);
        if (factor == Factor && steps == StepsThatFit)
        {
            return;
        }

        Factor = factor;
        StepsThatFit = steps;
        Changed?.Invoke();
    }
}

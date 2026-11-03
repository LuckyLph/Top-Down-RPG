using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

/// <summary>
/// The Scene view's Time of Day overlay (Docs/LightingGaps.md, Phase 6): a toggle that shows the open scenes at a time
/// of day, as play lights them, and a slider over the day's phase, 0 to 1, with the phase written beside it; both
/// drive <see cref="NightPreview"/>, which puts everything back when the toggle is off.
/// </summary>
[Overlay(typeof(SceneView), OverlayId, "Time of Day", true)]
public sealed class TimeOfDayOverlay : Overlay
{
    public const string OverlayId = "topdownrpg-time-of-day";

    /// <summary>
    /// The toggle and the slider, set from the preview's state.
    /// </summary>
    public override VisualElement CreatePanelContent()
    {
        NightPreview preview = NightPreview.instance;
        VisualElement root = new() { style = { width = 220 } };
        Toggle toggle = new("Preview") { value = preview.Shown };
        Slider slider = new("Phase", 0f, 1f) { value = preview.Phase };
        Label phase = new(Describe(preview.Phase));
        toggle.RegisterValueChangedCallback(change =>
        {
            if (change.newValue)
            {
                NightPreview.instance.Show(slider.value);
            }
            else
            {
                NightPreview.instance.Hide();
            }
        });
        slider.RegisterValueChangedCallback(change =>
        {
            phase.text = Describe(change.newValue);
            if (NightPreview.instance.Shown)
            {
                NightPreview.instance.Show(change.newValue);
            }
        });
        root.Add(toggle);
        root.Add(slider);
        root.Add(phase);
        return root;
    }

    /// <summary>
    /// The phase written for the overlay, with the night lights' share at it.
    /// </summary>
    private static string Describe(float value)
    {
        DayNightSettings settings = AssetDatabase.LoadAssetAtPath<DayNightSettings>(NightPreview.SettingsPath);
        return settings != null ? $"{value:F2}, night lights {settings.LightAt(value).NightLights:F2}" : $"{value:F2}";
    }
}

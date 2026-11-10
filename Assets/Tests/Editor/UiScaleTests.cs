using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tests for the UI scale (Docs/DesignSystem.md, Phase 3): the UI zoom and the size steps at every display of the Scale
/// table, the project's settings matching the Pixel Perfect Camera, the zoom following the local UI size step, and
/// <see cref="UiPixelScale"/> keeping a canvas on it. Caption text's readability at every display is in
/// <c>UiThemeTests</c>, from the theme's font.
/// </summary>
public class UiScaleTests
{
    private const string SettingsPath = "Assets/Data/UI/UiScale.asset";
    private const string MainScenePath = "Assets/Scenes/Main.unity";

    internal static readonly int[][] Displays =
    {
        new[] { 1280, 720, 2, 0 },
        new[] { 1366, 768, 2, 0 },
        new[] { 1024, 768, 2, 0 },
        new[] { 1536, 864, 3, 0 },
        new[] { 1440, 900, 3, 0 },
        new[] { 1600, 900, 3, 0 },
        new[] { 1680, 1050, 3, 0 },
        new[] { 1920, 1080, 3, 1 },
        new[] { 1920, 1010, 3, 0 },
        new[] { 1920, 1200, 4, 0 },
        new[] { 2560, 1440, 4, 1 },
        new[] { 2560, 1600, 5, 0 },
        new[] { 3840, 2160, 6, 2 },
    };

    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys every object the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// Every display of the Scale table gets its zoom and its steps, each step adding one zoom where it fits, and the
    /// minimum layout fits at the largest.
    /// </summary>
    [Test]
    public void EveryDisplay_GetsTheZoomAndTheStepsOfTheScaleTable()
    {
        UiScaleSettings settings = Track(UiScaleSettings.Create());
        foreach (int[] display in Displays)
        {
            int width = display[0];
            int height = display[1];
            string name = $"{width} x {height}";
            Assert.That(UiScale.ZoomFor(width, height, 0, settings), Is.EqualTo(display[2]), name);
            Assert.That(UiScale.StepsFor(width, height, settings), Is.EqualTo(display[3]), name);
            for (int step = 0; step <= 3; step++)
            {
                int zoom = UiScale.ZoomFor(width, height, step, settings);
                Assert.That(zoom, Is.EqualTo(display[2] + Mathf.Min(step, display[3])), $"{name} at step {step}");
                Assert.That(width / zoom, Is.GreaterThanOrEqualTo(480), $"{name} at step {step}: the layout's width fits");
                Assert.That(height / zoom, Is.GreaterThanOrEqualTo(270), $"{name} at step {step}: the layout's height fits");
            }
        }
    }

    /// <summary>
    /// The project's settings are the Scale table's (a 480 x 270 layout, 390 lines a step, two steps), and at 720p,
    /// 1080p, 1440p and 4K the UI zoom equals the Main scene's Pixel Perfect Camera's, so a UI px is an art pixel there.
    /// </summary>
    [Test]
    public void TheProjectSettings_MatchTheCameraAtTheCommonDisplays()
    {
        UiScaleSettings settings = AssetDatabase.LoadAssetAtPath<UiScaleSettings>(SettingsPath);
        Assert.That(settings, Is.Not.Null);
        Assert.That(settings.MinimumLayout, Is.EqualTo(new Vector2Int(480, 270)));
        Assert.That(settings.LinesPerZoom, Is.EqualTo(390));
        Assert.That(settings.MaxSizeStep, Is.EqualTo(2));

        string scene = File.ReadAllText(MainScenePath);
        int referenceWidth = int.Parse(Regex.Match(scene, @"m_RefResolutionX: (\d+)").Groups[1].Value);
        int referenceHeight = int.Parse(Regex.Match(scene, @"m_RefResolutionY: (\d+)").Groups[1].Value);
        Assert.That(scene, Does.Contain("uiScaleSettings: {fileID: 11400000, guid: " + AssetDatabase.AssetPathToGUID(SettingsPath)), "The Main scope names the settings.");
        foreach (Vector2Int display in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3840, 2160) })
        {
            int cameraZoom = Mathf.Max(1, Mathf.Min(display.x / referenceWidth, display.y / referenceHeight));
            Assert.That(UiScale.ZoomFor(display.x, display.y, 0, settings), Is.EqualTo(cameraZoom), display.ToString());
        }
    }

    /// <summary>
    /// The zoom takes the local UI size step where it fits, raises its change once per change of the zoom or the steps
    /// that fit, and not for a display that changes neither.
    /// </summary>
    [Test]
    public void TheZoom_FollowsTheSizeStep_AndRaisesItsChangesOnce()
    {
        UiScaleSettings settings = Track(UiScaleSettings.Create());
        UserSettings user = new(new MemorySaveStore());
        UiScale scale = new(settings, user);
        int changes = 0;
        scale.Changed += () => changes++;

        user.SetUiSizeStep(2);
        scale.Apply(1920, 1080, user.UiSizeStep);
        Assert.That(scale.Factor, Is.EqualTo(4), "Large is as far as 1080p goes.");
        Assert.That(scale.StepsThatFit, Is.EqualTo(1));
        Assert.That(changes, Is.EqualTo(1));

        scale.Apply(1920, 1081, user.UiSizeStep);
        Assert.That(changes, Is.EqualTo(1), "Nothing changed for a display one line taller.");

        scale.Apply(1280, 720, user.UiSizeStep);
        Assert.That(scale.Factor, Is.EqualTo(2));
        Assert.That(scale.StepsThatFit, Is.EqualTo(0));
        Assert.That(changes, Is.EqualTo(2));

        scale.Apply(3840, 2160, user.UiSizeStep);
        Assert.That(scale.Factor, Is.EqualTo(8));
        Assert.That(changes, Is.EqualTo(3));
    }

    /// <summary>
    /// A canvas's <see cref="UiPixelScale"/>, once given the zoom, puts its scaler at a constant pixel size of 32
    /// reference pixels per unit and the zoom, and follows every change.
    /// </summary>
    [Test]
    public void ACanvas_FollowsTheZoom()
    {
        UiScaleSettings settings = Track(UiScaleSettings.Create());
        UiScale scale = new(settings, new UserSettings(new MemorySaveStore()));
        scale.Apply(1920, 1080, 0);
        GameObject canvas = Track(new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler)));
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referencePixelsPerUnit = 100f;

        canvas.AddComponent<UiPixelScale>().Construct(scale);
        Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ConstantPixelSize));
        Assert.That(scaler.referencePixelsPerUnit, Is.EqualTo(32f));
        Assert.That(scaler.scaleFactor, Is.EqualTo(3f));

        scale.Apply(3840, 2160, 0);
        Assert.That(scaler.scaleFactor, Is.EqualTo(6f));
    }

    /// <summary>
    /// Keeps <paramref name="instance"/> for <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}

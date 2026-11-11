using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using UnityEngine.UI;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for the command bar's health (Docs/UiRevamp.md, Phase 2): <see cref="PlayerHudPresenter"/> on a
/// <see cref="HealthBarView"/> built in code, over a real <see cref="Health"/>: "72 / 100" with a tick every
/// <see cref="HudSettings.TickHealth"/> HP, the part just lost shown for the recent-loss time from the highest health of
/// the hits in it, a heal past it hiding it, the frame in Danger at or under the low share and pulsing, steady while the
/// player reduces flashing, a change a network sync made silently caught on the next tick, and no allocation per frame.
/// </summary>
public class PlayerHudPresenterTests
{
    private readonly List<Object> created = new();
    private ManualClock clock;
    private LocalPlayerTracker localPlayer;
    private HealthBarView bar;
    private UserSettings preferences;
    private PlayerHudPresenter presenter;
    private Health health;

    /// <summary>
    /// Builds the bar and a player of 100 HP, and starts a presenter on them with the default HUD settings.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock();
        clock.Advance(10f);
        localPlayer = new LocalPlayerTracker();
        preferences = new UserSettings(new MemorySaveStore());
        bar = CreateBar();
        GameObject hudObject = Track(new GameObject("Hud"));
        PlayerHudView view = hudObject.AddComponent<PlayerHudView>();
        view.ConfigureReferences(bar, null);
        presenter = new PlayerHudPresenter(localPlayer, view, clock, Track(HudSettings.Create()), preferences);
        presenter.Start();
        GameObject player = Track(new GameObject("Player"));
        PlayerController controller = player.AddComponent<PlayerController>();
        health = player.AddComponent<Health>();
        UnityEditor.SerializedObject healthSettings = new(health);
        healthSettings.FindProperty("maxHealth").intValue = 100;
        healthSettings.ApplyModifiedPropertiesWithoutUndo();
        localPlayer.Assign(new LocalPlayer(controller));
    }

    /// <summary>
    /// Disposes the presenter and destroys every object the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        presenter?.Dispose();
        presenter = null;
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
    /// The bar reads "72 / 100" and fills to 72%, with a tick every 10 HP below the maximum (nine), each at its share of
    /// the width, from the top.
    /// </summary>
    [Test]
    public void TheBar_ReadsCurrentOverMax_WithATickEveryTenHealth()
    {
        Assert.That(health.MaxHealth, Is.EqualTo(100));
        health.ApplyDamage(28);

        Assert.That(bar.Text.text, Is.EqualTo("72 / 100"));
        Assert.That(bar.Fill.fillAmount, Is.EqualTo(0.72f).Within(0.0001f));
        Assert.That(bar.TickCount, Is.EqualTo(9));
        for (int i = 0; i < bar.TickCount; i++)
        {
            RectTransform tick = bar.Tick(i).rectTransform;
            Assert.That(tick.anchorMin.x, Is.EqualTo((i + 1) / 10f).Within(0.0001f), $"Tick {i + 1}");
            Assert.That(tick.anchorMin.y, Is.EqualTo(1f));
            Assert.That(tick.gameObject.activeSelf, Is.True);
        }
    }

    /// <summary>
    /// A hit shows the part just lost, from the health before it, for the recent-loss time; a second hit within it
    /// keeps the highest start and restarts the time; a heal past that start hides it.
    /// </summary>
    [Test]
    public void ALoss_ShowsForItsTime_FromTheHighestHealthOfTheHitsInIt()
    {
        health.ApplyDamage(20);
        Assert.That(bar.Lost.enabled, Is.True);
        Assert.That(bar.Lost.fillAmount, Is.EqualTo(1f).Within(0.0001f), "Lost from the full bar.");

        clock.Advance(0.3f);
        presenter.Tick();
        health.ApplyDamage(8);
        Assert.That(bar.Lost.fillAmount, Is.EqualTo(1f).Within(0.0001f), "A second hit keeps the highest start.");

        clock.Advance(0.3f);
        presenter.Tick();
        Assert.That(bar.Lost.enabled, Is.True, "The second hit restarted the time.");

        clock.Advance(0.15f);
        presenter.Tick();
        Assert.That(bar.Lost.enabled, Is.False);

        health.ApplyDamage(10);
        Assert.That(bar.Lost.fillAmount, Is.EqualTo(0.72f).Within(0.0001f));
        health.Heal(30);
        Assert.That(bar.Lost.enabled, Is.False, "A heal past the loss hides it.");
    }

    /// <summary>
    /// At or under 30% the frame turns Danger and pulses once a second down to the floor and back; above it the frame is
    /// Border again.
    /// </summary>
    [Test]
    public void LowHealth_TurnsTheFrameDanger_AndPulsesIt()
    {
        health.ApplyDamage(69);
        Assert.That(bar.IsLow, Is.False, "31 HP is not low.");
        Assert.That(bar.Frame.color, Is.EqualTo(TestThemes.Shared.Colour(UiColour.Border)));

        health.ApplyDamage(1);
        Assert.That(bar.IsLow, Is.True, "30 HP is low.");
        Color danger = TestThemes.Shared.Colour(UiColour.Danger);
        float[] alphas = new float[4];
        for (int i = 0; i < alphas.Length; i++)
        {
            clock.Advance(0.25f);
            presenter.Tick();
            alphas[i] = bar.Frame.color.a;
            Assert.That((Vector3)(Vector4)bar.Frame.color, Is.EqualTo((Vector3)(Vector4)danger));
        }

        Assert.That(Mathf.Min(alphas), Is.EqualTo(0.35f).Within(0.02f), "The pulse dims to its floor.");
        Assert.That(Mathf.Max(alphas), Is.EqualTo(1f).Within(0.02f), "And comes back once a second.");

        health.Heal(10);
        Assert.That(bar.IsLow, Is.False);
        Assert.That(bar.Frame.color, Is.EqualTo(TestThemes.Shared.Colour(UiColour.Border)));
    }

    /// <summary>
    /// With Reduce flashing on, a low bar's frame holds at full Danger.
    /// </summary>
    [Test]
    public void LowHealth_HoldsSteady_WithReduceFlashingOn()
    {
        preferences.SetReduceFlashing(true);
        health.ApplyDamage(80);

        for (int i = 0; i < 4; i++)
        {
            clock.Advance(0.25f);
            presenter.Tick();
            Assert.That(bar.Frame.color, Is.EqualTo(TestThemes.Shared.Colour(UiColour.Danger)));
        }
    }

    /// <summary>
    /// A change a network sync makes without an event (<see cref="Health.SyncTo"/>) shows on the next tick, the loss
    /// included.
    /// </summary>
    [Test]
    public void ASilentSync_ShowsOnTheNextTick()
    {
        health.SyncTo(55);
        Assert.That(bar.Text.text, Is.EqualTo("100 / 100"), "No event yet.");

        presenter.Tick();
        Assert.That(bar.Text.text, Is.EqualTo("55 / 100"));
        Assert.That(bar.Lost.enabled, Is.True);
    }

    /// <summary>
    /// Ticking a low, pulsing bar with a loss showing allocates no managed memory.
    /// </summary>
    [Test]
    public void Tick_DoesNotAllocate()
    {
        health.ApplyDamage(80);
        presenter.Tick();

        TestDelegate tick = () =>
        {
            clock.Advance(0.01f);
            presenter.Tick();
        };

        Assert.That(tick, Is.Not.AllocatingGCMemory());
    }

    /// <summary>
    /// A bar of fill, lost part, frame, text and a hidden tick template under a ticks parent, on the test theme.
    /// </summary>
    private HealthBarView CreateBar()
    {
        GameObject root = Track(new GameObject("Bar", typeof(RectTransform), typeof(Image)));
        Image fill = Child<Image>(root, "Fill");
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        Image lost = Child<Image>(root, "Lost");
        Image frame = Child<Image>(root, "Frame");
        TextMeshProUGUI text = Child<TextMeshProUGUI>(root, "Text");
        GameObject ticks = new("Ticks", typeof(RectTransform));
        ticks.transform.SetParent(root.transform, false);
        Image tick = Child<Image>(ticks, "TickTemplate");
        tick.gameObject.SetActive(false);
        HealthBarView view = root.AddComponent<HealthBarView>();
        view.ConfigureReferences(fill, lost, frame, text, tick, TestThemes.Shared);
        return view;
    }

    /// <summary>
    /// Adds a child named <paramref name="name"/> under <paramref name="parent"/> with a <typeparamref name="T"/>.
    /// </summary>
    private static T Child<T>(GameObject parent, string name) where T : Component
    {
        GameObject child = new(name, typeof(RectTransform));
        child.transform.SetParent(parent.transform, false);
        return child.AddComponent<T>();
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

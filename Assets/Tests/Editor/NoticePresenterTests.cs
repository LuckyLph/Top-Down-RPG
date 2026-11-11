using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for the HUD's notice line (Docs/UiRevamp.md, Phase 3): <see cref="NoticePresenter"/> on a
/// <see cref="NoticeView"/> built in code, on a manual clock. Notices queue, each for the notice time; one raised again
/// while it shows restarts it, and one waiting is not queued twice; their time stands still during a transition. The
/// local player's death counts down from the respawn delay in whole seconds, then says "Respawning..." until it is
/// restored, holding the line while the queue waits. Entering another world, and the local player's waystone discovery,
/// raise their notices, nobody else's. Ticking allocates nothing.
/// </summary>
public class NoticePresenterTests
{
    private const float NoticeSeconds = 3f;
    private const string First = "First notice";
    private const string Second = "Second notice";

    private readonly List<Object> created = new();
    private ManualClock clock;
    private LocalPlayerTracker localPlayer;
    private PlayerRegistry players;
    private PlayerWorlds playerWorlds;
    private WaystoneNotices waystones;
    private GameFlow gameFlow;
    private GameplaySettings settings;
    private NoticeView view;
    private NoticePresenter presenter;
    private Health health;
    private LocalPlayer local;

    /// <summary>
    /// Builds the notice line and a local player of 100 HP in no world, and starts a presenter on them with a 3 s
    /// respawn delay and notice time.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock();
        clock.Advance(10f);
        localPlayer = new LocalPlayerTracker();
        players = new PlayerRegistry();
        playerWorlds = new PlayerWorlds(players);
        waystones = new WaystoneNotices();
        gameFlow = new GameFlow(null, null, null, null);
        settings = Track(GameplaySettings.Create(respawnDelay: 3f));
        view = CreateView();
        presenter = new NoticePresenter(view, Track(HudSettings.Create(noticeSeconds: NoticeSeconds)), settings, clock, localPlayer, playerWorlds, waystones, gameFlow);
        presenter.Start();
        GameObject player = Track(new GameObject("Player"));
        PlayerController controller = player.AddComponent<PlayerController>();
        health = player.AddComponent<Health>();
        SerializedObject healthSettings = new(health);
        healthSettings.FindProperty("maxHealth").intValue = 100;
        healthSettings.ApplyModifiedPropertiesWithoutUndo();
        local = new LocalPlayer(controller);
        players.Add(local.Handle);
        localPlayer.Assign(local);
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
    /// Nothing shows at first. A notice shows at once and the next waits; each shows for the notice time, then the
    /// next, then the line hides.
    /// </summary>
    [Test]
    public void Notices_Queue_EachForTheNoticeTime()
    {
        Assert.That(view.IsShown, Is.False);

        presenter.Show(First);
        presenter.Show(Second);
        Assert.That(view.IsShown, Is.True);
        Assert.That(view.Text, Is.EqualTo(First));
        Assert.That(presenter.WaitingCount, Is.EqualTo(1));

        Advance(NoticeSeconds - 0.1f);
        Assert.That(view.Text, Is.EqualTo(First));
        Advance(0.2f);
        Assert.That(view.Text, Is.EqualTo(Second), "The next notice shows once the first one's time is up.");
        Advance(NoticeSeconds + 0.1f);
        Assert.That(view.IsShown, Is.False, "The line hides once the queue is empty.");
        Assert.That(presenter.Shown, Is.Null);
    }

    /// <summary>
    /// A notice raised again while it shows restarts its time; one raised again while it waits is not queued twice; an
    /// empty notice is ignored.
    /// </summary>
    [Test]
    public void ANoticeRepeatedWhileItShows_RestartsItsTime_AndAWaitingOneIsNotQueuedTwice()
    {
        presenter.Show(First);
        Advance(2f);
        presenter.Show(First);
        Advance(2f);
        Assert.That(view.Text, Is.EqualTo(First), "The repeat restarted the notice's time.");

        presenter.Show(Second);
        presenter.Show(Second);
        presenter.Show(string.Empty);
        presenter.Show(null);
        Assert.That(presenter.WaitingCount, Is.EqualTo(1));
        Advance(1.1f);
        Assert.That(view.Text, Is.EqualTo(Second));
        Advance(NoticeSeconds + 0.1f);
        Assert.That(view.IsShown, Is.False, "The second notice showed once.");
    }

    /// <summary>
    /// While a transition runs, behind the fade, the shown notice's time stands still.
    /// </summary>
    [Test]
    public void DuringATransition_TheNoticesTimeStandsStill()
    {
        presenter.Show(First);
        SetTransitioning(true);
        Advance(NoticeSeconds * 2f);
        Assert.That(view.Text, Is.EqualTo(First));

        SetTransitioning(false);
        Advance(NoticeSeconds - 0.1f);
        Assert.That(view.Text, Is.EqualTo(First), "The notice shows its whole time once the screen fades in.");
        Advance(0.2f);
        Assert.That(view.IsShown, Is.False);
    }

    /// <summary>
    /// The local player's death shows "You fell. Respawning in 3", counting whole seconds down from the respawn delay,
    /// then "Respawning..." until it is restored, the very same strings each second; a notice that showed waits behind it
    /// with its time standing still, and shows again once the player stands.
    /// </summary>
    [Test]
    public void TheDeathNotice_CountsDown_ThenSaysRespawning_UntilThePlayerStands()
    {
        presenter.Show(First);
        Advance(1f);
        health.ApplyDamage(100);
        Assert.That(view.Text, Is.EqualTo("You fell. Respawning in 3"), "The death notice shows on the frame of the death.");
        string three = view.Text;

        Advance(0.5f);
        Assert.That(view.Text, Is.SameAs(three), "The same text, not one built again.");
        Advance(0.6f);
        Assert.That(view.Text, Is.EqualTo("You fell. Respawning in 2"));
        Advance(1f);
        Assert.That(view.Text, Is.EqualTo("You fell. Respawning in 1"));
        Advance(1f);
        Assert.That(view.Text, Is.EqualTo(NoticePresenter.Respawning));
        Advance(10f);
        Assert.That(view.Text, Is.EqualTo(NoticePresenter.Respawning), "It holds until the player stands again.");

        health.Restore();
        Assert.That(view.Text, Is.EqualTo(First), "The notice the death interrupted shows again.");
        Advance(NoticeSeconds - 1.1f);
        Assert.That(view.Text, Is.EqualTo(First), "Its time stood still while the player was dead.");
        Advance(0.2f);
        Assert.That(view.IsShown, Is.False);
    }

    /// <summary>
    /// A local player found dead as it is bound, whose death this machine did not see, shows "Respawning..." at once.
    /// </summary>
    [Test]
    public void ALocalPlayerFoundDead_ShowsRespawning()
    {
        localPlayer.Assign(null);
        health.ApplyDamage(100);
        Assert.That(view.IsShown, Is.False);

        localPlayer.Assign(local);
        Assert.That(view.Text, Is.EqualTo(NoticePresenter.Respawning));
    }

    /// <summary>
    /// Entering a world announces it by its display name: at the start, on another world, and not on a trip within the
    /// world (a respawn's included); another player's world changes announce nothing.
    /// </summary>
    [Test]
    public void EnteringAnotherWorld_AnnouncesItsDisplayName_ATripWithinItDoesNot()
    {
        WorldDefinition home = Track(WorldDefinition.Create("Home", new Vector2Int(2048, 0), Vector2Int.one, new[] { Vector2Int.zero }, new WorldDefinition.Entry("start", new Vector2(10.5f, 10.5f), true, "Start")));
        WorldDefinition away = Track(WorldDefinition.Create("Away", new Vector2Int(4096, 0), Vector2Int.one, new[] { Vector2Int.zero }, new WorldDefinition.Entry("start", new Vector2(10.5f, 10.5f), true, "Start")));
        PlayerHandle other = new(Track(new GameObject("Other")).transform, null);
        players.Add(other);

        playerWorlds.Set(local.Handle, home);
        Assert.That(view.Text, Is.EqualTo("Entering the Home"));
        Advance(NoticeSeconds + 0.1f);

        playerWorlds.Travel(local.Handle, home, "start");
        playerWorlds.Set(other, away);
        Advance(0.1f);
        Assert.That(view.IsShown, Is.False, "A trip within the world, and another player's world, announce nothing.");

        playerWorlds.Travel(local.Handle, away, "start");
        Assert.That(view.Text, Is.EqualTo("Entering the Away"));
    }

    /// <summary>
    /// A local player bound while it already stands in a world, as a joining client's is, announces that world.
    /// </summary>
    [Test]
    public void ALocalPlayerBoundInAWorld_AnnouncesIt()
    {
        WorldDefinition home = Track(WorldDefinition.Create("Home", new Vector2Int(2048, 0), Vector2Int.one, new[] { Vector2Int.zero }, new WorldDefinition.Entry("start", new Vector2(10.5f, 10.5f), true, "Start")));
        localPlayer.Assign(null);
        playerWorlds.Set(local.Handle, home);
        Assert.That(view.IsShown, Is.False);

        localPlayer.Assign(local);
        Assert.That(view.Text, Is.EqualTo("Entering the Home"));
    }

    /// <summary>
    /// The local player's waystone discovery shows "Waystone discovered"; another player's shows nothing.
    /// </summary>
    [Test]
    public void TheLocalPlayersDiscovery_ShowsTheWaystoneNotice_AnotherPlayersDoesNot()
    {
        PlayerHandle other = new(Track(new GameObject("Other")).transform, null);
        players.Add(other);

        waystones.Raise(other, "east");
        Assert.That(view.IsShown, Is.False);

        waystones.Raise(local.Handle, "east");
        Assert.That(view.Text, Is.EqualTo(settings.WaystoneDiscoveredText));
    }

    /// <summary>
    /// Ticking while a notice shows, and while the death countdown runs, allocates no managed memory.
    /// </summary>
    [Test]
    public void Tick_DoesNotAllocate()
    {
        presenter.Show(First);
        presenter.Show(Second);
        presenter.Tick();
        TestDelegate tick = () =>
        {
            clock.Advance(0.01f);
            presenter.Tick();
        };

        Assert.That(tick, Is.Not.AllocatingGCMemory());

        health.ApplyDamage(100);
        presenter.Tick();
        Assert.That(tick, Is.Not.AllocatingGCMemory());
    }

    /// <summary>
    /// Moves the clock on by <paramref name="seconds"/> and ticks.
    /// </summary>
    private void Advance(float seconds)
    {
        clock.Advance(seconds);
        presenter.Tick();
    }

    /// <summary>
    /// Sets whether the flow runs a transition, as its fades do.
    /// </summary>
    private void SetTransitioning(bool transitioning)
    {
        typeof(GameFlow).GetProperty(nameof(GameFlow.IsTransitioning)).SetValue(gameFlow, transitioning);
    }

    /// <summary>
    /// A hidden notice line: a panel with a text child, the view naming the text.
    /// </summary>
    private NoticeView CreateView()
    {
        GameObject panel = Track(new GameObject("Notice", typeof(RectTransform)));
        GameObject label = new("NoticeLine", typeof(RectTransform));
        label.transform.SetParent(panel.transform, false);
        TextMeshProUGUI text = label.AddComponent<TextMeshProUGUI>();
        text.fontSize = 8f;
        NoticeView notice = panel.AddComponent<NoticeView>();
        SerializedObject serialized = new(notice);
        serialized.FindProperty("text").objectReferenceValue = text;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        panel.SetActive(false);
        return notice;
    }

    /// <summary>
    /// Keeps <paramref name="instance"/> for destruction after the test.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}

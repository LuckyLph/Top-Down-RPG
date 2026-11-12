using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tests for Play together (<see cref="PlayTogetherController"/>, Docs/UiRevamp.md, Phase 9) on the real prefab, over a
/// fake game flow and network connection: what opening shows and selects, the focus order across the two columns and
/// Back, the field's check, the connecting dialog's count and Cancel, each failure on the notice line, hosting that
/// fails, the recent hosts, Copy, and Back.
/// </summary>
public class PlayTogetherControllerTests
{
    private const string ScreenPath = "Assets/Prefabs/UI/PlayTogether.prefab";

    private MenuTestRig rig;
    private PlayTogetherView view;
    private PlayTogetherController controller;
    private int closes;

    /// <summary>
    /// Builds the rig and a controller over an instance of the screen.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        rig = new MenuTestRig();
        GameObject screen = rig.Track((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ScreenPath)));
        view = screen.GetComponent<PlayTogetherView>();
        controller = rig.CreatePlayTogether(view);
        closes = 0;
    }

    /// <summary>
    /// Disposes the controller and the rig.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        controller.Dispose();
        rig.Dispose();
    }

    /// <summary>
    /// With a save, opening shows Host and continue with the Continue line and selects it, the field holding the host
    /// joined last and the recent hosts newest first; it pushes a menu layer.
    /// </summary>
    [Test]
    public void Opening_WithASave_SelectsHostAndContinue_AndFillsTheFieldWithTheLastHost()
    {
        rig.WriteSave(MenuTestRig.NearNorth, 3d * 3600d + 12d * 60d);
        rig.Settings.AddRecentHost("192.168.1.31");
        rig.Settings.AddRecentHost("192.168.1.20:7778");
        rig.Settings.AddRecentHost("100.101.102.103");

        Open();

        Assert.That(view.IsShown, Is.True);
        Assert.That(view.HostContinueButton.gameObject.activeSelf, Is.True);
        Assert.That(rig.Line.TryGet(out string line), Is.True);
        Assert.That(view.ContinueText, Is.EqualTo(line));
        Assert.That(rig.Selection.Current, Is.EqualTo(view.HostContinueButton.gameObject));
        Assert.That(view.Field.text, Is.EqualTo("100.101.102.103"));
        Assert.That(ShownRecent(), Is.EqualTo(new[] { "100.101.102.103", "192.168.1.20:7778", "192.168.1.31" }));
        Assert.That(rig.Menus.Top, Is.SameAs(controller));
    }

    /// <summary>
    /// The first time, with no save, no recent host and no network: Host and continue and "Recent" hide, Host a new game
    /// is selected, the field is empty and the invite block says no network was found.
    /// </summary>
    [Test]
    public void TheFirstTime_ShowsLittle_AndSelectsHostANewGame()
    {
        Open();

        Assert.That(view.HostContinueButton.gameObject.activeSelf, Is.False);
        Assert.That(rig.Selection.Current, Is.EqualTo(view.HostNewButton.gameObject));
        Assert.That(view.Field.text, Is.Empty);
        Assert.That(ShownRecent(), Is.Empty);
        Assert.That(view.Invite.ShownAddresses, Is.Zero);
    }

    /// <summary>
    /// Up and Down move along a column through Back, Left and Right across to the same row of the other column, and
    /// Back's Up and Down follow the column the selection was last in.
    /// </summary>
    [Test]
    public void TheFocusOrder_RunsDownEachColumnThroughBack_AndAcrossByRow()
    {
        rig.WriteSave(MenuTestRig.NearNorth, 600d);
        rig.AddAddresses();
        rig.Settings.AddRecentHost("192.168.1.31");
        rig.Settings.AddRecentHost("192.168.1.20");
        Open();

        Selectable[] host = { view.HostContinueButton, view.HostNewButton, view.Invite.CopyButton(0), view.Invite.CopyButton(1) };
        Selectable[] join = { view.Field, view.JoinButton, view.RecentButton(0), view.RecentButton(1) };
        for (int i = 0; i < host.Length; i++)
        {
            Assert.That(host[i].navigation.selectOnDown, Is.EqualTo(i < host.Length - 1 ? host[i + 1] : view.BackButton), $"Down from host {i}");
            Assert.That(host[i].navigation.selectOnUp, Is.EqualTo(i > 0 ? host[i - 1] : view.BackButton), $"Up from host {i}");
            Assert.That(host[i].navigation.selectOnRight, Is.EqualTo(join[i]), $"Right from host {i}");
            Assert.That(join[i].navigation.selectOnLeft, Is.EqualTo(host[i]), $"Left from join {i}");
            Assert.That(join[i].navigation.selectOnDown, Is.EqualTo(i < join.Length - 1 ? join[i + 1] : view.BackButton), $"Down from join {i}");
        }

        Assert.That(view.BackButton.navigation.selectOnUp, Is.EqualTo(host[3]));
        Assert.That(view.BackButton.navigation.selectOnDown, Is.EqualTo(host[0]));
        rig.Selection.Select(view.JoinButton);
        controller.Tick();
        Assert.That(view.BackButton.navigation.selectOnUp, Is.EqualTo(join[3]), "Back goes up to the column the selection came from.");
        Assert.That(view.BackButton.navigation.selectOnDown, Is.EqualTo(join[0]));
    }

    /// <summary>
    /// An empty or malformed address says how to type one and leaves the field selected, without connecting.
    /// </summary>
    [TestCase("")]
    [TestCase("192.168.1.300")]
    [TestCase("192.168.1.20:99999")]
    public void AnEmptyOrMalformedAddress_SaysHowToTypeOne_AndSelectsTheField(string typed)
    {
        Open();
        view.Field.text = typed;

        view.JoinButton.onClick.Invoke();

        Assert.That(view.Notice.text, Is.EqualTo(PlayTogetherController.InvalidAddress));
        Assert.That(rig.Selection.Current, Is.EqualTo(view.Field.gameObject));
        Assert.That(rig.Connection.Joins, Is.Zero);
        Assert.That(controller.IsConnecting, Is.False);
    }

    /// <summary>
    /// Join shows the connecting dialog with the address and a count of whole seconds against the timeout, selects Cancel
    /// the tick after, ignores a Cancel in the tick the join started, and a Cancel later ends the join, hides the dialog,
    /// says nothing and selects Join.
    /// </summary>
    [Test]
    public void Join_ShowsTheConnectingDialog_CountsSeconds_AndCancelEndsIt()
    {
        Open();
        view.Field.text = "192.168.1.20";

        view.JoinButton.onClick.Invoke();
        Assert.That(controller.IsConnecting, Is.True);
        Assert.That(view.Dialog.IsShown, Is.True);
        Assert.That(view.Dialog.Message, Is.EqualTo("Connecting to 192.168.1.20:7777…\n0 s of 10"));
        Assert.That(view.Dialog.PrimaryButton.gameObject.activeSelf, Is.False, "Cancel alone.");
        view.Dialog.SecondaryButton.onClick.Invoke();
        Assert.That(controller.IsConnecting, Is.True, "The press that started the join does not cancel it.");

        rig.WallClock.Advance(4.2);
        controller.Tick();
        Assert.That(rig.Selection.Current, Is.EqualTo(view.Dialog.SecondaryButton.gameObject));
        Assert.That(view.Dialog.Message, Is.EqualTo("Connecting to 192.168.1.20:7777…\n4 s of 10"));

        view.Dialog.SecondaryButton.onClick.Invoke();
        Assert.That(controller.IsConnecting, Is.False);
        Assert.That(view.Dialog.IsShown, Is.False);
        Assert.That(view.Notice.text, Is.Empty);
        Assert.That(rig.Selection.Current, Is.EqualTo(view.JoinButton.gameObject));
        Assert.That(rig.Settings.RecentHosts, Is.Empty);
    }

    /// <summary>
    /// Back, or Escape, while connecting cancels the join; otherwise it closes the screen.
    /// </summary>
    [Test]
    public void Back_CancelsAJoin_ElseClosesTheScreen()
    {
        Open();
        view.Field.text = "192.168.1.20";
        view.JoinButton.onClick.Invoke();
        controller.Tick();

        rig.MenuInput.BackPressedThisFrame = true;
        rig.Menus.Tick();
        Assert.That(controller.IsConnecting, Is.False);
        Assert.That(controller.IsOpen, Is.True);

        rig.Menus.Tick();
        rig.MenuInput.BackPressedThisFrame = false;
        Assert.That(controller.IsOpen, Is.False);
        Assert.That(view.IsShown, Is.False);
        Assert.That(closes, Is.EqualTo(1));
        Assert.That(rig.Menus.Count, Is.Zero);
    }

    /// <summary>
    /// Each failed join says why on the notice line, each sentence on its own line, hides the dialog and selects Join.
    /// </summary>
    [TestCase(null, "Could not reach 192.168.1.20:7777 in 10 s.\nCheck the address, and that the host is in a game.")]
    [TestCase("full:4", "192.168.1.20:7777 is full: 4 of 4 players.")]
    [TestCase("build:0.0.9-prealpha", "The host runs 0.0.9-prealpha and you run {0}.\nPlay together needs the same build.")]
    public void AFailedJoin_SaysWhyOnTheNoticeLine(string refusal, string message)
    {
        Open();
        view.Field.text = "192.168.1.20";
        view.JoinButton.onClick.Invoke();
        controller.Tick();

        rig.Connection.CompleteJoin(false, refusal);

        Assert.That(view.Notice.text, Is.EqualTo(string.Format(message, Application.version)));
        Assert.That(view.Dialog.IsShown, Is.False);
        Assert.That(controller.IsConnecting, Is.False);
        Assert.That(rig.Selection.Current, Is.EqualTo(view.JoinButton.gameObject));
    }

    /// <summary>
    /// A host that cannot start says what to do on the notice line.
    /// </summary>
    [Test]
    public void HostingThatFails_SaysWhatToDo()
    {
        Open();
        rig.Connection.HostStarts = false;
        UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Could not start hosting"));

        view.HostNewButton.onClick.Invoke();

        Assert.That(view.Notice.text, Is.EqualTo("Could not host on port 7777.\nClose any other copy of the game and try again."));
        Assert.That(rig.Flow.WorldStarts, Is.Zero);
    }

    /// <summary>
    /// A recent host's button joins it at once, on its own port, and once connected it goes first among the recent hosts.
    /// </summary>
    [Test]
    public void ARecentHost_JoinsWithOnePress()
    {
        rig.Settings.AddRecentHost("192.168.1.20:7778");
        rig.Settings.AddRecentHost("100.101.102.103");
        Open();

        view.RecentButton(1).onClick.Invoke();
        Assert.That(rig.Connection.JoinHost, Is.EqualTo("192.168.1.20"));
        Assert.That(rig.Connection.JoinPort, Is.EqualTo(7778));
        Assert.That(view.Field.text, Is.EqualTo("192.168.1.20:7778"));

        rig.Connection.CompleteJoin(true);
        Assert.That(rig.Settings.RecentHosts, Is.EqualTo(new[] { "192.168.1.20:7778", "100.101.102.103" }));
        Assert.That(rig.Flow.JoinedStarts, Is.EqualTo(1));
        Assert.That(view.Dialog.IsShown, Is.False);
    }

    /// <summary>
    /// The host column's invite block shows this machine's addresses, and Copy puts "address:port" on the clipboard and
    /// says "Copied" for its time.
    /// </summary>
    [Test]
    public void TheInvite_ShowsTheAddresses_AndCopyPutsOneOnTheClipboard()
    {
        rig.AddAddresses();
        Open();

        Assert.That(view.Invite.ShownAddresses, Is.EqualTo(2));
        view.Invite.CopyButton(1).onClick.Invoke();
        Assert.That(rig.Clipboard.Text, Is.EqualTo("100.101.102.103:7777"));
        Assert.That(view.Invite.CopyLabel(1), Is.EqualTo(HostInvite.CopiedLabel));
        rig.WallClock.Advance(1.3);
        controller.Tick();
        Assert.That(view.Invite.CopyLabel(1), Is.EqualTo(HostInvite.CopyLabel));
    }

    /// <summary>
    /// Opening with an address and Join selected, as the lost-while-joining dialog does, puts it in the field.
    /// </summary>
    [Test]
    public void OpeningWithAnAddress_PutsItInTheField_AndCanSelectJoin()
    {
        rig.Settings.AddRecentHost("10.0.0.1");

        controller.Open(() => closes++, "100.101.102.103:7777", true);

        Assert.That(view.Field.text, Is.EqualTo("100.101.102.103:7777"));
        Assert.That(rig.Selection.Current, Is.EqualTo(view.JoinButton.gameObject));
    }

    /// <summary>
    /// The field takes only what an address holds, the field limited to the longest host and a port.
    /// </summary>
    [Test]
    public void TheField_TakesOnlyWhatAnAddressHolds()
    {
        foreach (char kept in "aZ09.-_:")
        {
            Assert.That(PlayTogetherController.ValidateAddressCharacter(string.Empty, 0, kept), Is.EqualTo(kept));
        }

        foreach (char dropped in " /@é<\n")
        {
            Assert.That(PlayTogetherController.ValidateAddressCharacter(string.Empty, 0, dropped), Is.EqualTo(char.MinValue), $"'{dropped}'");
        }

        Assert.That(view.Field.onValidateInput, Is.Not.Null);
        Assert.That(view.Field.characterLimit, Is.EqualTo(JoinAddress.MaxHostLength + 6));
    }

    /// <summary>
    /// Opens the screen, counting its closes.
    /// </summary>
    private void Open()
    {
        controller.Open(() => closes++);
    }

    /// <summary>
    /// The hosts on the recent hosts' buttons that show.
    /// </summary>
    private string[] ShownRecent()
    {
        System.Collections.Generic.List<string> shown = new();
        for (int i = 0; i < view.RecentCapacity; i++)
        {
            if (view.RecentButton(i).gameObject.activeSelf)
            {
                shown.Add(view.RecentHost(i));
            }
        }

        return shown.ToArray();
    }
}

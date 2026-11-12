using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using VContainer.Unity;

/// <summary>
/// Menu-scope entry point for Play together (Docs/UiRevamp.md, Phase 9), on this machine only.
/// <list type="bullet">
/// <item><b>Host:</b> Host and continue (shown with a save, its Continue line under it) and Host a new game start hosting
/// through <see cref="MenuSessionStarter"/>; the invite block shows this machine's addresses with Copy
/// (<see cref="HostInvite"/>).</item>
/// <item><b>Join:</b> the field holds the address joined last and takes only what an address holds (letters, digits,
/// '.', '-', '_' and ':'); Join checks it (<see cref="JoinAddress"/>) and connects, showing the connecting dialog with a
/// count of the seconds and Cancel; a recent host's button joins it at once.</item>
/// <item><b>Messages:</b> a malformed address, a host that cannot be reached, refused or failing to start says why on
/// the notice line; a malformed address leaves the field selected.</item>
/// <item><b>Focus:</b> opening selects Host and continue, else Host a new game; Up and Down move along a column, Left and
/// Right across to the same row of the other, and Down from a column's last control reaches Back. Back (or Escape)
/// cancels a join, else stops typing in the field, else closes the screen.</item>
/// </list>
/// </summary>
public sealed class PlayTogetherController : IStartable, ITickable, IDisposable, IMenuLayer
{
    public const string InvalidAddress = "Type the host's address, like 192.168.1.20 or 100.101.102.103:7777.";
    public const string ConnectingFormat = "Connecting to {0}…\n{1} s of {2}";
    public const string CancelLabel = "Cancel";

    private readonly PlayTogetherView view;
    private readonly MenuSessionStarter starter;
    private readonly ContinueLine continueLine;
    private readonly UserSettings settings;
    private readonly IWallClock wallClock;
    private readonly MenuStack menus;
    private readonly UISelection selection;
    private readonly HostInvite invite;
    private readonly List<Selectable> hostColumn = new();
    private readonly List<Selectable> joinColumn = new();
    private readonly List<UnityAction> recentActions = new();
    private Action onClosed;
    private CancellationTokenSource connecting;
    private JoinAddress joining;
    private DateTime joinStarted;
    private int shownSeconds = -1;
    private int ticks;
    private int joinTick = -1;
    private bool selectCancel;
    private int typingFrame = -100;
    private List<Selectable> backColumn;
    private bool started;

    /// <summary>
    /// Runs <paramref name="view"/>, starting sessions through <paramref name="starter"/>, its Continue line from
    /// <paramref name="continueLine"/>, its invite from <paramref name="hostAddresses"/> copied through
    /// <paramref name="clipboard"/>, the recent hosts from <paramref name="settings"/>, timed on
    /// <paramref name="wallClock"/> with <paramref name="hudSettings"/>' message time, as a layer of
    /// <paramref name="menus"/>, moving focus through <paramref name="selection"/>.
    /// </summary>
    public PlayTogetherController(
        PlayTogetherView view,
        MenuSessionStarter starter,
        ContinueLine continueLine,
        HostAddresses hostAddresses,
        IClipboard clipboard,
        IWallClock wallClock,
        HudSettings hudSettings,
        UserSettings settings,
        MenuStack menus,
        UISelection selection)
    {
        this.view = view;
        this.starter = starter;
        this.continueLine = continueLine;
        this.settings = settings;
        this.wallClock = wallClock;
        this.menus = menus;
        this.selection = selection;
        invite = view != null ? new HostInvite(view.Invite, hostAddresses, clipboard, wallClock, hudSettings) : null;
    }

    public bool IsOpen { get; private set; }
    public bool IsConnecting => connecting != null;

    /// <summary>
    /// Wires the screen's buttons and field, readies the invite block, and hides the screen.
    /// </summary>
    public void Start()
    {
        if (started || view == null)
        {
            return;
        }

        started = true;
        invite.Start();
        view.HostContinueButton.onClick.AddListener(HostAndContinue);
        view.HostNewButton.onClick.AddListener(HostNew);
        view.JoinButton.onClick.AddListener(Join);
        view.BackButton.onClick.AddListener(Close);
        view.Field.onSubmit.AddListener(HandleFieldSubmit);
        view.Field.onValidateInput = ValidateAddressCharacter;
        view.Field.characterLimit = JoinAddress.MaxHostLength + 6;
        view.Dialog.SecondaryButton.onClick.AddListener(CancelJoin);
        for (int i = 0; i < view.RecentCapacity; i++)
        {
            int index = i;
            UnityAction action = () => JoinRecent(index);
            recentActions.Add(action);
            view.RecentButton(i).onClick.AddListener(action);
        }

        view.Dialog.Hide();
        view.SetShown(false);
    }

    /// <summary>
    /// Cancels a join in progress and unwires the screen.
    /// </summary>
    public void Dispose()
    {
        if (!started)
        {
            return;
        }

        started = false;
        connecting?.Cancel();
        if (view == null)
        {
            return;
        }

        view.HostContinueButton.onClick.RemoveListener(HostAndContinue);
        view.HostNewButton.onClick.RemoveListener(HostNew);
        view.JoinButton.onClick.RemoveListener(Join);
        view.BackButton.onClick.RemoveListener(Close);
        view.Field.onSubmit.RemoveListener(HandleFieldSubmit);
        view.Dialog.SecondaryButton.onClick.RemoveListener(CancelJoin);
        for (int i = 0; i < recentActions.Count; i++)
        {
            view.RecentButton(i).onClick.RemoveListener(recentActions[i]);
        }
    }

    /// <summary>
    /// Shows the screen: Host and continue with its line when there is a save, the addresses read now, the recent hosts,
    /// the field holding <paramref name="address"/> (the host joined last when null), and no message. Selects Join when
    /// <paramref name="selectJoin"/>, else Host and continue, else Host a new game. <paramref name="closed"/> runs once it
    /// closes.
    /// </summary>
    public void Open(Action closed, string address = null, bool selectJoin = false)
    {
        if (!started || IsOpen)
        {
            return;
        }

        IsOpen = true;
        onClosed = closed;
        string line = starter.HasSave && continueLine.TryGet(out string text) ? text : null;
        view.SetContinue(line);
        int addresses = invite.Show();
        IReadOnlyList<string> recent = settings.RecentHosts;
        int recentCount = view.SetRecent(recent);
        view.Field.text = address ?? (recent.Count > 0 ? recent[0] : string.Empty);
        view.SetNotice(null);
        view.Dialog.Hide();
        view.SetShown(true);
        Wire(line != null, addresses, recentCount);
        menus.Push(this);
        selection.Select(selectJoin ? view.JoinButton : hostColumn[0]);
    }

    /// <summary>
    /// Cancels a join in progress, hides the screen and runs the callback given to <see cref="Open"/>.
    /// </summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        connecting?.Cancel();
        IsOpen = false;
        invite.Hide();
        view.Dialog.Hide();
        view.SetShown(false);
        menus.Remove(this);
        Action closed = onClosed;
        onClosed = null;
        closed?.Invoke();
    }

    /// <summary>
    /// Cancels the join in progress, else stops typing in the field, else closes the screen.
    /// </summary>
    public void HandleBack()
    {
        if (IsConnecting)
        {
            CancelJoin();
        }
        else if (view.Field.isFocused || Time.frameCount - typingFrame <= 1)
        {
            view.Field.DeactivateInputField();
            selection.Select(view.Field);
        }
        else
        {
            Close();
        }
    }

    /// <summary>
    /// While the screen shows: puts a Copy button's label back once its time is up, notes when the field takes the
    /// typing, counts a join's seconds, selects Cancel the tick after a join started, and points Back's Up and Down at the
    /// column the selection was last in.
    /// </summary>
    public void Tick()
    {
        if (!IsOpen)
        {
            return;
        }

        ticks++;
        invite.Tick();
        if (view.Field.isFocused)
        {
            typingFrame = Time.frameCount;
        }

        if (IsConnecting)
        {
            ShowCount();
            if (selectCancel)
            {
                selectCancel = false;
                selection.Select(view.Dialog.SecondaryButton);
            }
        }

        FollowColumn();
    }

    /// <summary>
    /// Puts <paramref name="address"/> in the field and joins it, as the main menu's JoinGame entry point does.
    /// </summary>
    public void JoinAt(string address)
    {
        view.Field.text = address ?? string.Empty;
        Join();
    }

    /// <summary>
    /// Join: checks the field's address, saying how to type one and selecting the field when it is empty or malformed,
    /// else connects to it.
    /// </summary>
    internal void Join()
    {
        if (!IsOpen || IsConnecting || !starter.CanStart)
        {
            return;
        }

        if (!JoinAddress.TryParse(view.Field.text, starter.DefaultPort, out JoinAddress address))
        {
            view.SetNotice(InvalidAddress);
            selection.Select(view.Field);
            return;
        }

        StartJoin(address);
    }

    /// <summary>
    /// Host and continue: hosts the latest save, saying why on the notice line when it cannot.
    /// </summary>
    private void HostAndContinue()
    {
        Host(true);
    }

    /// <summary>
    /// Host a new game: hosts a new session, saying why on the notice line when it cannot.
    /// </summary>
    private void HostNew()
    {
        Host(false);
    }

    /// <summary>
    /// Starts hosting, to continue the latest save when <paramref name="continueLatest"/>, or shows why not.
    /// </summary>
    private void Host(bool continueLatest)
    {
        if (!IsOpen || IsConnecting)
        {
            return;
        }

        string failure = starter.Host(continueLatest);
        if (failure != null)
        {
            view.SetNotice(failure);
        }
    }

    /// <summary>
    /// A recent host's button: puts that host in the field and joins it.
    /// </summary>
    private void JoinRecent(int index)
    {
        if (index < settings.RecentHosts.Count)
        {
            JoinAt(settings.RecentHosts[index]);
        }
    }

    /// <summary>
    /// The field's input filter: <paramref name="added"/> when an address can hold it, else nothing.
    /// </summary>
    internal static char ValidateAddressCharacter(string text, int index, char added)
    {
        bool kept = (added >= 'a' && added <= 'z') || (added >= 'A' && added <= 'Z') || (added >= '0' && added <= '9') || added == '.' || added == '-' || added == '_' || added == ':';
        return kept ? added : char.MinValue;
    }

    /// <summary>
    /// Enter in the field while typing joins; the UI's own submit on a field not typing only starts the typing.
    /// </summary>
    private void HandleFieldSubmit(string text)
    {
        if (view.Field.isFocused)
        {
            Join();
        }
    }

    /// <summary>
    /// Clears the message, shows the connecting dialog and connects to <paramref name="address"/> in the background.
    /// </summary>
    private void StartJoin(JoinAddress address)
    {
        view.SetNotice(null);
        joining = address;
        joinStarted = wallClock.UtcNow;
        joinTick = ticks;
        shownSeconds = -1;
        selectCancel = true;
        connecting = new CancellationTokenSource();
        view.Dialog.Show(string.Empty, null, CancelLabel);
        ShowCount();
        _ = RunJoinAsync(address, connecting);
    }

    /// <summary>
    /// Waits for the join through <paramref name="cancellation"/>, then hides the dialog: on a failure says why and
    /// selects Join, on a cancel selects Join, and on success leaves the menu to the joined game's transition.
    /// </summary>
    private async Awaitable RunJoinAsync(JoinAddress address, CancellationTokenSource cancellation)
    {
        string failure;
        try
        {
            failure = await starter.JoinAsync(address, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            failure = null;
        }

        bool cancelled = cancellation.IsCancellationRequested;
        if (connecting == cancellation)
        {
            connecting = null;
        }

        cancellation.Dispose();
        if (!IsOpen)
        {
            return;
        }

        view.Dialog.Hide();
        if (failure != null || cancelled)
        {
            view.SetNotice(failure);
            selection.Select(view.JoinButton);
        }
    }

    /// <summary>
    /// Cancel: ends the join in progress, unless it started this very tick (the press that started it).
    /// </summary>
    private void CancelJoin()
    {
        if (IsConnecting && ticks != joinTick)
        {
            connecting.Cancel();
        }
    }

    /// <summary>
    /// Shows the join's whole seconds so far against the connect timeout, when they changed.
    /// </summary>
    private void ShowCount()
    {
        float timeout = starter.ConnectTimeoutSeconds;
        int seconds = Mathf.Clamp((int)Math.Floor((wallClock.UtcNow - joinStarted).TotalSeconds), 0, Mathf.RoundToInt(timeout));
        if (seconds == shownSeconds)
        {
            return;
        }

        shownSeconds = seconds;
        view.Dialog.SetMessage(string.Format(CultureInfo.InvariantCulture, ConnectingFormat, joining, seconds, Mathf.RoundToInt(timeout)));
    }

    /// <summary>
    /// Wires the explicit navigation: the host column (Host and continue when <paramref name="hasSave"/>, Host a new game,
    /// <paramref name="addresses"/> Copy buttons), the join column (the field, Join, <paramref name="recentCount"/> recent
    /// hosts), each looping through Back, and Left and Right across the same row of the other column.
    /// </summary>
    private void Wire(bool hasSave, int addresses, int recentCount)
    {
        hostColumn.Clear();
        joinColumn.Clear();
        if (hasSave)
        {
            hostColumn.Add(view.HostContinueButton);
        }

        hostColumn.Add(view.HostNewButton);
        for (int i = 0; i < addresses; i++)
        {
            hostColumn.Add(view.Invite.CopyButton(i));
        }

        joinColumn.Add(view.Field);
        joinColumn.Add(view.JoinButton);
        for (int i = 0; i < recentCount; i++)
        {
            joinColumn.Add(view.RecentButton(i));
        }

        WireColumn(hostColumn, joinColumn, true);
        WireColumn(joinColumn, hostColumn, false);
        backColumn = null;
        PointBackAt(hostColumn);
    }

    /// <summary>
    /// Wires <paramref name="column"/> top to bottom through Back, and across to <paramref name="other"/> on its right
    /// when <paramref name="onLeft"/>, else on its left.
    /// </summary>
    private void WireColumn(List<Selectable> column, List<Selectable> other, bool onLeft)
    {
        for (int i = 0; i < column.Count; i++)
        {
            Selectable across = other[Mathf.Min(i, other.Count - 1)];
            column[i].navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = i > 0 ? column[i - 1] : view.BackButton,
                selectOnDown = i < column.Count - 1 ? column[i + 1] : view.BackButton,
                selectOnLeft = onLeft ? null : across,
                selectOnRight = onLeft ? across : null,
            };
        }
    }

    /// <summary>
    /// Points Back's Up at the last control of the column the selection is in and its Down at that column's first.
    /// </summary>
    private void FollowColumn()
    {
        GameObject current = selection.Current;
        if (current == null)
        {
            return;
        }

        for (int i = 0; i < joinColumn.Count; i++)
        {
            if (joinColumn[i].gameObject == current)
            {
                PointBackAt(joinColumn);
                return;
            }
        }

        for (int i = 0; i < hostColumn.Count; i++)
        {
            if (hostColumn[i].gameObject == current)
            {
                PointBackAt(hostColumn);
                return;
            }
        }
    }

    /// <summary>
    /// Points Back's Up at <paramref name="column"/>'s last control and its Down at its first, when it pointed elsewhere.
    /// </summary>
    private void PointBackAt(List<Selectable> column)
    {
        if (backColumn == column)
        {
            return;
        }

        backColumn = column;
        view.BackButton.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnUp = column[column.Count - 1],
            selectOnDown = column[0],
        };
    }
}

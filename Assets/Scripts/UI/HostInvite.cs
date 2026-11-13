using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Fills an <see cref="InviteBlockView"/> (Docs/UiRevamp.md, Phases 6 and 9), for the Party page and Play together
/// alike: the addresses friends join this machine at (<see cref="HostAddresses"/>, read each time it shows) with the
/// port; a Copy button puts "address:port" on the clipboard and reads "Copied" for <see cref="HudSettings.MessageSeconds"/>
/// of real time. Nothing is ever sent anywhere. A plain class each page builds over its own block.
/// </summary>
public sealed class HostInvite
{
    public const string CopyLabel = "Copy";
    public const string CopiedLabel = "Copied";
    public const string PortFormat = "port {0}";

    private readonly InviteBlockView view;
    private readonly HostAddresses hostAddresses;
    private readonly IClipboard clipboard;
    private readonly IWallClock wallClock;
    private readonly HudSettings settings;
    private readonly List<HostAddress> addresses = new();
    private string portText = string.Empty;
    private int copied = -1;
    private DateTime copiedUntil;
    private bool started;

    /// <summary>
    /// Fills <paramref name="view"/> with <paramref name="hostAddresses"/>, copying through <paramref name="clipboard"/>
    /// and timing "Copied" on <paramref name="wallClock"/> for <paramref name="settings"/>' message time.
    /// </summary>
    public HostInvite(InviteBlockView view, HostAddresses hostAddresses, IClipboard clipboard, IWallClock wallClock, HudSettings settings)
    {
        this.view = view;
        this.hostAddresses = hostAddresses;
        this.clipboard = clipboard;
        this.wallClock = wallClock;
        this.settings = settings;
    }

    public bool IsShown { get; private set; }

    /// <summary>
    /// How many addresses show.
    /// </summary>
    public int Count => addresses.Count;

    /// <summary>
    /// Builds the port's text and wires the Copy buttons, once.
    /// </summary>
    public void Start()
    {
        if (started || view == null)
        {
            return;
        }

        started = true;
        portText = string.Format(CultureInfo.InvariantCulture, PortFormat, hostAddresses.Port);
        for (int i = 0; i < view.AddressCapacity; i++)
        {
            int index = i;
            view.CopyButton(i).onClick.AddListener(() => Copy(index));
        }
    }

    /// <summary>
    /// Reads the addresses now and shows them with the port; returns how many show.
    /// </summary>
    public int Show()
    {
        ResetCopied();
        hostAddresses.Read(addresses);
        IsShown = true;
        return view.Show(addresses, portText, CopyLabel);
    }

    /// <summary>
    /// Stops the block taking presses and puts a copied button's label back.
    /// </summary>
    public void Hide()
    {
        ResetCopied();
        IsShown = false;
    }

    /// <summary>
    /// Puts a Copy button's label back once its time is up.
    /// </summary>
    public void Tick()
    {
        if (copied >= 0 && wallClock.UtcNow >= copiedUntil)
        {
            ResetCopied();
        }
    }

    /// <summary>
    /// Puts "address:port" of the address at <paramref name="index"/> on the clipboard and shows "Copied" on its button,
    /// while the block shows.
    /// </summary>
    public void Copy(int index)
    {
        if (!IsShown || index < 0 || index >= addresses.Count)
        {
            return;
        }

        ResetCopied();
        clipboard.Copy(new JoinAddress(addresses[index].Address, hostAddresses.Port).ToString());
        view.SetCopyLabel(index, CopiedLabel);
        copied = index;
        copiedUntil = wallClock.UtcNow + TimeSpan.FromSeconds(settings.MessageSeconds);
    }

    /// <summary>
    /// Puts the copied button's label back.
    /// </summary>
    private void ResetCopied()
    {
        if (copied >= 0)
        {
            view.SetCopyLabel(copied, CopyLabel);
            copied = -1;
        }
    }
}

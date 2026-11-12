using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The in-game menu's Party page (Docs/UiRevamp.md, Phase 6), shown in the menu's place online: titled "Party", a
/// <see cref="PartyRowView"/> per player, for the host an invite block ("Friends join at", the port, and each address
/// with its network's name and a Copy button, or a line saying there is none), and Back. Passive: the presenter fills
/// it. Its Copy buttons and Back loop top to bottom. <see cref="Resized"/> is raised when the rows or addresses shown
/// change, so the menu lays it out again.
/// </summary>
[DisallowMultipleComponent]
public class PartyPageView : MonoBehaviour
{
    [SerializeField, Tooltip("The page's laid-out column, under its title.")] private RectTransform content;
    [SerializeField, Tooltip("A row per player there can be, top to bottom.")] private PartyRowView[] rows = new PartyRowView[0];
    [SerializeField, Tooltip("The host's invite block.")] private GameObject invite;
    [SerializeField] private TMP_Text port;
    [SerializeField, Tooltip("The line shown when no address was found.")] private GameObject none;
    [SerializeField, Tooltip("An address row per address there can be.")] private GameObject[] addressRows = new GameObject[0];
    [SerializeField] private TMP_Text[] addresses = new TMP_Text[0];
    [SerializeField] private TMP_Text[] networks = new TMP_Text[0];
    [SerializeField] private Button[] copyButtons = new Button[0];
    [SerializeField] private TMP_Text[] copyTexts = new TMP_Text[0];
    [SerializeField] private Button backButton;

    private readonly Selectable[] looped = new Selectable[8];
    private int shownRows = -1;
    private int shownAddresses = -1;
    private bool shownInvite;

    public event Action Resized;

    public RectTransform Box => (RectTransform)transform;
    public RectTransform Content => content;
    public int RowCount => rows != null ? rows.Length : 0;
    public int AddressCapacity => addressRows != null ? addressRows.Length : 0;
    public Button BackButton => backButton;
    internal bool IsInviteShown => invite != null && invite.activeSelf;
    internal int ShownAddresses => shownAddresses;

    /// <summary>
    /// The first control of the page: its first Copy button shown, else Back.
    /// </summary>
    public Selectable FirstSelectable => shownAddresses > 0 ? copyButtons[0] : backButton;

    /// <summary>
    /// Logs an error when a part is missing or the address arrays differ in length.
    /// </summary>
    private void Awake()
    {
        int count = AddressCapacity;
        if (content == null || rows == null || rows.Length == 0 || invite == null || port == null || none == null || backButton == null
            || addresses.Length != count || networks.Length != count || copyButtons.Length != count || copyTexts.Length != count)
        {
            Debug.LogError($"{name} needs its content, rows, invite block with its port, none line and as many addresses, networks and Copy buttons as address rows, and Back.", this);
        }
    }

    /// <summary>
    /// The row at <paramref name="index"/>, from the top.
    /// </summary>
    public PartyRowView Row(int index)
    {
        return rows[index];
    }

    /// <summary>
    /// The Copy button of the address at <paramref name="index"/>.
    /// </summary>
    public Button CopyButton(int index)
    {
        return copyButtons[index];
    }

    /// <summary>
    /// Hides the rows from <paramref name="count"/> on; the presenter shows the others.
    /// </summary>
    public void SetRowCount(int count)
    {
        for (int i = count; i < RowCount; i++)
        {
            if (rows[i] != null)
            {
                rows[i].Hide();
            }
        }

        if (count != shownRows)
        {
            shownRows = count;
            Resized?.Invoke();
        }
    }

    /// <summary>
    /// Shows the invite block with <paramref name="portText"/> and the first <see cref="AddressCapacity"/> of
    /// <paramref name="hostAddresses"/>, each with its Copy button reading <paramref name="copyLabel"/>, or the none
    /// line without any; then makes the Copy buttons and Back loop.
    /// </summary>
    public void ShowInvite(IReadOnlyList<HostAddress> hostAddresses, string portText, string copyLabel)
    {
        invite.SetActive(true);
        port.text = portText;
        int count = Mathf.Min(hostAddresses.Count, AddressCapacity);
        for (int i = 0; i < AddressCapacity; i++)
        {
            bool shown = i < count;
            addressRows[i].SetActive(shown);
            if (shown)
            {
                addresses[i].text = hostAddresses[i].Address;
                networks[i].text = hostAddresses[i].Network;
                copyTexts[i].text = copyLabel;
            }
        }

        none.SetActive(count == 0);
        SetAddresses(count, true);
    }

    /// <summary>
    /// Hides the invite block, leaving Back alone in its loop.
    /// </summary>
    public void HideInvite()
    {
        invite.SetActive(false);
        SetAddresses(0, false);
    }

    /// <summary>
    /// Shows <paramref name="label"/> on the Copy button of the address at <paramref name="index"/>.
    /// </summary>
    public void SetCopyLabel(int index, string label)
    {
        if (index >= 0 && index < AddressCapacity && copyTexts[index] != null)
        {
            copyTexts[index].text = label;
        }
    }

    /// <summary>
    /// The label on the Copy button of the address at <paramref name="index"/>, for tests.
    /// </summary>
    internal string CopyLabel(int index)
    {
        return copyTexts[index].text;
    }

    /// <summary>
    /// The address shown at <paramref name="index"/> and its network's name, for tests.
    /// </summary>
    internal (string address, string network) Address(int index)
    {
        return (addresses[index].text, networks[index].text);
    }

    /// <summary>
    /// Records <paramref name="count"/> addresses shown and whether the invite block shows, makes the shown Copy buttons
    /// and Back loop, and raises <see cref="Resized"/> on a change.
    /// </summary>
    private void SetAddresses(int count, bool inviteShown)
    {
        int looping = 0;
        for (int i = 0; i < count; i++)
        {
            looped[looping++] = copyButtons[i];
        }

        looped[looping++] = backButton;
        GameMenuView.Loop(looped, looping, true);
        if (count != shownAddresses || inviteShown != shownInvite)
        {
            shownAddresses = count;
            shownInvite = inviteShown;
            Resized?.Invoke();
        }
    }
}

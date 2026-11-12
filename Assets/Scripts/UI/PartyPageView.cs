using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The in-game menu's Party page (Docs/UiRevamp.md, Phase 6), shown in the menu's place online: titled "Party", a
/// <see cref="PartyRowView"/> per player, for the host the invite block (<see cref="InviteBlockView"/>, shared with Play
/// together since Phase 9), and Back. Passive: the presenter fills it. Its Copy buttons and Back loop top to bottom.
/// <see cref="Resized"/> is raised when the rows or addresses shown change, so the menu lays it out again.
/// </summary>
[DisallowMultipleComponent]
public class PartyPageView : MonoBehaviour
{
    [SerializeField, Tooltip("The page's laid-out column, under its title.")] private RectTransform content;
    [SerializeField, Tooltip("A row per player there can be, top to bottom.")] private PartyRowView[] rows = new PartyRowView[0];
    [SerializeField, Tooltip("The host's invite block, an InviteBlock component.")] private InviteBlockView inviteBlock;
    [SerializeField] private Button backButton;

    private readonly Selectable[] looped = new Selectable[8];
    private int shownRows = -1;
    private int shownAddresses = -1;
    private bool shownInvite;

    public event Action Resized;

    public RectTransform Box => (RectTransform)transform;
    public RectTransform Content => content;
    public int RowCount => rows != null ? rows.Length : 0;
    public InviteBlockView Invite => inviteBlock;
    public Button BackButton => backButton;
    internal bool IsInviteShown => inviteBlock != null && inviteBlock.gameObject.activeSelf;
    internal int ShownAddresses => shownAddresses;

    /// <summary>
    /// The first control of the page: its first Copy button shown, else Back.
    /// </summary>
    public Selectable FirstSelectable => shownAddresses > 0 ? inviteBlock.CopyButton(0) : backButton;

    /// <summary>
    /// Logs an error when a part is missing.
    /// </summary>
    private void Awake()
    {
        if (content == null || rows == null || rows.Length == 0 || inviteBlock == null || backButton == null)
        {
            Debug.LogError($"{name} needs its content, rows, invite block and Back.", this);
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
        return inviteBlock.CopyButton(index);
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
    /// Shows the invite block, which <see cref="HostInvite"/> filled with <paramref name="addressCount"/> addresses, and
    /// makes its Copy buttons and Back loop.
    /// </summary>
    public void ShowInvite(int addressCount)
    {
        inviteBlock.gameObject.SetActive(true);
        SetAddresses(addressCount, true);
    }

    /// <summary>
    /// Hides the invite block, leaving Back alone in its loop.
    /// </summary>
    public void HideInvite()
    {
        inviteBlock.gameObject.SetActive(false);
        SetAddresses(0, false);
    }

    /// <summary>
    /// The label on the Copy button of the address at <paramref name="index"/>, for tests.
    /// </summary>
    internal string CopyLabel(int index)
    {
        return inviteBlock.CopyLabel(index);
    }

    /// <summary>
    /// The address shown at <paramref name="index"/> and its network's name, for tests.
    /// </summary>
    internal (string address, string network) Address(int index)
    {
        return inviteBlock.Address(index);
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
            looped[looping++] = inviteBlock.CopyButton(i);
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

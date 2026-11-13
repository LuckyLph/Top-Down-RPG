using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using VContainer.Unity;

/// <summary>
/// Menu-scope entry point for the Saved games screen (Docs/UiRevamp.md, Phase 10), on this machine only.
/// <list type="bullet">
/// <item><b>The list:</b> a card per save (<see cref="GameSave.List"/>), newest first: the place
/// (<see cref="ContinueLine.PlaceOf(GameSaveData)"/>), "from a host's game" after a joined client's copy, and the time
/// played and when it was saved.</item>
/// <item><b>Actions:</b> Play plays the save offline and Host hosts it (<see cref="MenuSessionStarter"/>), a copy going
/// on as a session of its own; a start that cannot go ahead says why on the notice line. Delete asks first, Cancel
/// selected; confirming deletes it (one copy kept, <see cref="GameSave.Delete"/>) and selects the same action of the card
/// taking its place, and deleting the last save closes the screen.</item>
/// <item><b>Hosting:</b> opened from Play together the screen is titled "Host a saved game" and its cards hold Host and
/// Delete, without Play.</item>
/// <item><b>Focus:</b> opening selects the newest card's first action. Only the selected card shows its actions; Up and
/// Down move to the same action of the card above or below, through Back past either end, and Left and Right among the
/// card's actions. Back's Up returns to the last card and its Down to the first, at the action used last; a click on a
/// card selects its first action. Back (or Escape) closes the confirmation, else the screen.</item>
/// </list>
/// </summary>
public sealed class SavedGamesController : IStartable, ILateTickable, IDisposable, IMenuLayer
{
    public const string Title = "Saved games";
    public const string HostingTitle = "Host a saved game";
    public const string CopyTag = "from a host's game";
    public const string UnknownPlace = "Unknown place";
    public const string DeleteQuestion = "Delete this saved game?";
    public const string DeleteCopyQuestion = "Delete your place in a host's game?";
    public const string DeleteFailed = "Could not delete that saved game.";

    private readonly SavedGamesView view;
    private readonly MenuSessionStarter starter;
    private readonly GameSave gameSave;
    private readonly ContinueLine line;
    private readonly MenuStack menus;
    private readonly UISelection selection;
    private readonly List<SavedGame> saves = new();
    private readonly List<CardActions> wired = new();
    private Action onClosed;
    private GameObject lastSelected;
    private int selectedCard = -1;
    private int column;
    private int pendingDelete = -1;
    private bool started;

    /// <summary>
    /// Runs <paramref name="view"/> over <paramref name="gameSave"/>'s saves, described by <paramref name="line"/>,
    /// starting them through <paramref name="starter"/>, as a layer of <paramref name="menus"/>, moving focus through
    /// <paramref name="selection"/>.
    /// </summary>
    public SavedGamesController(SavedGamesView view, MenuSessionStarter starter, GameSave gameSave, ContinueLine line, MenuStack menus, UISelection selection)
    {
        this.view = view;
        this.starter = starter;
        this.gameSave = gameSave;
        this.line = line;
        this.menus = menus;
        this.selection = selection;
    }

    public bool IsOpen { get; private set; }
    public bool IsHosting { get; private set; }
    public int SelectedCard => selectedCard;
    internal SavedGamesView View => view;

    /// <summary>
    /// Wires Back, the confirmation and every card the view makes, and hides the screen.
    /// </summary>
    public void Start()
    {
        if (started || view == null)
        {
            return;
        }

        started = true;
        view.BackButton.onClick.AddListener(Close);
        view.ConfirmYesButton.onClick.AddListener(ConfirmDelete);
        view.ConfirmNoButton.onClick.AddListener(CancelDelete);
        view.CardMade += WireCard;
        view.HideConfirm();
        view.SetShown(false);
    }

    /// <summary>
    /// Unwires the screen and its cards.
    /// </summary>
    public void Dispose()
    {
        if (!started)
        {
            return;
        }

        started = false;
        view.BackButton.onClick.RemoveListener(Close);
        view.ConfirmYesButton.onClick.RemoveListener(ConfirmDelete);
        view.ConfirmNoButton.onClick.RemoveListener(CancelDelete);
        view.CardMade -= WireCard;
        foreach (CardActions card in wired)
        {
            card.Unwire();
        }

        wired.Clear();
    }

    /// <summary>
    /// Shows the screen over every save, to host them when <paramref name="hosting"/>, and selects the newest card's first
    /// action; does nothing without a save. <paramref name="closed"/> runs once it closes.
    /// </summary>
    public void Open(Action closed, bool hosting)
    {
        if (!started || IsOpen)
        {
            return;
        }

        IsHosting = hosting;
        if (!Fill())
        {
            return;
        }

        IsOpen = true;
        onClosed = closed;
        view.SetTitle(hosting ? HostingTitle : Title);
        view.SetNotice(null);
        view.HideConfirm();
        view.SetShown(true);
        view.Scroll.verticalNormalizedPosition = 1f;
        menus.Push(this);
        column = 0;
        SelectCard(0, 0);
    }

    /// <summary>
    /// Hides the screen and runs the callback given to <see cref="Open"/>.
    /// </summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        pendingDelete = -1;
        view.HideConfirm();
        view.SetShown(false);
        menus.Remove(this);
        Action closed = onClosed;
        onClosed = null;
        closed?.Invoke();
    }

    /// <summary>
    /// Closes the confirmation, else the screen.
    /// </summary>
    public void HandleBack()
    {
        if (view.IsConfirmShown)
        {
            CancelDelete();
        }
        else
        {
            Close();
        }
    }

    /// <summary>
    /// After the selection moves (the event system moves it in Update): shows the actions of the card it reached and hides
    /// the last one's, scrolls the card into view, and points Back at the action used last.
    /// </summary>
    public void LateTick()
    {
        if (!IsOpen)
        {
            return;
        }

        GameObject current = selection.Current;
        if (current == lastSelected)
        {
            return;
        }

        lastSelected = current;
        for (int i = 0; i < view.CardCount; i++)
        {
            int action = view.Card(i).IndexOf(current);
            if (action >= 0)
            {
                column = action;
                ShowSelected(i);
                return;
            }
        }
    }

    /// <summary>
    /// Plays the save of the card at <paramref name="index"/> offline, or says why it cannot.
    /// </summary>
    internal void Play(int index)
    {
        if (CanAct(index))
        {
            view.SetNotice(starter.Play(saves[index].Name));
        }
    }

    /// <summary>
    /// Hosts the save of the card at <paramref name="index"/>, or says why it cannot.
    /// </summary>
    internal void Host(int index)
    {
        if (CanAct(index))
        {
            view.SetNotice(starter.Host(saves[index].Name));
        }
    }

    /// <summary>
    /// Asks before deleting the save of the card at <paramref name="index"/>, naming it, Cancel selected.
    /// </summary>
    internal void AskDelete(int index)
    {
        if (!CanAct(index))
        {
            return;
        }

        pendingDelete = index;
        SessionCardView card = view.Card(index);
        string question = saves[index].IsCopy ? DeleteCopyQuestion : DeleteQuestion;
        view.ShowConfirm(question + "\n" + card.PlaceText.text + ContinueLine.Separator + card.DetailsText.text);
        selection.Select(view.ConfirmNoButton);
    }

    /// <summary>
    /// Fills the cards from the saves, newest first, each with its actions for the mode and its explicit navigation;
    /// false when there is no save.
    /// </summary>
    private bool Fill()
    {
        saves.Clear();
        foreach (SavedGame save in gameSave.List())
        {
            saves.Add(save);
        }

        if (saves.Count == 0)
        {
            return false;
        }

        view.SetCardCount(saves.Count);
        for (int i = 0; i < saves.Count; i++)
        {
            GameSaveData data = saves[i].Data;
            string place = line.PlaceOf(data);
            SessionCardView card = view.Card(i);
            card.SetHosting(IsHosting);
            card.SetSelected(false);
            card.Set(place.Length > 0 ? place : UnknownPlace, data.IsCopy ? CopyTag : null, line.DetailsOf(data));
        }

        selectedCard = -1;
        lastSelected = null;
        Wire();
        return true;
    }

    /// <summary>
    /// Wires every shown card's actions: Left and Right along the card, Up and Down to the same action of the card above
    /// and below, through Back past either end; and Back to the last and first cards.
    /// </summary>
    private void Wire()
    {
        int count = view.CardCount;
        for (int i = 0; i < count; i++)
        {
            SessionCardView card = view.Card(i);
            for (int k = 0; k < card.ActionCount; k++)
            {
                card.Action(k).navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = k > 0 ? card.Action(k - 1) : null,
                    selectOnRight = k < card.ActionCount - 1 ? card.Action(k + 1) : null,
                    selectOnUp = i > 0 ? view.Card(i - 1).Action(k) : view.BackButton,
                    selectOnDown = i < count - 1 ? view.Card(i + 1).Action(k) : view.BackButton,
                };
            }
        }

        PointBack();
    }

    /// <summary>
    /// Points Back's Up at the last card's and its Down at the first card's action in the column used last.
    /// </summary>
    private void PointBack()
    {
        int count = view.CardCount;
        view.BackButton.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnUp = count > 0 ? view.Card(count - 1).Action(column) : null,
            selectOnDown = count > 0 ? view.Card(0).Action(column) : null,
        };
    }

    /// <summary>
    /// Selects the action at <paramref name="action"/> of the card at <paramref name="index"/>, showing that card.
    /// </summary>
    private void SelectCard(int index, int action)
    {
        Button target = view.Card(index).Action(action);
        selection.Select(target);
        lastSelected = target.gameObject;
        ShowSelected(index);
    }

    /// <summary>
    /// Shows the card at <paramref name="index"/> as the selected one, hiding the last one's actions, scrolls it into
    /// view, and points Back at the column used last.
    /// </summary>
    private void ShowSelected(int index)
    {
        if (selectedCard != index)
        {
            if (selectedCard >= 0 && selectedCard < view.CardCount)
            {
                view.Card(selectedCard).SetSelected(false);
            }

            selectedCard = index;
            view.Card(index).SetSelected(true);
        }

        view.Reveal(view.Card(index));
        PointBack();
    }

    /// <summary>
    /// Confirmation accepted: deletes the save asked about, then shows the rest with the same action of the card taking
    /// its place selected, or closes the screen when none is left; a delete the disk refuses is said on the notice line.
    /// </summary>
    private void ConfirmDelete()
    {
        if (!IsOpen || pendingDelete < 0)
        {
            return;
        }

        int index = pendingDelete;
        pendingDelete = -1;
        view.HideConfirm();
        if (!gameSave.Delete(saves[index].Name))
        {
            view.SetNotice(DeleteFailed);
            SelectCard(index, column);
            return;
        }

        if (!Fill())
        {
            Close();
            return;
        }

        SelectCard(Mathf.Min(index, saves.Count - 1), column);
    }

    /// <summary>
    /// Confirmation declined: hides it and selects the Delete it came from.
    /// </summary>
    private void CancelDelete()
    {
        if (!view.IsConfirmShown)
        {
            return;
        }

        int index = pendingDelete;
        pendingDelete = -1;
        view.HideConfirm();
        if (index >= 0 && index < view.CardCount)
        {
            SelectCard(index, column);
        }
    }

    /// <summary>
    /// Whether an action of the card at <paramref name="index"/> may run: the screen open, the card shown, and no
    /// confirmation up.
    /// </summary>
    private bool CanAct(int index)
    {
        return IsOpen && index >= 0 && index < saves.Count && !view.IsConfirmShown;
    }

    /// <summary>
    /// Wires a card the view just made: its actions and its click, by its place in the list.
    /// </summary>
    private void WireCard(SessionCardView card)
    {
        wired.Add(new CardActions(this, card, wired.Count));
    }

    /// <summary>
    /// A click on a card: selects its first action.
    /// </summary>
    private void HandleCardClicked(SessionCardView card)
    {
        if (!IsOpen || view.IsConfirmShown)
        {
            return;
        }

        for (int i = 0; i < view.CardCount; i++)
        {
            if (view.Card(i) == card)
            {
                column = 0;
                SelectCard(i, 0);
                return;
            }
        }
    }

    /// <summary>One card's listeners, kept to remove them.</summary>
    private sealed class CardActions
    {
        private readonly SavedGamesController owner;
        private readonly SessionCardView card;
        private readonly UnityAction play;
        private readonly UnityAction host;
        private readonly UnityAction delete;

        /// <summary>
        /// Wires <paramref name="view"/>, the card at <paramref name="index"/>, to <paramref name="controller"/>.
        /// </summary>
        public CardActions(SavedGamesController controller, SessionCardView view, int index)
        {
            owner = controller;
            card = view;
            play = () => owner.Play(index);
            host = () => owner.Host(index);
            delete = () => owner.AskDelete(index);
            card.PlayButton.onClick.AddListener(play);
            card.HostButton.onClick.AddListener(host);
            card.DeleteButton.onClick.AddListener(delete);
            card.Clicked += owner.HandleCardClicked;
        }

        /// <summary>
        /// Removes the card's listeners.
        /// </summary>
        public void Unwire()
        {
            if (card == null)
            {
                return;
            }

            card.PlayButton.onClick.RemoveListener(play);
            card.HostButton.onClick.RemoveListener(host);
            card.DeleteButton.onClick.RemoveListener(delete);
            card.Clicked -= owner.HandleCardClicked;
        }
    }
}

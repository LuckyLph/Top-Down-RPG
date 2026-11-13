using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

/// <summary>
/// The main menu (Docs/UiRevamp.md, Phase 9): a raised panel on the left over the title view (Phase 11) holding the
/// logo, then Continue with its line
/// (<see cref="ContinueLine"/>: where, how long played, when saved), New game, Saved games (Phase 10), Play together,
/// Character, Options and Quit, looping; Continue, its line and Saved games hide without a save, and New game is selected
/// then. The build version shows at the bottom right. Escape does nothing here. On the first run the Welcome panel
/// (<see cref="WelcomeController"/>) shows before it, and New game without a save goes through the Character screen,
/// whose Back reads "Start" (Phase 11).
/// <list type="bullet">
/// <item><b>Starting:</b> <see cref="ContinueGame"/>, <see cref="StartNewGame"/>, <see cref="HostGame"/> and
/// <see cref="JoinGame"/> stay as entry points for the PlayMode tests, over <see cref="MenuSessionStarter"/>; a start
/// that cannot go ahead says why in a dialog.</item>
/// <item><b>Screens:</b> Saved games, Play together, Character and Options open full screens in the panel's place, and
/// closing one shows the panel again for the saves left and selects its button, or the first one when it hid.</item>
/// <item><b>Notices:</b> a <see cref="MenuNotice"/> left by the session's lifecycle shows as a dialog
/// (<see cref="DialogNoticeView"/>) as the menu opens: the host gone, with Continue alone and Back, or the connection
/// lost while joining, with Play together and Back.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public class MainMenuController : MonoBehaviour, IMenuLayer
{
    public const string HostGoneMessage = "The host left the game. Continue carries on alone from where you were, in a saved game of your own.";
    public const string HostGoneWithoutSaveMessage = "The host left the game.";
    public const string LostWhileJoiningFormat = "Lost the connection while joining {0}.";
    public const string ContinueAloneLabel = "Continue alone";
    public const string PlayTogetherLabel = "Play together";
    public const string BackLabel = "Back";
    public const float PanelLeft = 16f;
    public const float PanelWidth = 208f;
    public const float LineHeight = 11f;

    [SerializeField, Tooltip("The panel on the left, a vertical layout holding the name and the buttons.")] private RectTransform panel;
    [SerializeField] private Button continueButton;
    [SerializeField, Tooltip("The line under Continue.")] private TMP_Text continueLine;
    [SerializeField] private Button newGameButton;
    [SerializeField, Tooltip("Saved games, after New game, shown with a save.")] private Button savedGamesButton;
    [SerializeField] private Button playTogetherButton;
    [SerializeField] private Button characterButton;
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button quitButton;
    [SerializeField, Tooltip("The build version at the bottom right.")] private TMP_Text versionText;
    [SerializeField, Tooltip("The dialog for notices and starts that cannot go ahead.")] private DialogNoticeView dialog;

    private readonly Selectable[] looped = new Selectable[7];
    private MenuSessionStarter starter;
    private PlayTogetherController playTogether;
    private SavedGamesController savedGames;
    private WelcomeController welcome;
    private ContinueLine line;
    private MenuNotice notice;
    private OptionsMenuController options;
    private CharacterScreenController character;
    private UISelection selection;
    private MenuStack menus;
    private Action dialogAction;

    internal Button ContinueButton => continueButton;
    internal Button NewGameButton => newGameButton;
    internal Button SavedGamesButton => savedGamesButton;
    internal Button PlayTogetherButton => playTogetherButton;
    internal Button OptionsButton => optionsButton;
    internal Button CharacterButton => characterButton;
    internal TMP_Text ContinueLineText => continueLine;
    internal TMP_Text VersionText => versionText;
    internal DialogNoticeView Dialog => dialog;
    internal RectTransform Panel => panel;

    /// <summary>
    /// Receives the session starter, Play together, Saved games, the Welcome panel, the Continue line, the menu notice,
    /// the options and Character screens, the UI selection and the menu stack.
    /// </summary>
    [Inject]
    public void Construct(
        MenuSessionStarter sessionStarter,
        PlayTogetherController playTogetherScreen,
        SavedGamesController savedGamesScreen,
        WelcomeController welcomePanel,
        ContinueLine continueLineSource,
        MenuNotice menuNotice,
        OptionsMenuController optionsMenu,
        CharacterScreenController characterScreen,
        UISelection uiSelection,
        MenuStack menuStack)
    {
        starter = sessionStarter;
        playTogether = playTogetherScreen;
        savedGames = savedGamesScreen;
        welcome = welcomePanel;
        line = continueLineSource;
        notice = menuNotice;
        options = optionsMenu;
        character = characterScreen;
        selection = uiSelection;
        menus = menuStack;
    }

    /// <summary>
    /// Logs an error when a part is missing, and wires the buttons.
    /// </summary>
    private void Awake()
    {
        if (panel == null || continueButton == null || continueLine == null || newGameButton == null || savedGamesButton == null || playTogetherButton == null
            || characterButton == null || optionsButton == null || quitButton == null || versionText == null || dialog == null)
        {
            Debug.LogError($"{nameof(MainMenuController)} is missing one of its main menu references.", this);
            return;
        }

        continueButton.onClick.AddListener(ContinueGame);
        newGameButton.onClick.AddListener(NewGame);
        savedGamesButton.onClick.AddListener(OpenSavedGames);
        playTogetherButton.onClick.AddListener(OpenPlayTogether);
        characterButton.onClick.AddListener(OpenCharacter);
        optionsButton.onClick.AddListener(OpenOptions);
        quitButton.onClick.AddListener(QuitGame);
        dialog.PrimaryButton.onClick.AddListener(HandleDialogPrimary);
        dialog.SecondaryButton.onClick.AddListener(HideDialog);
    }

    /// <summary>
    /// Unwires the buttons and leaves the menu stack.
    /// </summary>
    private void OnDestroy()
    {
        if (menus != null)
        {
            menus.Remove(this);
        }

        if (dialog == null || continueButton == null)
        {
            return;
        }

        continueButton.onClick.RemoveListener(ContinueGame);
        newGameButton.onClick.RemoveListener(NewGame);
        savedGamesButton.onClick.RemoveListener(OpenSavedGames);
        playTogetherButton.onClick.RemoveListener(OpenPlayTogether);
        characterButton.onClick.RemoveListener(OpenCharacter);
        optionsButton.onClick.RemoveListener(OpenOptions);
        quitButton.onClick.RemoveListener(QuitGame);
        dialog.PrimaryButton.onClick.RemoveListener(HandleDialogPrimary);
        dialog.SecondaryButton.onClick.RemoveListener(HideDialog);
    }

    /// <summary>
    /// Shows the build version and the buttons for whether there is a save, selects Continue (or New game without a
    /// save), and shows the notice the session's lifecycle left, if any; on the first run the Welcome panel shows in the
    /// panel's place first, and the notice once it closes.
    /// </summary>
    private void Start()
    {
        if (!EnsureInjected() || panel == null)
        {
            return;
        }

        versionText.text = Application.version;
        dialog.Hide();
        Refresh();
        SelectFirst();
        if (welcome != null && welcome.ShouldShow)
        {
            SetPanelShown(false);
            welcome.Open(HandleWelcomeClosed);
            return;
        }

        TakeNotice();
    }

    /// <summary>
    /// The Welcome panel closed: shows the panel with its first button selected, then the notice left, if any.
    /// </summary>
    private void HandleWelcomeClosed()
    {
        SetPanelShown(true);
        Refresh();
        SelectFirst();
        TakeNotice();
    }

    /// <summary>
    /// Shows the notice the session's lifecycle left, if any.
    /// </summary>
    private void TakeNotice()
    {
        if (notice.TryTake(out MenuNoticeKind kind, out string address))
        {
            ShowNotice(kind, address);
        }
    }

    /// <summary>
    /// Lays the panel out again when the canvas changes size (a UI size step, the game's window).
    /// </summary>
    private void OnRectTransformDimensionsChange()
    {
        Place();
    }

    /// <summary>
    /// The New game button: without a save on this machine (the first game), opens the Character screen in the panel's
    /// place with its Back reading "Start", which starts the new game, while Escape brings the panel back (Docs/UiRevamp.md,
    /// Phase 11); else starts the new game at once.
    /// </summary>
    public void NewGame()
    {
        if (!EnsureInjected() || !starter.CanStart)
        {
            return;
        }

        if (character == null || character.IsOpen || starter.HasSave)
        {
            StartNewGame();
            return;
        }

        SetPanelShown(false);
        character.OpenToStart(StartNewGame, () => HandleScreenClosed(newGameButton));
    }

    /// <summary>
    /// New game: starts a new offline session, in the starting world at its entry; the entry point the PlayMode tests
    /// drive, which never goes through the Character screen.
    /// </summary>
    public void StartNewGame()
    {
        if (EnsureInjected())
        {
            ShowFailure(starter.StartNew());
        }
    }

    /// <summary>
    /// Continue: continues the latest save offline, the local player starting at its saved place.
    /// </summary>
    public void ContinueGame()
    {
        if (EnsureInjected())
        {
            ShowFailure(starter.Continue());
        }
    }

    /// <summary>
    /// Starts hosting, then continues the latest save when there is one, or starts a new game: Play together's Host and
    /// continue, kept as an entry point for the PlayMode tests.
    /// </summary>
    public void HostGame()
    {
        if (EnsureInjected())
        {
            ShowFailure(starter.Host(true));
        }
    }

    /// <summary>
    /// Opens Play together and joins the network settings' default address, a host on this machine: kept as an entry
    /// point for the PlayMode tests, whose in-process host listens there. Does nothing while a start cannot go ahead.
    /// </summary>
    public void JoinGame()
    {
        if (!EnsureInjected() || !starter.CanStart)
        {
            return;
        }

        if (!playTogether.IsOpen)
        {
            OpenPlayTogether(null, false);
        }

        playTogether.JoinAt(starter.DefaultAddress);
    }

    /// <summary>
    /// Play together: opens the screen in the panel's place.
    /// </summary>
    public void OpenPlayTogether()
    {
        OpenPlayTogether(null, false);
    }

    /// <summary>
    /// Opens Play together with <paramref name="address"/> in its field (the host joined last when null), selecting
    /// Join when <paramref name="selectJoin"/>, unless a start cannot go ahead; the panel comes back, with Play together
    /// selected, when it closes.
    /// </summary>
    public void OpenPlayTogether(string address, bool selectJoin)
    {
        if (!EnsureInjected() || !starter.CanStart || playTogether.IsOpen)
        {
            return;
        }

        SetPanelShown(false);
        playTogether.Open(() => HandleScreenClosed(playTogetherButton), address, selectJoin);
    }

    /// <summary>
    /// Saved games: swaps the panel for the Saved games screen, unless a start cannot go ahead; the panel comes back for
    /// the saves left when it closes, Saved games selected while there is one.
    /// </summary>
    public void OpenSavedGames()
    {
        if (!EnsureInjected() || !starter.CanStart || savedGames == null || savedGames.IsOpen)
        {
            return;
        }

        SetPanelShown(false);
        savedGames.Open(() => HandleScreenClosed(savedGamesButton), false);
    }

    /// <summary>
    /// Options: swaps the panel for the options screen, on its first category (Docs/UiRevamp.md, Phase 8), unless a
    /// transition runs; the panel comes back, with Options selected, when it closes.
    /// </summary>
    public void OpenOptions()
    {
        if (!EnsureInjected() || !starter.CanStart || options == null || options.IsOpen)
        {
            return;
        }

        SetPanelShown(false);
        options.Open(() => HandleScreenClosed(optionsButton), OptionsMenuController.Opening.First);
    }

    /// <summary>
    /// Character: swaps the panel for the Character screen, unless a transition runs; the panel comes back, with
    /// Character selected, when it closes.
    /// </summary>
    public void OpenCharacter()
    {
        if (!EnsureInjected() || !starter.CanStart || character == null || character.IsOpen)
        {
            return;
        }

        SetPanelShown(false);
        character.Open(() => HandleScreenClosed(characterButton));
    }

    /// <summary>
    /// Quit: closes the application.
    /// </summary>
    public void QuitGame()
    {
        ApplicationExit.Quit();
    }

    /// <summary>
    /// Back while the dialog shows: closes it, as its Back button does.
    /// </summary>
    public void HandleBack()
    {
        HideDialog();
    }

    /// <summary>
    /// Shows <paramref name="kind"/>'s dialog about the host at <paramref name="address"/>.
    /// </summary>
    internal void ShowNotice(MenuNoticeKind kind, string address)
    {
        if (kind == MenuNoticeKind.HostGone)
        {
            bool hasSave = starter.HasSave;
            ShowDialog(hasSave ? HostGoneMessage : HostGoneWithoutSaveMessage, hasSave ? ContinueAloneLabel : null, hasSave ? (Action)ContinueGame : null);
        }
        else if (kind == MenuNoticeKind.LostWhileJoining)
        {
            ShowDialog(string.Format(LostWhileJoiningFormat, address), PlayTogetherLabel, () => OpenPlayTogether(address, true));
        }
    }

    /// <summary>
    /// Shows the panel's buttons for whether there is a save, with the Continue line, loops them and lays the panel out.
    /// </summary>
    private void Refresh()
    {
        string text = null;
        bool hasSave = starter.HasSave && line.TryGet(out text);
        continueButton.gameObject.SetActive(hasSave);
        continueLine.gameObject.SetActive(hasSave);
        savedGamesButton.gameObject.SetActive(hasSave);
        if (hasSave)
        {
            continueLine.text = text;
            float width = PanelWidth - 16f;
            float lines = Mathf.Max(1f, Mathf.Round(continueLine.GetPreferredValues(text, width, 0f).y / LineHeight));
            if (continueLine.TryGetComponent(out LayoutElement element))
            {
                element.preferredHeight = lines * LineHeight + 2f;
            }
        }

        int count = 0;
        if (hasSave)
        {
            looped[count++] = continueButton;
        }

        looped[count++] = newGameButton;
        if (hasSave)
        {
            looped[count++] = savedGamesButton;
        }

        looped[count++] = playTogetherButton;
        looped[count++] = characterButton;
        looped[count++] = optionsButton;
        looped[count++] = quitButton;
        GameMenuView.Loop(looped, count, true);
        Place();
    }

    /// <summary>
    /// Stands the panel <see cref="PanelLeft"/> from the canvas's left edge, <see cref="PanelWidth"/> wide and as tall as
    /// its contents, centred down the canvas, on whole UI px.
    /// </summary>
    internal void Place()
    {
        if (panel == null || !(transform is RectTransform root))
        {
            return;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        float height = Mathf.Round(LayoutUtility.GetPreferredHeight(panel));
        panel.anchorMin = new Vector2(0f, 1f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.sizeDelta = new Vector2(PanelWidth, height);
        panel.anchoredPosition = new Vector2(PanelLeft, -Mathf.Floor((root.rect.height - height) * 0.5f));
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
    }

    /// <summary>
    /// Selects Continue, or New game without a save.
    /// </summary>
    private void SelectFirst()
    {
        selection.Select(continueButton.gameObject.activeSelf ? continueButton : newGameButton);
    }

    /// <summary>
    /// Brings the panel back for the saves there are now when a screen closes, with <paramref name="opener"/> selected,
    /// or the first button when it hid (the last save deleted).
    /// </summary>
    private void HandleScreenClosed(Button opener)
    {
        SetPanelShown(true);
        Refresh();
        if (opener.gameObject.activeSelf)
        {
            selection.Select(opener);
        }
        else
        {
            SelectFirst();
        }
    }

    /// <summary>
    /// Shows or hides the panel.
    /// </summary>
    private void SetPanelShown(bool shown)
    {
        panel.gameObject.SetActive(shown);
        if (shown)
        {
            Place();
        }
    }

    /// <summary>
    /// Says why a start did not go ahead in the dialog, with Back; does nothing for null.
    /// </summary>
    private void ShowFailure(string failure)
    {
        if (failure != null)
        {
            ShowDialog(failure, null, null);
        }
    }

    /// <summary>
    /// Shows the dialog with <paramref name="message"/>, a primary button reading <paramref name="primaryLabel"/> that
    /// runs <paramref name="primary"/> (none for null) and Back, puts it on the menu stack and selects its first button.
    /// </summary>
    private void ShowDialog(string message, string primaryLabel, Action primary)
    {
        dialogAction = primary;
        dialog.Show(message, primaryLabel, BackLabel);
        menus.Push(this);
        selection.Select(primaryLabel != null ? dialog.PrimaryButton : dialog.SecondaryButton);
    }

    /// <summary>
    /// The dialog's primary button: closes the dialog, then does what it offered.
    /// </summary>
    private void HandleDialogPrimary()
    {
        Action action = dialogAction;
        HideDialog();
        action?.Invoke();
    }

    /// <summary>
    /// Closes the dialog and selects the panel's first button again.
    /// </summary>
    private void HideDialog()
    {
        if (!dialog.IsShown)
        {
            return;
        }

        dialogAction = null;
        dialog.Hide();
        menus.Remove(this);
        SelectFirst();
    }

    /// <summary>
    /// True when the menu was injected; otherwise logs an error pointing at loading the menu through the Main scene.
    /// </summary>
    private bool EnsureInjected()
    {
        if (starter != null && playTogether != null && line != null && notice != null && selection != null && menus != null)
        {
            return true;
        }

        Debug.LogError($"{nameof(MainMenuController)} was not injected; load MainMenu through the Main scene.", this);
        return false;
    }
}

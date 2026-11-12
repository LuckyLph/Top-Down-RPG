using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The in-game menu's Controls page (Docs/UiRevamp.md, Phase 6), shown in the menu's place: titled "Controls", a row
/// per control with its keys in <c>KeyCap</c>s (Move / attack, Attack move, Stop, the four class abilities, the two
/// weapon abilities, and online the Chat key), then Change keys and Back, which loop. Passive: the presenter sets the
/// keys. <see cref="Resized"/> is raised when the Chat row shows or hides, so the menu lays it out again.
/// </summary>
[DisallowMultipleComponent]
public class ControlsPageView : MonoBehaviour
{
    [SerializeField, Tooltip("The page's laid-out column, under its title.")] private RectTransform content;
    [SerializeField] private TMP_Text moveKey;
    [SerializeField] private TMP_Text attackMoveKey;
    [SerializeField] private TMP_Text stopKey;
    [SerializeField, Tooltip("The six ability slots' keys: the class abilities, then the weapon's.")] private TMP_Text[] abilityKeys = new TMP_Text[0];
    [SerializeField] private TMP_Text chatKey;
    [SerializeField, Tooltip("The Chat row, shown online only.")] private GameObject chatRow;
    [SerializeField] private Button changeKeysButton;
    [SerializeField] private Button backButton;

    private readonly Selectable[] looped = new Selectable[2];
    private bool navigationSet;

    public event Action Resized;

    public RectTransform Box => (RectTransform)transform;
    public RectTransform Content => content;
    public Button ChangeKeysButton => changeKeysButton;
    public Button BackButton => backButton;
    internal TMP_Text MoveKey => moveKey;
    internal TMP_Text AttackMoveKey => attackMoveKey;
    internal TMP_Text StopKey => stopKey;
    internal TMP_Text ChatKey => chatKey;
    internal bool IsChatShown => chatRow != null && chatRow.activeSelf;

    /// <summary>
    /// The key shown for ability slot <paramref name="slot"/>, for tests.
    /// </summary>
    internal TMP_Text AbilityKey(int slot)
    {
        return abilityKeys[slot];
    }

    /// <summary>
    /// Logs an error when a part is missing or there are not six ability keys.
    /// </summary>
    private void Awake()
    {
        if (content == null || moveKey == null || attackMoveKey == null || stopKey == null || abilityKeys == null
            || abilityKeys.Length != AbilitySlots.Count || chatKey == null || chatRow == null || changeKeysButton == null || backButton == null)
        {
            Debug.LogError($"{name} needs its content, the move, attack move, stop and chat keys, six ability keys, the Chat row, Change keys and Back.", this);
        }
    }

    /// <summary>
    /// Shows the keys of Move / attack, Attack move and Stop.
    /// </summary>
    public void SetMovementKeys(string move, string attackMove, string stop)
    {
        moveKey.text = move;
        attackMoveKey.text = attackMove;
        stopKey.text = stop;
    }

    /// <summary>
    /// Shows <paramref name="label"/> as ability slot <paramref name="slot"/>'s key.
    /// </summary>
    public void SetAbilityKey(int slot, string label)
    {
        if (slot >= 0 && slot < abilityKeys.Length)
        {
            abilityKeys[slot].text = label;
        }
    }

    /// <summary>
    /// Shows <paramref name="label"/> as the Chat key.
    /// </summary>
    public void SetChatKey(string label)
    {
        chatKey.text = label;
    }

    /// <summary>
    /// Shows the Chat row when <paramref name="shown"/> (online), and makes Change keys and Back loop.
    /// </summary>
    public void SetChatShown(bool shown)
    {
        if (!navigationSet)
        {
            navigationSet = true;
            looped[0] = changeKeysButton;
            looped[1] = backButton;
            GameMenuView.Loop(looped, looped.Length, false);
        }

        if (chatRow.activeSelf != shown)
        {
            chatRow.SetActive(shown);
            Resized?.Invoke();
        }
    }
}

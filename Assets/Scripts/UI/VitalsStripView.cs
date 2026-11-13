using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The vitals strip (Docs/UiRevamp.md, Phase 7): on a full screen opened in game online, the session's line and the
/// local player's health, "72 / 100" over a bar, so the screen hides neither that the game runs on nor how the player
/// fares. It shows only what it is given.
/// </summary>
[DisallowMultipleComponent]
public sealed class VitalsStripView : MonoBehaviour
{
    [SerializeField] private TMP_Text session;
    [SerializeField] private Image fill;
    [SerializeField] private TMP_Text health;

    private int shownCurrent = -1;
    private int shownMax = -1;

    public bool IsShown => gameObject.activeSelf;
    internal TMP_Text Session => session;
    internal TMP_Text Health => health;
    internal Image Fill => fill;

    /// <summary>
    /// Logs an error when the session line, the bar's fill or the health text is missing.
    /// </summary>
    private void Awake()
    {
        if (session == null || fill == null || health == null)
        {
            Debug.LogError($"{name} needs its session line, its bar's fill and its health text.", this);
        }
    }

    /// <summary>
    /// Shows or hides the strip.
    /// </summary>
    public void SetShown(bool shown)
    {
        if (gameObject.activeSelf != shown)
        {
            gameObject.SetActive(shown);
        }
    }

    /// <summary>
    /// Shows <paramref name="text"/> as the session's line.
    /// </summary>
    public void SetSession(string text)
    {
        session.text = text;
    }

    /// <summary>
    /// Shows <paramref name="current"/> of <paramref name="max"/> health on the bar and its text; builds the text only
    /// when either changed.
    /// </summary>
    public void SetHealth(int current, int max)
    {
        if (current == shownCurrent && max == shownMax)
        {
            return;
        }

        shownCurrent = current;
        shownMax = max;
        fill.fillAmount = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
        health.SetText("{0} / {1}", current, max);
    }
}

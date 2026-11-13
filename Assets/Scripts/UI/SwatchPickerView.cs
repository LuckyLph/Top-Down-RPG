using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The swatch picker (Docs/UiRevamp.md, Phase 7): a raised popover framed in Accent, titled with the colour choice's
/// name in Accent, its swatches as 12 UI px chips 2 apart in rows of <see cref="Columns"/>, and the focused swatch's
/// name under the grid, so a colour is never told by its colour alone. It opens under the chip that opened it, right
/// edges aligned, or over it where it would pass the content's bottom, on whole UI px. The arrows move between its
/// chips, wrapping in their row and column; it raises <see cref="Focused"/> as a chip is selected and
/// <see cref="Picked"/> when one is pressed.
/// </summary>
[DisallowMultipleComponent]
public sealed class SwatchPickerView : MonoBehaviour
{
    public const int Columns = 6;
    public const float Pad = 6f;
    public const float Chip = 12f;
    public const float Step = 14f;
    public const float Line = 11f;
    public const float GridTop = 18f;

    [SerializeField] private TMP_Text title;
    [SerializeField, Tooltip("The chips, as many as the longest swatch list; those past the shown list hide.")]
    private SwatchChipView[] chips;
    [SerializeField] private TMP_Text swatchName;

    private readonly Vector3[] corners = new Vector3[4];
    private int shownCount;
    private bool wired;

    /// <summary>
    /// Raised with a chip's index when it is selected.
    /// </summary>
    public event Action<int> Focused;

    /// <summary>
    /// Raised with a chip's index when it is pressed.
    /// </summary>
    public event Action<int> Picked;

    public bool IsShown => gameObject.activeSelf;
    public int ShownCount => shownCount;
    public int Capacity => chips != null ? chips.Length : 0;
    internal TMP_Text Title => title;
    internal TMP_Text SwatchName => swatchName;

    /// <summary>
    /// Logs an error without its title, chips or name line.
    /// </summary>
    private void Awake()
    {
        if (title == null || chips == null || chips.Length == 0 || swatchName == null)
        {
            Debug.LogError($"{name} needs its title, its chips and its name line.", this);
        }
    }

    /// <summary>
    /// Raises <see cref="Focused"/> and <see cref="Picked"/> from the chips, once.
    /// </summary>
    private void Wire()
    {
        if (wired)
        {
            return;
        }

        wired = true;
        for (int i = 0; i < chips.Length; i++)
        {
            int index = i;
            chips[i].Selected += _ => Focused?.Invoke(index);
            chips[i].Button.onClick.AddListener(() => Picked?.Invoke(index));
        }
    }

    /// <summary>
    /// The chip at <paramref name="index"/>.
    /// </summary>
    public SwatchChipView GetChip(int index)
    {
        return chips[index];
    }

    /// <summary>
    /// Shows <paramref name="swatches"/> of the choice named <paramref name="choice"/> under or over
    /// <paramref name="anchor"/>, keeping above <paramref name="bottom"/> (UI px down from its parent's top) when it can;
    /// a list longer than the chips shows its first ones.
    /// </summary>
    public void Show(string choice, IReadOnlyList<CharacterSwatch> swatches, RectTransform anchor, float bottom)
    {
        Wire();
        shownCount = Mathf.Min(swatches.Count, chips.Length);
        int columns = Mathf.Clamp(shownCount, 1, Columns);
        int rows = (shownCount + columns - 1) / columns;
        float width = Pad + columns * Step - (Step - Chip) + Pad;
        float gridHeight = rows * Step - (Step - Chip);
        float height = GridTop + gridHeight + 3f + Line + 3f;
        title.text = choice;
        for (int i = 0; i < chips.Length; i++)
        {
            bool shown = i < shownCount;
            chips[i].gameObject.SetActive(shown);
            if (!shown)
            {
                continue;
            }

            CharacterSwatch swatch = swatches[i];
            chips[i].SetColour(swatch.Ramps.Count > 0 ? (Color)swatch.Ramps[0].Middle : Color.clear);
            RectTransform rect = (RectTransform)chips[i].transform;
            rect.anchoredPosition = new Vector2(Pad + i % columns * Step, -(GridTop + i / columns * Step));
        }

        RectTransform nameRect = swatchName.rectTransform;
        nameRect.anchoredPosition = new Vector2(Pad, -(GridTop + gridHeight + 3f));
        Navigate(columns, rows);
        Place(anchor, width, height, bottom);
        gameObject.SetActive(true);
    }

    /// <summary>
    /// Hides the picker.
    /// </summary>
    public void Hide()
    {
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Shows <paramref name="text"/> under the grid, the focused swatch's name.
    /// </summary>
    public void SetName(string text)
    {
        swatchName.text = text;
    }

    /// <summary>
    /// Wires the shown chips' navigation as a grid of <paramref name="columns"/> by <paramref name="rows"/>, wrapping in
    /// each row and column.
    /// </summary>
    private void Navigate(int columns, int rows)
    {
        for (int i = 0; i < shownCount; i++)
        {
            int column = i % columns;
            int row = i / columns;
            int rowStart = row * columns;
            int rowCount = Mathf.Min(columns, shownCount - rowStart);
            int columnCount = (shownCount - column + columns - 1) / columns;
            Navigation navigation = new()
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = chips[rowStart + (column - 1 + rowCount) % rowCount].Button,
                selectOnRight = chips[rowStart + (column + 1) % rowCount].Button,
                selectOnUp = chips[column + (row - 1 + columnCount) % columnCount * columns].Button,
                selectOnDown = chips[column + (row + 1) % columnCount * columns].Button
            };
            chips[i].Button.navigation = navigation;
        }
    }

    /// <summary>
    /// Sizes the picker <paramref name="width"/> by <paramref name="height"/> and stands it on whole UI px under
    /// <paramref name="anchor"/>, right edges aligned, or over it when it would pass <paramref name="bottom"/>, inside its
    /// parent's width.
    /// </summary>
    private void Place(RectTransform anchor, float width, float height, float bottom)
    {
        RectTransform rect = (RectTransform)transform;
        RectTransform parent = (RectTransform)rect.parent;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        if (anchor == null || parent == null)
        {
            return;
        }

        anchor.GetWorldCorners(corners);
        Rect area = parent.rect;
        Vector2 bottomRight = parent.InverseTransformPoint(corners[3]);
        Vector2 topRight = parent.InverseTransformPoint(corners[2]);
        float right = bottomRight.x - area.xMin;
        float anchorBottom = area.yMax - bottomRight.y;
        float anchorTop = area.yMax - topRight.y;
        float y = anchorBottom + 2f;
        if (y + height > bottom)
        {
            y = anchorTop - 2f - height;
        }

        float x = Mathf.Clamp(right - width, 0f, Mathf.Max(0f, area.width - width));
        rect.anchoredPosition = new Vector2(Mathf.Round(x), -Mathf.Round(y));
    }
}

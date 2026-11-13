using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The title view behind the main menu (Docs/UiRevamp.md, Phase 11): a still picture of the Overworld at sunset,
/// rendered by the editor's title view recipe, shown at one UI px a pixel and centred on the canvas on whole UI px, so it
/// covers every canvas in the Scale table (768 x 432 against the largest, 683 x 384). On a stretched object, it places
/// its picture as it wakes and whenever the canvas changes size.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class TitleView : MonoBehaviour
{
    [SerializeField, Tooltip("The picture, an image of the title view's sprite.")] private Image picture;

    internal Image Picture => picture;

    /// <summary>
    /// Logs an error without its picture, and places it.
    /// </summary>
    private void Awake()
    {
        if (picture == null || picture.sprite == null)
        {
            Debug.LogError($"{name} needs its picture and the title view's sprite.", this);
            return;
        }

        Place();
    }

    /// <summary>
    /// Places the picture again when the canvas changes size.
    /// </summary>
    private void OnRectTransformDimensionsChange()
    {
        Place();
    }

    /// <summary>
    /// Sizes the picture to its sprite, one UI px a pixel, and centres it on whole UI px.
    /// </summary>
    internal void Place()
    {
        if (picture == null || picture.sprite == null || !(transform is RectTransform root))
        {
            return;
        }

        Rect area = root.rect;
        Vector2 size = picture.sprite.rect.size;
        RectTransform rect = picture.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(Mathf.Floor((area.width - size.x) * 0.5f), -Mathf.Floor((area.height - size.y) * 0.5f));
    }
}

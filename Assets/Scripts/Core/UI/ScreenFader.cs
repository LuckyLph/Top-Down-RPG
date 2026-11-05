using System.Threading;
using UnityEngine;

/// <summary>
/// Full-screen overlay that <see cref="GameFlow"/> fades in and out around scene transitions, on unscaled time so it
/// still runs while game time is frozen. It blocks clicks whenever it is not fully transparent.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public class ScreenFader : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField, Min(0f)] private float fadeDuration = 0.25f;
    [SerializeField] private bool startOpaque = true;

    /// <summary>
    /// Falls back to the sibling <see cref="CanvasGroup"/> when none is assigned and starts opaque or clear as
    /// configured.
    /// </summary>
    private void Awake()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        SetAlpha(startOpaque ? 1f : 0f);
    }

    /// <summary>
    /// Fades the overlay to fully opaque, hiding the screen.
    /// </summary>
    public Awaitable FadeOutAsync(CancellationToken cancellation)
    {
        return FadeToAsync(1f, cancellation);
    }

    /// <summary>
    /// Fades the overlay to fully transparent, revealing the screen.
    /// </summary>
    public Awaitable FadeInAsync(CancellationToken cancellation)
    {
        return FadeToAsync(0f, cancellation);
    }

    /// <summary>
    /// Blocks clicks and lerps the alpha from its current value to <paramref name="targetAlpha"/> over the fade
    /// duration in unscaled time (instantly when the duration is 0 or it is already there), then settles on it.
    /// Throws when <paramref name="cancellation"/> fires mid-fade.
    /// </summary>
    private async Awaitable FadeToAsync(float targetAlpha, CancellationToken cancellation)
    {
        canvasGroup.blocksRaycasts = true;

        float startAlpha = canvasGroup.alpha;
        if (fadeDuration > 0f && !Mathf.Approximately(startAlpha, targetAlpha))
        {
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                await Awaitable.NextFrameAsync(cancellation);
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / fadeDuration);
            }
        }

        SetAlpha(targetAlpha);
    }

    /// <summary>
    /// Sets the alpha, blocking clicks unless fully transparent; the overlay is never interactable itself.
    /// </summary>
    private void SetAlpha(float alpha)
    {
        canvasGroup.alpha = alpha;
        canvasGroup.blocksRaycasts = alpha > 0f;
        canvasGroup.interactable = false;
    }
}

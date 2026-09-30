using System.Threading;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public class ScreenFader : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField, Min(0f)] private float fadeDuration = 0.25f;
    [SerializeField] private bool startOpaque = true;

    public bool IsOpaque => canvasGroup != null && canvasGroup.alpha >= 1f;

    private void Awake()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        SetAlpha(startOpaque ? 1f : 0f);
    }

    public Awaitable FadeOutAsync(CancellationToken cancellation)
    {
        return FadeToAsync(1f, cancellation);
    }

    public Awaitable FadeInAsync(CancellationToken cancellation)
    {
        return FadeToAsync(0f, cancellation);
    }

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

    private void SetAlpha(float alpha)
    {
        canvasGroup.alpha = alpha;
        canvasGroup.blocksRaycasts = alpha > 0f;
        canvasGroup.interactable = false;
    }
}

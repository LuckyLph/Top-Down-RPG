using UnityEngine;

/// <summary>
/// Marks the object in a world's root scene that holds the world's global lights. Only the world the local player is in
/// shows its lighting, since URP lights everything with one global light per sorting layer whichever world it is in, and
/// logs an error whenever a second one is enabled. So in play mode the lights start hidden: this hides its object as
/// the root scene loads, before the lights under it are enabled, and <see cref="LocalWorldPresentation"/> shows it once
/// it is the world on screen.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
public class WorldLighting : MonoBehaviour
{
    /// <summary>
    /// Hides the lights until the presentation shows them, so a world loading beside the one on screen never enables a
    /// second global light.
    /// </summary>
    private void Awake()
    {
        SetShown(false);
    }

    /// <summary>
    /// Shows or hides the world's lights.
    /// </summary>
    public void SetShown(bool shown)
    {
        if (gameObject.activeSelf != shown)
        {
            gameObject.SetActive(shown);
        }
    }
}

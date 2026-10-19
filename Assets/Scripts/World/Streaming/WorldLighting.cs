using UnityEngine;

/// <summary>
/// Marks the object in a world's root scene that holds the world's global lights. Only the world the local player is in
/// shows its lighting, since URP lights everything with one global light per sorting layer whichever world it is in.
/// </summary>
[DisallowMultipleComponent]
public class WorldLighting : MonoBehaviour
{
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

using UnityEngine;

/// <summary>
/// A stable id for a placed object whose state persists, such as a mob spawn point (Docs/WorldStreaming.md: Terms): a
/// GUID string the editor assigns once and keeps unique within the object's world. A copy of an object copies its id,
/// so the editor gives a duplicate a new one (<c>PersistentIds</c>), and the World Editor's validation and the build
/// refuse a world where two objects share one. Read at runtime only, by the world's saved state.
/// </summary>
[DisallowMultipleComponent]
public class PersistentId : MonoBehaviour
{
    [Tooltip("This object's id within its world, a GUID assigned once by the editor. Never edit it by hand: saved state refers to it.")]
    [SerializeField] private string id;

    public string Id => id;

    /// <summary>
    /// Whether the editor has assigned an id.
    /// </summary>
    public bool HasId => !string.IsNullOrEmpty(id);
}

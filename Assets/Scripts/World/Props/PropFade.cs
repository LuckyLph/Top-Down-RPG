using UnityEngine;

/// <summary>
/// The part of a prop that can fade while it hides the local player (Docs/PropGeneration.md, In play): the renderer that
/// fades and the box its drawing covers around the prop's root, which the prop writer measures from the drawing, empty
/// for a prop that never fades. It does nothing itself, so a prop costs nothing per frame: <see cref="PropFading"/>
/// learns of it from its chunk and fades it.
/// </summary>
[DisallowMultipleComponent]
public sealed class PropFade : MonoBehaviour
{
    [SerializeField, Tooltip("The renderer that fades: the prop's Visuals.")]
    private SpriteRenderer visuals;
    [SerializeField, Tooltip("The box the prop's drawing covers around its root, in units; empty for a prop that never fades.")]
    private Rect cover;

    public SpriteRenderer Visuals => visuals;
    public Rect Cover => cover;

    /// <summary>
    /// Whether the prop fades: it has a renderer and a box its drawing covers.
    /// </summary>
    public bool Fades => visuals != null && cover.width > 0f && cover.height > 0f;

    /// <summary>
    /// The box its drawing covers in the world.
    /// </summary>
    public Rect WorldCover
    {
        get
        {
            Vector2 at = transform.position;
            return new Rect(at + cover.position, cover.size);
        }
    }

    /// <summary>
    /// Points at the renderer that fades, and fades over <paramref name="box"/> around the root (empty for never), for
    /// the prop writer and tests.
    /// </summary>
    internal void Configure(SpriteRenderer renderer, Rect box)
    {
        visuals = renderer;
        cover = box;
    }

    /// <summary>
    /// Warns in the editor when the prop has no renderer to fade.
    /// </summary>
    private void OnValidate()
    {
        if (visuals == null)
        {
            Debug.LogWarning($"{name} has no visuals to fade.", this);
        }
    }
}

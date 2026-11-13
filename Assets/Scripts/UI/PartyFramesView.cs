using UnityEngine;

/// <summary>
/// The HUD's party frames at the left edge (Docs/UiRevamp.md, Phase 5): one <see cref="PartyFrameView"/> for each other
/// player there can be, top to bottom, hidden until <see cref="PartyFramesPresenter"/> shows them.
/// </summary>
[DisallowMultipleComponent]
public class PartyFramesView : MonoBehaviour
{
    [SerializeField, Tooltip("The frames, top to bottom.")] private PartyFrameView[] frames = new PartyFrameView[0];

    public int Count => frames != null ? frames.Length : 0;

    /// <summary>
    /// Logs an error without frames.
    /// </summary>
    private void Awake()
    {
        if (frames == null || frames.Length == 0)
        {
            Debug.LogError($"{name} has no party frames.", this);
        }
    }

    /// <summary>
    /// The frame at <paramref name="index"/>, from the top.
    /// </summary>
    public PartyFrameView Frame(int index)
    {
        return frames[index];
    }

    /// <summary>
    /// Assigns the frames in code, for tests that build the view without a prefab.
    /// </summary>
    internal void ConfigureReferences(PartyFrameView[] frameViews)
    {
        frames = frameViews;
    }
}

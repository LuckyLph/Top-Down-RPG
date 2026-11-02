using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where the skies of the regions reach into a chunk (Docs/Lighting.md, Phase 8), written on its grid by the world
/// generator: for each <see cref="RegionSky"/> of a region in or near the chunk, its weight, from 0 to 255, at every
/// <see cref="Spacing"/> cells of the chunk, its edges included, so the samples along a border are the same in both
/// chunks. A sky's weight is full inside its regions and falls to none over the world generator's blend, across their
/// outline. <see cref="RegionSkies"/> reads it, blended between the samples, at the local player's place.
/// </summary>
[DisallowMultipleComponent]
public sealed class ChunkSky : MonoBehaviour
{
    public const int Spacing = 4;
    public const int Samples = ChunkCoords.Size / Spacing + 1;
    public const int SamplesPerSky = Samples * Samples;

    [SerializeField, Tooltip("The skies of the regions in or near the chunk.")]
    private RegionSky[] skies = new RegionSky[0];
    [SerializeField, Tooltip("Each sky's weight, 0 to 255, at every sample of the chunk: sky by sky, row by row from the chunk's bottom left corner.")]
    private byte[] weights = new byte[0];

    public IReadOnlyList<RegionSky> Skies => skies;
    public IReadOnlyList<byte> Weights => weights;

    /// <summary>
    /// The weight of the sky at <paramref name="sky"/> at <paramref name="cell"/>, a place in cells from the chunk's
    /// bottom left corner, from 0 to 1, blended between the four samples around it; 0 for a sky the chunk does not list.
    /// </summary>
    public float WeightAt(int sky, Vector2 cell)
    {
        if (sky < 0 || sky >= skies.Length || weights.Length < (sky + 1) * SamplesPerSky)
        {
            return 0f;
        }

        float u = Mathf.Clamp(cell.x / Spacing, 0f, Samples - 1);
        float v = Mathf.Clamp(cell.y / Spacing, 0f, Samples - 1);
        int x = Mathf.Min((int)u, Samples - 2);
        int y = Mathf.Min((int)v, Samples - 2);
        float fx = u - x;
        float fy = v - y;
        int start = sky * SamplesPerSky + y * Samples + x;
        float bottom = Mathf.Lerp(weights[start], weights[start + 1], fx);
        float top = Mathf.Lerp(weights[start + Samples], weights[start + Samples + 1], fx);
        return Mathf.Lerp(bottom, top, fy) / 255f;
    }

    /// <summary>
    /// Sets the skies and their weights, for the world generator's writer and tests.
    /// </summary>
    internal void Configure(RegionSky[] regionSkies, byte[] sampleWeights)
    {
        skies = regionSkies;
        weights = sampleWeights;
    }

    /// <summary>
    /// Warns when the weights do not hold a sample grid per sky, or a sky is missing.
    /// </summary>
    private void OnValidate()
    {
        if (weights.Length != skies.Length * SamplesPerSky)
        {
            Debug.LogWarning($"{name}: the chunk's sky weights hold {weights.Length} samples where its {skies.Length} skies need {skies.Length * SamplesPerSky}.", this);
        }

        for (int i = 0; i < skies.Length; i++)
        {
            if (skies[i] == null)
            {
                Debug.LogWarning($"{name}: the chunk's sky {i} is missing.", this);
            }
        }
    }
}

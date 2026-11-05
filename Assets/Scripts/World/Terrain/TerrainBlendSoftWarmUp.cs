#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor only: has a <see cref="TerrainBlendRenderer"/> switched to the soft look keep drawing crisp until the soft
/// look's shader variants are compiled (Docs/KnownGaps.md, Phase 6), since the editor compiles a variant in the
/// background the first time it is drawn and draws nothing with it until then. It checks a copy of the renderer's
/// material with the soft keyword on, every pass unlit and with the 2D renderer's light keyword, starts compiling what is
/// missing in the background and, from the editor's updates, calls the renderer back once all of it is compiled, so the
/// ground goes from crisp to soft without blank frames. <see cref="CompileNow"/> compiles them at once instead, for checks
/// that render right away. Nothing runs while the variants are compiled already.
/// </summary>
internal sealed class TerrainBlendSoftWarmUp
{
    private const string LightKeyword = "USE_SHAPE_LIGHT_TYPE_0";
    private readonly Action onReady;
    private Material probe;
    private Material pending;
    private Material readyFor;
    private bool waiting;

    /// <summary>
    /// Creates a warm-up that calls <paramref name="ready"/> once the variants it waits for are compiled.
    /// </summary>
    public TerrainBlendSoftWarmUp(Action ready)
    {
        onReady = ready;
    }

    /// <summary>
    /// Whether a variant is compiled, for tests; by default <see cref="ShaderUtil.IsPassCompiled"/>.
    /// </summary>
    internal Func<Material, int, bool> IsCompiled { get; set; } = ShaderUtil.IsPassCompiled;

    internal bool IsWaiting => waiting;

    /// <summary>
    /// Whether <paramref name="material"/>'s variants with <paramref name="soft"/> on are compiled; when they are not,
    /// starts compiling them in the background and waits for them, calling back once they are. Once they were found
    /// compiled for the material, or while it waits for them, it answers without checking again, allocating nothing.
    /// </summary>
    public bool IsReady(Material material, LocalKeyword soft)
    {
        if (readyFor == material)
        {
            return true;
        }

        if (waiting && pending == material)
        {
            return false;
        }

        if (Compile(material, soft, false))
        {
            readyFor = material;
            return true;
        }

        pending = material;
        if (!waiting)
        {
            waiting = true;
            EditorApplication.update += Poll;
        }

        return false;
    }

    /// <summary>
    /// Compiles <paramref name="material"/>'s variants with <paramref name="soft"/> on now, waiting for them.
    /// </summary>
    public void CompileNow(Material material, LocalKeyword soft)
    {
        Compile(material, soft, true);
        readyFor = material;
    }

    /// <summary>
    /// Stops waiting and destroys the material copy.
    /// </summary>
    public void Stop()
    {
        if (waiting)
        {
            EditorApplication.update -= Poll;
            waiting = false;
        }

        pending = null;

        if (probe != null)
        {
            UnityEngine.Object.DestroyImmediate(probe);
            probe = null;
        }
    }

    /// <summary>
    /// Checks again, from the editor's update, whether the variants it waits for are compiled, and when they are stops
    /// waiting and calls back.
    /// </summary>
    internal void Poll()
    {
        if (probe != null && !AllCompiled(probe))
        {
            return;
        }

        readyFor = pending;
        Stop();
        onReady?.Invoke();
    }

    /// <summary>
    /// Brings the material copy in line with <paramref name="material"/> and the soft keyword on, then compiles each of
    /// its passes, unlit and lit, that is not compiled (waiting for them when <paramref name="now"/>); returns whether all
    /// were compiled already.
    /// </summary>
    private bool Compile(Material material, LocalKeyword soft, bool now)
    {
        if (probe == null || probe.shader != material.shader)
        {
            if (probe != null)
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }

            probe = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
        }
        else
        {
            probe.CopyPropertiesFromMaterial(material);
        }

        probe.enabledKeywords = material.enabledKeywords;
        probe.SetKeyword(new LocalKeyword(probe.shader, soft.name), true);
        bool wasLit = Shader.IsKeywordEnabled(LightKeyword);
        bool compiled = true;
        try
        {
            foreach (bool lit in new[] { false, true })
            {
                SetLight(lit);
                for (int pass = 0; pass < probe.passCount; pass++)
                {
                    if (!IsCompiled(probe, pass))
                    {
                        compiled = false;
                        ShaderUtil.CompilePass(probe, pass, now);
                    }
                }
            }
        }
        finally
        {
            SetLight(wasLit);
        }

        return compiled;
    }

    /// <summary>
    /// Whether every pass of <paramref name="material"/> is compiled, unlit and lit.
    /// </summary>
    private bool AllCompiled(Material material)
    {
        bool wasLit = Shader.IsKeywordEnabled(LightKeyword);
        try
        {
            foreach (bool lit in new[] { false, true })
            {
                SetLight(lit);
                for (int pass = 0; pass < material.passCount; pass++)
                {
                    if (!IsCompiled(material, pass))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        finally
        {
            SetLight(wasLit);
        }
    }

    /// <summary>
    /// Turns the 2D renderer's global light keyword on or off.
    /// </summary>
    private static void SetLight(bool lit)
    {
        if (lit)
        {
            Shader.EnableKeyword(LightKeyword);
        }
        else
        {
            Shader.DisableKeyword(LightKeyword);
        }
    }
}
#endif

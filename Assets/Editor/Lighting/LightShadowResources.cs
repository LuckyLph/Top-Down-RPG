using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The 2D renderer's projected shadow shader (Docs/Lighting.md, Phase 7): the shadows of walls and props are drawn by
/// <see cref="ShaderPath"/>, our copy of URP's, which decides each shadow texel by the art pixel it lies in, so a light's
/// shadow changes only at whole art pixels and stays on the art grid as the camera moves. URP reads it from its 2D
/// renderer resources in the project's URP global settings, which hide it from the inspector and which a URP upgrade
/// may reset to URP's own; <see cref="Apply"/> points them at ours again, and the editor does it by itself once its
/// scripts have loaded whenever it finds another shader there (<see cref="RestoreIfLost"/>, Docs/LightingGaps.md,
/// Phase 5).
/// </summary>
[InitializeOnLoad]
public static class LightShadowResources
{
    public const string ShaderPath = "Assets/Shaders/Lighting/LightShadowProjected.shader";

    private const string ResourcesType = "UnityEngine.Rendering.Universal.Renderer2DResources, Unity.RenderPipelines.Universal.Runtime";
    private const string ProjectedShadowProperty = "projectedShadowShader";

    static LightShadowResources()
    {
        EditorApplication.delayCall -= RestoreIfLostOnLoad;
        EditorApplication.delayCall += RestoreIfLostOnLoad;
    }

    /// <summary>
    /// The projected shadow shader the 2D renderer draws light shadows with, or null without URP's 2D resources.
    /// </summary>
    public static Shader Current
    {
        get
        {
            object resources = Resources(out PropertyInfo property);
            return resources != null ? (Shader)property.GetValue(resources) : null;
        }
    }

    /// <summary>
    /// Points the 2D renderer's projected shadow shader at <see cref="ShaderPath"/> and saves the global settings when it
    /// was not; returns whether it changed.
    /// </summary>
    [MenuItem("Tools/TopDownRPG/Lighting/Use Art-Grid Light Shadows")]
    public static bool Apply()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        object resources = Resources(out PropertyInfo property);
        if (shader == null || resources == null)
        {
            Debug.LogError($"{ShaderPath} or URP's 2D renderer resources are missing; the light shadows stay URP's.");
            return false;
        }

        if ((Shader)property.GetValue(resources) == shader)
        {
            return false;
        }

        property.SetValue(resources, shader);
        if (GraphicsSettings.TryGetCurrentRenderPipelineGlobalSettings(out RenderPipelineGlobalSettings settings))
        {
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        return true;
    }

    /// <summary>
    /// Points the 2D renderer's projected shadow shader at ours again (<see cref="Apply"/>) when URP's 2D resources hold
    /// another, as a URP upgrade leaves them, warning that the global settings were saved; nothing while they hold ours
    /// or either is missing. Returns whether it set it.
    /// </summary>
    public static bool RestoreIfLost()
    {
        Shader ours = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        Shader current = Current;
        if (ours == null || current == null || current == ours || !Apply())
        {
            return false;
        }

        Debug.LogWarning($"URP's 2D renderer resources held {current.name} as the projected shadow shader, as a URP upgrade leaves them; they were set to {ShaderPath} again and the URP global settings saved. Keep only that line of their diff: saving them from the editor empties their m_RuntimeSettings list.");
        return true;
    }

    /// <summary>
    /// <see cref="RestoreIfLost"/> after the editor has loaded its scripts and assets.
    /// </summary>
    private static void RestoreIfLostOnLoad()
    {
        RestoreIfLost();
    }

    /// <summary>
    /// URP's 2D renderer resources and their projected shadow shader's property, found by reflection since both are
    /// internal; null when the project has none.
    /// </summary>
    private static object Resources(out PropertyInfo property)
    {
        Type type = Type.GetType(ResourcesType);
        property = type?.GetProperty(ProjectedShadowProperty, BindingFlags.Instance | BindingFlags.NonPublic);
        if (property == null)
        {
            return null;
        }

        MethodInfo get = typeof(GraphicsSettings).GetMethod(nameof(GraphicsSettings.GetRenderPipelineSettings), BindingFlags.Static | BindingFlags.Public, null, Type.EmptyTypes, null);
        return get?.MakeGenericMethod(type).Invoke(null, null);
    }
}

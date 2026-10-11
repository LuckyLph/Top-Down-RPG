using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tests that the terrain blending shader graph (and the HLSL it calls) imports and compiles for D3D without errors or
/// warnings, in every pass, for vertex and fragment, with no keywords and with the 2D light keyword the area's light
/// sets, since compile warnings reach the console whenever a variant compiles.
/// </summary>
public class TerrainBlendShaderTests
{
    private const string ShaderPath = "Assets/Shaders/Terrain/TerrainBlend.shadergraph";
    private const string LightKeyword = "USE_SHAPE_LIGHT_TYPE_0";

    /// <summary>
    /// The graph imports as a shader without errors or messages.
    /// </summary>
    [Test]
    public void TheGraph_ImportsWithoutErrorsOrMessages()
    {
        Shader shader = LoadShader();

        Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
        Assert.That(Describe(ShaderUtil.GetShaderMessages(shader)), Is.Empty);
    }

    /// <summary>
    /// Every pass compiles for D3D, vertex and fragment, without errors or warnings, with no keywords and, where the
    /// pass has it, with the shape light keyword.
    /// </summary>
    [Test]
    public void EveryPass_CompilesForD3DWithoutErrorsOrWarnings()
    {
        Shader shader = LoadShader();
        ShaderData.Subshader subshader = ShaderUtil.GetShaderData(shader).GetSubshader(0);
        List<string> problems = new();
        for (int i = 0; i < subshader.PassCount; i++)
        {
            ShaderData.Pass pass = subshader.GetPass(i);
            PassIdentifier id = new(0, (uint)i);
            List<string[]> variants = new() { new string[0] };
            foreach (LocalKeyword keyword in ShaderUtil.GetPassKeywords(shader, in id))
            {
                if (keyword.name == LightKeyword)
                {
                    variants.Add(new[] { LightKeyword });
                }
            }

            foreach (string[] keywords in variants)
            {
                foreach (ShaderType stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                {
                    ShaderData.VariantCompileInfo result = pass.CompileVariant(stage, keywords, ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                    string variant = $"{pass.Name} {stage} [{string.Join(" ", keywords)}]";
                    if (!result.Success)
                    {
                        problems.Add($"{variant} failed");
                    }

                    foreach (string message in Describe(result.Messages))
                    {
                        problems.Add($"{variant}: {message}");
                    }
                }
            }
        }

        Assert.That(problems, Is.Empty);
    }

    /// <summary>
    /// Loads the terrain shader and asserts it exists.
    /// </summary>
    private static Shader LoadShader()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        Assert.That(shader, Is.Not.Null);
        return shader;
    }

    /// <summary>
    /// The errors and warnings among <paramref name="messages"/>, described with their file and line.
    /// </summary>
    private static List<string> Describe(ShaderMessage[] messages)
    {
        List<string> described = new();
        foreach (ShaderMessage message in messages)
        {
            if (message.severity == ShaderCompilerMessageSeverity.Error || message.severity == ShaderCompilerMessageSeverity.Warning)
            {
                described.Add($"{message.severity}: {message.message} ({message.file}:{message.line})");
            }
        }

        return described;
    }
}

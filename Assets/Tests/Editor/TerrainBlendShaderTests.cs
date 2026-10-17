using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tests that the terrain blending shader graph (and the HLSL it calls) imports and compiles for D3D without errors or
/// warnings, in every pass, for vertex and fragment, in the crisp look and with the soft look's keyword, each with and
/// without the 2D light keyword the area's light sets, raised ground's keyword and the wide control map's keyword,
/// since compile warnings reach the console whenever a variant compiles; and that the soft, raised ground and wide
/// keywords are local ones the drawing passes declare; and that the debug view's colours are each the probe's own layer
/// whatever the light. The compile test is explicit (category ShaderCompile): it runs only when asked for.
/// </summary>
public class TerrainBlendShaderTests
{
    private const string ShaderPath = "Assets/Shaders/Terrain/TerrainBlend.shadergraph";
    private const string DebugPath = "Assets/Shaders/Terrain/TerrainBlendDebug.hlsl";
    private const string LightKeyword = "USE_SHAPE_LIGHT_TYPE_0";
    private const string SoftKeyword = "_TERRAIN_SOFT";
    private const string RaisedKeyword = "_TERRAIN_RAISED";
    private const string WideKeyword = "_TERRAIN_WIDE";
    private const string ShaderCompileCategory = "ShaderCompile";
    private const int CompileTimeout = 600000;

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
    /// The soft look's keyword, raised ground's and the wide control map's are local keywords of the shader, declared by
    /// the pass that draws the ground lit.
    /// </summary>
    [TestCase(SoftKeyword)]
    [TestCase(RaisedKeyword)]
    [TestCase(WideKeyword)]
    public void TheKeyword_IsLocalToTheShader_AndDeclaredByTheLitPass(string name)
    {
        Shader shader = LoadShader();
        LocalKeyword keyword = new(shader, name);
        ShaderData.Subshader subshader = ShaderUtil.GetShaderData(shader).GetSubshader(0);
        bool declared = false;
        for (int i = 0; i < subshader.PassCount; i++)
        {
            PassIdentifier id = new(0, (uint)i);
            foreach (LocalKeyword passKeyword in ShaderUtil.GetPassKeywords(shader, in id))
            {
                declared |= subshader.GetPass(i).Name == "Sprite Lit" && passKeyword.name == name;
            }
        }

        Assert.That(keyword.isValid, Is.True);
        Assert.That(declared, Is.True);
    }

    /// <summary>
    /// Every pass compiles for D3D, vertex and fragment, without errors or warnings, with no keywords and, where the
    /// pass has them, with every combination of the soft keyword, raised ground's, the wide control map's and the shape
    /// light keyword. It takes over three minutes, past the Test Framework's default timeout, so it has a timeout of its
    /// own, and it runs only when asked for, by name or by its category, after a change to a shader and before merging
    /// one.
    /// </summary>
    [Test, Explicit("Compiles every variant of the terrain shader; run it after changing a shader."), Category(ShaderCompileCategory), Timeout(CompileTimeout)]
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
                if (keyword.name == LightKeyword || keyword.name == SoftKeyword || keyword.name == RaisedKeyword || keyword.name == WideKeyword)
                {
                    int count = variants.Count;
                    for (int v = 0; v < count; v++)
                    {
                        variants.Add(new List<string>(variants[v]) { keyword.name }.ToArray());
                    }
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
    /// The debug view's colours (TB_DEBUG_COLOURS) are one per layer, <see cref="TerrainControlMap.NarrowLayers"/> of
    /// them before the wide variant's and <see cref="TerrainControlMap.WideLayers"/> in all, each channel in quarters
    /// with one full, no two alike; scaled by a white light of any intensity from 0.1 to 1 and rounded to 8 bits, as the
    /// probe's linear render reads them, each reads back through <see cref="TerrainBlendProbe.DebugClass"/> as its own
    /// layer.
    /// </summary>
    [Test]
    public void TheDebugColours_ReadBackAsTheirOwnLayer_UnderAnyLight()
    {
        string source = File.ReadAllText(DebugPath);
        int table = source.IndexOf("TB_DEBUG_COLOURS[TB_MAX_LAYERS] =", System.StringComparison.Ordinal);
        int wide = source.IndexOf("#ifdef _TERRAIN_WIDE", table, System.StringComparison.Ordinal);
        int end = source.IndexOf("};", table, System.StringComparison.Ordinal);
        Assert.That(table, Is.GreaterThanOrEqualTo(0));
        Assert.That(wide, Is.GreaterThan(table).And.LessThan(end));
        List<Vector3> colours = new();
        int narrow = 0;
        foreach (Match match in Regex.Matches(source.Substring(table, end - table), @"float3\(([0-9.]+), ([0-9.]+), ([0-9.]+)\)"))
        {
            colours.Add(new Vector3(Parse(match.Groups[1].Value), Parse(match.Groups[2].Value), Parse(match.Groups[3].Value)));
            narrow += table + match.Index < wide ? 1 : 0;
        }

        Assert.That(narrow, Is.EqualTo(TerrainControlMap.NarrowLayers));
        Assert.That(colours.Count, Is.EqualTo(TerrainControlMap.WideLayers));
        List<string> problems = new();
        for (int i = 0; i < colours.Count; i++)
        {
            Vector3 colour = colours[i];
            if (Mathf.Max(colour.x, Mathf.Max(colour.y, colour.z)) != 1f || colour * 4f != new Vector3(Mathf.Round(colour.x * 4f), Mathf.Round(colour.y * 4f), Mathf.Round(colour.z * 4f)))
            {
                problems.Add($"layer {i}: {colour} is not in quarters with one channel full");
            }

            if (colours.IndexOf(colour) != i)
            {
                problems.Add($"layer {i}: {colour} is layer {colours.IndexOf(colour)}'s colour too");
            }

            foreach (float light in new[] { 0.1f, 0.25f, 0.5f, 0.85f, 1f })
            {
                Color32 read = new((byte)Mathf.RoundToInt(colour.x * light * 255f), (byte)Mathf.RoundToInt(colour.y * light * 255f), (byte)Mathf.RoundToInt(colour.z * light * 255f), 255);
                byte cls = TerrainBlendProbe.DebugClass(read);
                if (cls != i + 1)
                {
                    problems.Add($"layer {i} under a light of {light}: read as class {cls}");
                }
            }
        }

        Assert.That(problems, Is.Empty);
    }

    /// <summary>
    /// A number written in the HLSL.
    /// </summary>
    private static float Parse(string value)
    {
        return float.Parse(value, CultureInfo.InvariantCulture);
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

using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Content checks for the UI (Docs/DesignSystem.md, Phase 4 and Accessibility), on the UI prefabs, the mobs' world UI
/// and the menu scenes, which are only read: every text, image, world UI sprite and control is themed with the
/// project's theme, no text auto-sizes, is bold, or is italic or in capitals below Title, every UI canvas keeps the UI
/// scale, and every control is large enough and reachable.
/// </summary>
public class UiContentTests
{
    private static readonly string[] PrefabFolders = { "Assets/Prefabs/UI", "Assets/Prefabs/Mobs" };

    private readonly List<(string where, GameObject root)> roots = new();
    private readonly List<UnityEngine.SceneManagement.Scene> opened = new();
    private UiTheme theme;

    /// <summary>
    /// Loads the project's theme, the prefabs and the menu scenes, opening each scene beside the open ones when it is
    /// not open already and laying out its layout groups, since a scene saves the rects they drive as they were last
    /// laid out.
    /// </summary>
    [OneTimeSetUp]
    public void Load()
    {
        theme = AssetDatabase.LoadAssetAtPath<UiTheme>(UiThemeUsage.ThemePath);
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", PrefabFolders))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            roots.Add((path, AssetDatabase.LoadAssetAtPath<GameObject>(path)));
        }

        foreach (string path in UiThemeUsage.Scenes)
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetSceneByPath(path);
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                opened.Add(scene);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (LayoutGroup group in root.GetComponentsInChildren<LayoutGroup>(true))
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)group.transform);
                    }
                }
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                roots.Add((path, root));
            }
        }
    }

    /// <summary>
    /// Closes the scenes <see cref="Load"/> opened, unsaved.
    /// </summary>
    [OneTimeTearDown]
    public void Unload()
    {
        foreach (UnityEngine.SceneManagement.Scene scene in opened)
        {
            EditorSceneManager.CloseScene(scene, true);
        }

        opened.Clear();
        roots.Clear();
    }

    /// <summary>
    /// Every text has a <see cref="ThemedText"/> with the project's theme, does not auto-size (a pixel font is crisp
    /// only at its size), is not bold (the font has no bold), and is neither italic nor in capitals below Title.
    /// </summary>
    [Test]
    public void EveryText_IsThemed_WithoutAutoSizeBoldItalicsOrCapitals()
    {
        List<string> problems = new();
        foreach ((string where, TMP_Text text) in All<TMP_Text>())
        {
            string name = $"{where}: {Path(text.transform)}";
            ThemedText themed = text.GetComponent<ThemedText>();
            if (themed == null || themed.Theme != theme)
            {
                problems.Add($"{name} has no ThemedText with the project's theme.");
                continue;
            }

            if (text.enableAutoSizing)
            {
                problems.Add($"{name} auto-sizes.");
            }

            if ((text.fontStyle & FontStyles.Bold) != 0)
            {
                problems.Add($"{name} is bold.");
            }

            if (themed.Size < UiTextSize.Title && (text.fontStyle & (FontStyles.Italic | FontStyles.UpperCase | FontStyles.SmallCaps)) != 0)
            {
                problems.Add($"{name} is italic or in capitals below Title.");
            }
        }

        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// Every Image and RawImage has a <see cref="ThemedGraphic"/> with the project's theme, unless it is a themed
    /// control's target graphic, whose colours the control's block gives.
    /// </summary>
    [Test]
    public void EveryImage_IsThemed_OrAThemedControlsTarget()
    {
        HashSet<Graphic> targets = new();
        foreach ((string _, Selectable selectable) in All<Selectable>())
        {
            if (selectable.targetGraphic != null && selectable.GetComponent<ThemedSelectable>() != null)
            {
                targets.Add(selectable.targetGraphic);
            }
        }

        List<string> problems = new();
        foreach ((string where, Graphic graphic) in All<Graphic>())
        {
            if (graphic is TMP_Text || targets.Contains(graphic))
            {
                continue;
            }

            ThemedGraphic themed = graphic.GetComponent<ThemedGraphic>();
            if (themed == null || themed.Theme != theme)
            {
                problems.Add($"{where}: {Path(graphic.transform)} has no ThemedGraphic with the project's theme.");
            }
        }

        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// The world UI's sprites (the world health bar and its status icons, the move marker and the hover ring) have a
    /// <see cref="ThemedGraphic"/> with the project's theme.
    /// </summary>
    [Test]
    public void TheWorldUisSprites_AreThemed()
    {
        List<string> problems = new();
        int count = 0;
        foreach ((string where, GameObject root) in roots)
        {
            List<SpriteRenderer> sprites = new();
            foreach (WorldHealthBar bar in root.GetComponentsInChildren<WorldHealthBar>(true))
            {
                sprites.AddRange(bar.GetComponentsInChildren<SpriteRenderer>(true));
            }

            foreach (MoveMarker marker in root.GetComponentsInChildren<MoveMarker>(true))
            {
                sprites.Add(marker.GetComponent<SpriteRenderer>());
            }

            foreach (PointerFeedbackLayer layer in root.GetComponentsInChildren<PointerFeedbackLayer>(true))
            {
                sprites.Add(layer.HoverRing);
            }

            foreach (SpriteRenderer sprite in sprites)
            {
                count++;
                ThemedGraphic themed = sprite.GetComponent<ThemedGraphic>();
                if (themed == null || themed.Theme != theme)
                {
                    problems.Add($"{where}: {Path(sprite.transform)} has no ThemedGraphic with the project's theme.");
                }
            }
        }

        Assert.That(count, Is.GreaterThanOrEqualTo(8), "The Weasel's bar and four icons, the move marker and the hover ring.");
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// Every themed component, and every view that reads roles, uses the project's theme.
    /// </summary>
    [Test]
    public void EveryThemedComponentAndView_UsesTheProjectTheme()
    {
        List<string> problems = new();
        foreach ((string where, ThemedGraphic themed) in All<ThemedGraphic>())
        {
            Check(themed.Theme, where, themed, problems);
        }

        foreach ((string where, ThemedSelectable themed) in All<ThemedSelectable>())
        {
            Check(themed.Theme, where, themed, problems);
        }

        foreach (System.Type view in new[] { typeof(AbilitySlotView), typeof(MoveMarker), typeof(PlayerHudView) })
        {
            foreach ((string where, GameObject root) in roots)
            {
                foreach (Component component in root.GetComponentsInChildren(view, true))
                {
                    Check(new SerializedObject(component).FindProperty("theme").objectReferenceValue as UiTheme, where, component, problems);
                }
            }
        }

        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// Every root UI canvas keeps the UI scale through a <see cref="UiPixelScale"/>.
    /// </summary>
    [Test]
    public void EveryUiCanvas_HasAUiPixelScale()
    {
        List<string> problems = new();
        foreach ((string where, Canvas canvas) in All<Canvas>())
        {
            if (canvas.transform.parent != null && canvas.transform.parent.GetComponentInParent<Canvas>(true) != null)
            {
                continue;
            }

            if (canvas.GetComponent<UiPixelScale>() == null)
            {
                problems.Add($"{where}: {Path(canvas.transform)} has no UiPixelScale.");
            }
        }

        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// Every control has a <see cref="ThemedSelectable"/>, keeps navigation on so keyboard and gamepad reach it, and is
    /// at least 12 UI px each way. A scroll list's scrollbar is left out of the last two: it shows where the list is,
    /// and the list scrolls to the selected row and with the wheel, so it is not a control to reach.
    /// </summary>
    [Test]
    public void EveryControl_IsThemed_ReachableAndLargeEnough()
    {
        List<string> problems = new();
        int count = 0;
        foreach ((string where, Selectable selectable) in All<Selectable>())
        {
            count++;
            string name = $"{where}: {Path(selectable.transform)}";
            if (selectable.GetComponent<ThemedSelectable>() == null)
            {
                problems.Add($"{name} has no ThemedSelectable.");
            }

            if (selectable is Scrollbar)
            {
                continue;
            }

            if (selectable.navigation.mode == Navigation.Mode.None)
            {
                problems.Add($"{name} has navigation off.");
            }

            Rect rect = ((RectTransform)selectable.transform).rect;
            if (rect.width < 12f || rect.height < 12f)
            {
                problems.Add($"{name} is {rect.width} x {rect.height} UI px, under 12.");
            }
        }

        Assert.That(count, Is.GreaterThan(10));
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// Every component of type <typeparamref name="T"/> under the loaded roots, with where it is.
    /// </summary>
    private IEnumerable<(string where, T component)> All<T>() where T : Component
    {
        foreach ((string where, GameObject root) in roots)
        {
            foreach (T component in root.GetComponentsInChildren<T>(true))
            {
                yield return (where, component);
            }
        }
    }

    /// <summary>
    /// Adds a problem when <paramref name="used"/> is not the project's theme.
    /// </summary>
    private void Check(UiTheme used, string where, Component component, List<string> problems)
    {
        if (used != theme)
        {
            problems.Add($"{where}: {Path(component.transform)} ({component.GetType().Name}) does not use the project's theme.");
        }
    }

    /// <summary>
    /// The path of <paramref name="transform"/> from its root.
    /// </summary>
    private static string Path(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }

        return path;
    }
}

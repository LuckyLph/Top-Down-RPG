using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Content checks for the UI (Docs/DesignSystem.md, Phases 4 to 6, and Accessibility), on the UI prefabs (the
/// components among them), the mobs' world UI and the menu scenes, which are only read: every text, image, world UI
/// sprite and control is themed with the project's theme, no text auto-sizes, is bold, or is italic or in capitals
/// below Title, every image draws the skin or nothing, every UI canvas keeps the UI scale, every control is large
/// enough and reachable, the marker kinds are different shapes, and the toggle row shows its state as a check mark.
/// </summary>
public class UiContentTests
{
    private static readonly string[] PrefabFolders = { "Assets/Prefabs/UI", "Assets/Prefabs/Mobs" };
    private const string Components = "Assets/Prefabs/UI/Components/";

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
    /// The world UI's sprites (the world health bar and its status pips, the markers and the hover marker) have a
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

            if (where.StartsWith(Components))
            {
                sprites.AddRange(root.GetComponentsInChildren<SpriteRenderer>(true));
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

        Assert.That(count, Is.GreaterThanOrEqualTo(10), "The Weasel's bar and four pips, the markers and the hover marker.");
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
    /// Every UI image draws a sprite of the skin (Assets/Sprites/UI/Skin) or none: the views give icons at runtime.
    /// </summary>
    [Test]
    public void EveryImage_DrawsTheSkinOrNothing()
    {
        List<string> problems = new();
        foreach ((string where, Image image) in All<Image>())
        {
            string sprite = image.sprite != null ? AssetDatabase.GetAssetPath(image.sprite) : null;
            if (sprite != null && !sprite.StartsWith(UiSkinRecipe.Folder + "/"))
            {
                problems.Add($"{where}: {Path(image.transform)} draws {sprite}.");
            }
        }

        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// The move, attack move and hover markers are three prefabs, each drawing its own skin shape in its role (so
    /// colour is never the only cue), and the Gameplay scene's pointer feedback drops the move and attack move markers
    /// and puts a hover marker under the hovered enemy.
    /// </summary>
    [Test]
    public void TheMarkerKinds_HaveDifferentSprites()
    {
        (string prefab, UiSkinPiece piece, UiColour role)[] kinds =
        {
            ("MarkerMove", UiSkinPiece.MarkerMove, UiColour.Positive),
            ("MarkerAttackMove", UiSkinPiece.MarkerAttackMove, UiColour.Danger),
            ("MarkerHover", UiSkinPiece.MarkerHover, UiColour.Danger),
        };
        HashSet<Sprite> sprites = new();
        foreach ((string prefab, UiSkinPiece piece, UiColour role) in kinds)
        {
            GameObject marker = AssetDatabase.LoadAssetAtPath<GameObject>($"{Components}{prefab}.prefab");
            SpriteRenderer renderer = marker.GetComponent<SpriteRenderer>();
            Assert.That(renderer.sprite, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(UiSkinRecipe.PathOf(piece))), $"{prefab} draws its shape.");
            Assert.That(sprites.Add(renderer.sprite), Is.True, $"{prefab} has a sprite of its own.");
            Assert.That(marker.GetComponent<ThemedGraphic>().Role, Is.EqualTo(role), $"{prefab} role.");
            MoveMarker moving = marker.GetComponent<MoveMarker>();
            if (moving != null)
            {
                Assert.That(moving.Role, Is.EqualTo(role), $"{prefab} fades in its role.");
            }
        }

        PointerFeedbackLayer layer = null;
        foreach ((string where, PointerFeedbackLayer found) in All<PointerFeedbackLayer>())
        {
            layer = found;
        }

        Assert.That(layer, Is.Not.Null, "The Gameplay scene's pointer feedback.");
        SerializedObject serialized = new(layer);
        Assert.That(AssetDatabase.GetAssetPath(serialized.FindProperty("markerPrefab").objectReferenceValue), Is.EqualTo($"{Components}MarkerMove.prefab"));
        Assert.That(AssetDatabase.GetAssetPath(serialized.FindProperty("attackMarkerPrefab").objectReferenceValue), Is.EqualTo($"{Components}MarkerAttackMove.prefab"));
        SpriteRenderer hover = (SpriteRenderer)serialized.FindProperty("hoverRing").objectReferenceValue;
        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(hover.gameObject), Is.EqualTo($"{Components}MarkerHover.prefab"));
    }

    /// <summary>
    /// The toggle row (Docs/DesignSystem.md, Phase 6) is a variant of the row with a 12 UI px box before its value, in
    /// Shade framed in Border, holding the skin's check mark in Accent while it is on: its state shows as a mark and
    /// as the On or Off its button reads, never as colour alone. The mark keeps 4.5:1 against the box on the screen's
    /// raised panel, and the frame 3:1 against the panel. Every options screen makes its toggle rows from it.
    /// </summary>
    [Test]
    public void TheToggleRow_ShowsItsStateAsACheckMark()
    {
        GameObject row = AssetDatabase.LoadAssetAtPath<GameObject>($"{Components}RowToggle.prefab");
        Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(row)), Is.EqualTo($"{Components}Row.prefab"), "A variant of the row.");
        KeyBindingRowView view = row.GetComponent<KeyBindingRowView>();
        Image mark = view.ToggleMark;
        Assert.That(mark, Is.Not.Null);
        Assert.That(view.Swatch, Is.Null);
        Assert.That(mark.sprite, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(UiSkinRecipe.PathOf(UiSkinPiece.Check))));
        Assert.That(mark.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.Accent));
        Assert.That(mark.enabled, Is.False, "Unmarked until the row shows its preference on.");

        Image box = mark.transform.parent.GetComponent<Image>();
        Assert.That(box.rectTransform.rect.size, Is.EqualTo(new Vector2(12f, 12f)));
        Assert.That(box.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.Shade));
        ThemedGraphic frame = null;
        foreach (ThemedGraphic graphic in box.GetComponentsInChildren<ThemedGraphic>(true))
        {
            if (graphic.Role == UiColour.Border)
            {
                frame = graphic;
            }
        }

        Assert.That(frame, Is.Not.Null, "The box is framed in Border.");
        Assert.That(box.transform.parent.GetSiblingIndex(), Is.LessThan(view.BindingButton.transform.GetSiblingIndex()), "The box comes before the value.");

        Color panel = theme.Colour(UiColour.SurfaceRaised);
        Color shade = theme.Colour(UiColour.Shade);
        Color well = Color.Lerp(panel, shade, shade.a);
        well.a = 1f;
        Assert.That(DesignMath.ContrastRatio(theme.Colour(UiColour.Accent), well), Is.GreaterThanOrEqualTo(4.5), "The mark against its box.");
        Assert.That(DesignMath.ContrastRatio(theme.Colour(UiColour.Border), panel), Is.GreaterThanOrEqualTo(3.0), "The box's frame against the panel.");

        int screens = 0;
        foreach ((string where, OptionsMenuView options) in All<OptionsMenuView>())
        {
            screens++;
            Object toggleRow = new SerializedObject(options).FindProperty("toggleRowPrefab").objectReferenceValue;
            Assert.That(AssetDatabase.GetAssetPath(toggleRow), Is.EqualTo($"{Components}RowToggle.prefab"), $"{where}'s toggle rows.");
        }

        Assert.That(screens, Is.GreaterThanOrEqualTo(2), "The options prefab and the main menu's screen.");
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

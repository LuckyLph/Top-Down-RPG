using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Content checks for the UI (Docs/DesignSystem.md, Phases 4 to 6, and Accessibility), on the UI prefabs (the
/// components among them), the mobs' and the player's world UI and the menu scenes, which are only read: every text, image, world UI
/// sprite and control is themed with the project's theme, no text auto-sizes, is bold, or is italic or in capitals
/// below Title, every image draws the skin or nothing, every UI canvas keeps the UI scale, every control is large
/// enough and reachable, the marker kinds are different shapes, the toggle row shows its state as a check mark, and
/// the ability slot's states and the message line are skinned and themed (Docs/UiRevamp.md, Phase 1), and the command
/// bar is built from its components (Phase 2).
/// </summary>
public class UiContentTests
{
    private static readonly string[] PrefabFolders = { "Assets/Prefabs/UI", "Assets/Prefabs/Mobs", "Assets/Prefabs/Player" };
    private const string Components = "Assets/Prefabs/UI/Components/";
    private const string IconsFolder = "Assets/Sprites/UI/Icons";

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
    /// The world UI's sprites (the world health bars with their status pips and the players' cast bars, the markers
    /// and the hover markers) have a <see cref="ThemedGraphic"/> with the project's theme.
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
                sprites.Add(layer.AllyHoverRing);
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

        Assert.That(count, Is.GreaterThanOrEqualTo(18), "The Weasel's and the player's bars with their pips, the player's cast bar, the markers and the hover markers.");
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

        foreach (System.Type view in new[] { typeof(AbilitySlotView), typeof(MoveMarker), typeof(WeaponSlotView), typeof(HealthBarView), typeof(StatusIconView) })
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
    /// Every UI image draws a sprite of the skin (Assets/Sprites/UI/Skin), one of the licensed icons
    /// (Assets/Sprites/UI/Icons, the Character screen's turn arrows, Docs/UiRevamp.md, Phase 7) or none: the views give
    /// the other icons at runtime.
    /// </summary>
    [Test]
    public void EveryImage_DrawsTheSkinOrNothing()
    {
        List<string> problems = new();
        foreach ((string where, Image image) in All<Image>())
        {
            string sprite = image.sprite != null ? AssetDatabase.GetAssetPath(image.sprite) : null;
            if (sprite != null && !sprite.StartsWith(UiSkinRecipe.Folder + "/") && !sprite.StartsWith(IconsFolder + "/"))
            {
                problems.Add($"{where}: {Path(image.transform)} draws {sprite}.");
            }
        }

        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// The licensed icons (Assets/Sprites/UI/Icons, Docs/UiRevamp.md, Phase 7) keep their licence beside them:
    /// Pixelarticons' MIT notice, which the art rule's exception for licensed UI icons asks for.
    /// </summary>
    [Test]
    public void TheLicensedIcons_KeepTheirLicenceBesideThem()
    {
        string[] icons = System.IO.Directory.GetFiles(IconsFolder, "*.png");
        string licence = System.IO.Path.Combine(IconsFolder, "Pixelarticons - MIT.txt");
        Assert.That(icons, Is.Not.Empty);
        Assert.That(System.IO.File.Exists(licence), Is.True);
        Assert.That(System.IO.File.ReadAllText(licence), Does.Contain("Copyright (c) 2019 Gerrit Halfmann"));
    }

    /// <summary>
    /// The move, attack move and hover markers are three prefabs, each drawing its own skin shape in its role (so
    /// colour is never the only cue), and the Gameplay scene's pointer feedback drops the move and attack move markers
    /// and puts a hover marker under the hovered enemy. The ally hover marker (Docs/UiRevamp.md, Phase 4) is a variant
    /// of the hover marker, its shape in Positive, which the feedback puts under the hovered ally: the attack cursor,
    /// shown over enemies only, tells the two apart besides their colour.
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

        GameObject allyMarker = AssetDatabase.LoadAssetAtPath<GameObject>($"{Components}MarkerHoverAlly.prefab");
        Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(allyMarker)), Is.EqualTo($"{Components}MarkerHover.prefab"), "A variant of the hover marker.");
        Assert.That(allyMarker.GetComponent<SpriteRenderer>().sprite, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(UiSkinRecipe.PathOf(UiSkinPiece.MarkerHover))), "The enemy's shape.");
        Assert.That(allyMarker.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.Positive));
        SpriteRenderer allyHover = (SpriteRenderer)serialized.FindProperty("allyHoverRing").objectReferenceValue;
        Assert.That(allyHover, Is.Not.Null, "The Gameplay scene's ally hover marker.");
        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(allyHover.gameObject), Is.EqualTo($"{Components}MarkerHoverAlly.prefab"));
    }

    /// <summary>
    /// The chat (Docs/UiRevamp.md, Phase 12): the HUD holds a <c>ChatView</c> anchored at the top left, 144 x 96 UI px,
    /// 8 from the left edge and 98 from the top, drawn after the command bar and before the notice line, the party
    /// frames and the tooltip, taking no pointer at rest. Its window is a hidden <c>Window</c> titled "Chat", the
    /// component a variant of the panel whose header (SurfaceRaised, framed in Border, its title in Accent and its move
    /// grip in ControlSecondary) moves it and whose resize grip in its bottom right corner resizes it; its eleven lines
    /// are hidden <c>ChatLine</c>s, a variant of the message line, left-aligned Text on Outline taking no pointer; and its
    /// field a hidden <c>Field</c>, 13 UI px tall: SurfaceRaised framed in Accent, Text, its placeholder in TextMuted,
    /// one line without rich text.
    /// </summary>
    [Test]
    public void TheHudsChat_IsAWindowOfLinesAndAField()
    {
        GameObject hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PlayerHudCanvas.prefab");
        ChatView chat = hud.GetComponentInChildren<ChatView>(true);
        Assert.That(chat, Is.Not.Null);
        RectTransform rect = (RectTransform)chat.transform;
        Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
        Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));
        Assert.That(rect.pivot, Is.EqualTo(new Vector2(0f, 1f)));
        Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(8f, -98f)));
        Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(144f, 96f)));
        int index = chat.transform.GetSiblingIndex();
        Assert.That(index, Is.GreaterThan(hud.transform.Find("CommandBar").GetSiblingIndex()));
        Assert.That(index, Is.LessThan(hud.GetComponentInChildren<NoticeView>(true).transform.GetSiblingIndex()), "Drawn under the notice line.");
        Assert.That(index, Is.LessThan(hud.GetComponentInChildren<PartyFramesView>(true).transform.GetSiblingIndex()));
        Assert.That(chat.GetComponent<CanvasGroup>().blocksRaycasts, Is.False, "At rest it takes no pointer.");

        SerializedObject serialized = new(chat);
        GameObject window = (GameObject)serialized.FindProperty("window").objectReferenceValue;
        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(window), Is.EqualTo($"{Components}Window.prefab"));
        Assert.That(window.activeSelf, Is.False);
        Assert.That(window.transform.Find("Header/Title").GetComponent<TMP_Text>().text, Is.EqualTo("Chat"));
        Assert.That(chat.MoveHandle.Kind, Is.EqualTo(WindowDrag.Move));
        Assert.That(chat.MoveHandle.transform.IsChildOf(window.transform), Is.True);
        Assert.That(chat.ResizeHandle.Kind, Is.EqualTo(WindowDrag.Resize));
        Assert.That(chat.LineCapacity, Is.EqualTo(11), "Enough for the largest window.");
        SerializedProperty lines = serialized.FindProperty("lines");
        for (int i = 0; i < lines.arraySize; i++)
        {
            TMP_Text line = (TMP_Text)lines.GetArrayElementAtIndex(i).objectReferenceValue;
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(line.gameObject), Is.EqualTo($"{Components}ChatLine.prefab"));
            Assert.That(line.gameObject.activeSelf, Is.False);
        }

        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(chat.Field.gameObject), Is.EqualTo($"{Components}Field.prefab"));
        Assert.That(chat.Field.gameObject.activeSelf, Is.False);
        Assert.That(((RectTransform)chat.Field.transform).rect.height, Is.EqualTo(13f));

        GameObject windowComponent = AssetDatabase.LoadAssetAtPath<GameObject>($"{Components}Window.prefab");
        Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(windowComponent)), Is.EqualTo($"{Components}Panel.prefab"), "A variant of the panel.");
        Transform header = windowComponent.transform.Find("Header");
        Assert.That(header.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.SurfaceRaised));
        Assert.That(header.Find("Frame").GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.Border));
        Assert.That(header.Find("Title").GetComponent<ThemedText>().Colour, Is.EqualTo(UiColour.Accent));
        Assert.That(header.Find("Title").GetComponent<ThemedText>().Background, Is.EqualTo(UiColour.SurfaceRaised));
        Image moveGrip = header.Find("MoveGrip").GetComponent<Image>();
        Assert.That(moveGrip.sprite, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(UiSkinRecipe.PathOf(UiSkinPiece.GripMove))));
        Assert.That(moveGrip.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.ControlSecondary));
        Image resizeGrip = windowComponent.transform.Find("ResizeGrip").GetComponent<Image>();
        Assert.That(resizeGrip.sprite, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(UiSkinRecipe.PathOf(UiSkinPiece.GripResize))));
        Assert.That(resizeGrip.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.ControlSecondary));
        Assert.That(resizeGrip.GetComponent<WindowDragHandle>().Kind, Is.EqualTo(WindowDrag.Resize));

        GameObject lineComponent = AssetDatabase.LoadAssetAtPath<GameObject>($"{Components}ChatLine.prefab");
        Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(lineComponent)), Is.EqualTo($"{Components}MessageLine.prefab"), "A variant of the message line.");
        TMP_Text lineText = lineComponent.GetComponent<TMP_Text>();
        Assert.That(lineText.alignment, Is.EqualTo(TextAlignmentOptions.TopLeft));
        Assert.That(lineText.textWrappingMode, Is.EqualTo(TextWrappingModes.Normal));
        Assert.That(lineText.raycastTarget, Is.False);
        Assert.That(lineComponent.GetComponent<ThemedText>().Colour, Is.EqualTo(UiColour.Text));
        Assert.That(lineComponent.GetComponent<ThemedText>().Background, Is.EqualTo(UiColour.Outline));

        GameObject fieldComponent = AssetDatabase.LoadAssetAtPath<GameObject>($"{Components}Field.prefab");
        TMP_InputField field = fieldComponent.GetComponent<TMP_InputField>();
        Assert.That(fieldComponent.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.SurfaceRaised));
        Assert.That(fieldComponent.transform.Find("Frame").GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.Accent));
        Assert.That(field.textComponent.GetComponent<ThemedText>().Colour, Is.EqualTo(UiColour.Text));
        Assert.That(field.placeholder.GetComponent<ThemedText>().Colour, Is.EqualTo(UiColour.TextMuted));
        Assert.That(field.lineType, Is.EqualTo(TMP_InputField.LineType.SingleLine));
        Assert.That(field.richText, Is.False);
        Assert.That(field.transition, Is.EqualTo(Selectable.Transition.None), "Its face stays dark while typing; its frame marks it.");
    }

    /// <summary>
    /// The party frames (Docs/UiRevamp.md, Phase 5): the HUD holds three <c>PartyFrame</c>s at the left edge, 104 x 82 UI
    /// px 8 from the left and top edges, 28 apart and hidden, before the tooltip. The component is a variant of the panel,
    /// 104 x 26: the skin's large badge with its number in TextOnControl, the name in Text and the state line in
    /// TextMuted on Surface, a bar whose fill in HealthPlayer is read against its track in HealthTrack, and four pips in
    /// its top right corner, hidden; nothing in it but the panel takes the pointer.
    /// </summary>
    [Test]
    public void TheHudsPartyFrames_AreThreePartyFramesAtTheLeftEdge()
    {
        GameObject hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PlayerHudCanvas.prefab");
        PartyFramesView party = hud.GetComponentInChildren<PartyFramesView>(true);
        Assert.That(party, Is.Not.Null);
        RectTransform region = (RectTransform)party.transform;
        Assert.That(region.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
        Assert.That(region.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));
        Assert.That(region.pivot, Is.EqualTo(new Vector2(0f, 1f)));
        Assert.That(region.anchoredPosition, Is.EqualTo(new Vector2(8f, -8f)));
        Assert.That(region.sizeDelta, Is.EqualTo(new Vector2(104f, 82f)));
        Assert.That(party.transform.GetSiblingIndex(), Is.LessThan(hud.GetComponentInChildren<HudTooltip>(true).transform.GetSiblingIndex()), "Drawn under the tooltip.");
        Assert.That(party.Count, Is.EqualTo(PlayerSlots.Count - 1), "One frame for each other player there can be.");
        for (int i = 0; i < party.Count; i++)
        {
            PartyFrameView frame = party.Frame(i);
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(frame.gameObject), Is.EqualTo($"{Components}PartyFrame.prefab"));
            Assert.That(frame.gameObject.activeSelf, Is.False, "Hidden until the presenter shows it.");
            Assert.That(((RectTransform)frame.transform).anchoredPosition, Is.EqualTo(new Vector2(0f, -28f * i)));
        }

        GameObject component = AssetDatabase.LoadAssetAtPath<GameObject>($"{Components}PartyFrame.prefab");
        Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(component)), Is.EqualTo($"{Components}Panel.prefab"), "A variant of the panel.");
        Assert.That(((RectTransform)component.transform).sizeDelta, Is.EqualTo(new Vector2(104f, 26f)));
        PartyFrameView view = component.GetComponent<PartyFrameView>();
        SerializedObject serialized = new(view);
        Assert.That(view.Badge.sprite, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(UiSkinRecipe.PathOf(UiSkinPiece.BadgeLarge))));
        Assert.That(((Component)serialized.FindProperty("number").objectReferenceValue).GetComponent<ThemedText>().Colour, Is.EqualTo(UiColour.TextOnControl));
        Assert.That(((Component)serialized.FindProperty("playerName").objectReferenceValue).GetComponent<ThemedText>().Colour, Is.EqualTo(UiColour.Text));
        Assert.That(view.State.GetComponent<ThemedText>().Colour, Is.EqualTo(UiColour.TextMuted));
        Assert.That(view.State.gameObject.activeSelf, Is.False);
        ThemedGraphic fill = view.Fill.GetComponent<ThemedGraphic>();
        Assert.That(fill.Role, Is.EqualTo(UiColour.HealthPlayer));
        Assert.That(fill.Track, Is.Not.Null);
        Assert.That(fill.Track.Role, Is.EqualTo(UiColour.HealthTrack));
        Assert.That(view.Fill.type, Is.EqualTo(Image.Type.Filled));
        Assert.That(view.Pips.Length, Is.EqualTo(4));
        foreach (Image pip in view.Pips)
        {
            Assert.That(pip.sprite, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(UiSkinRecipe.PathOf(UiSkinPiece.Pip))));
            Assert.That(pip.enabled, Is.False);
        }

        foreach (Graphic graphic in component.GetComponentsInChildren<Graphic>(true))
        {
            Assert.That(graphic.raycastTarget, Is.EqualTo(graphic.gameObject == component), $"{graphic.name} takes the pointer only as the panel.");
        }
    }

    /// <summary>
    /// The toggle row (Docs/DesignSystem.md, Phase 6) is a variant of the row with a 12 UI px box before its value, in
    /// Shade framed in ControlSecondary (in Border until Docs/UiRevamp.md's Phase 3 made frames black, which a box on the
    /// dark panel would lose), holding the skin's check mark in Accent while it is on: its state shows as a mark and as
    /// the On or Off its button reads, never as colour alone. The mark keeps 4.5:1 against the box on the screen's
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
        Assert.That(mark.sprite, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(UiSkinRecipe.PathOf(UiSkinPiece.Check))));
        Assert.That(mark.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.Accent));
        Assert.That(mark.enabled, Is.False, "Unmarked until the row shows its preference on.");

        Image box = mark.transform.parent.GetComponent<Image>();
        Assert.That(box.rectTransform.rect.size, Is.EqualTo(new Vector2(12f, 12f)));
        Assert.That(box.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.Shade));
        ThemedGraphic frame = null;
        foreach (ThemedGraphic graphic in box.GetComponentsInChildren<ThemedGraphic>(true))
        {
            if (graphic.Role == UiColour.ControlSecondary)
            {
                frame = graphic;
            }
        }

        Assert.That(frame, Is.Not.Null, "The box is framed in ControlSecondary.");
        Assert.That(box.transform.parent.GetSiblingIndex(), Is.LessThan(view.BindingButton.transform.GetSiblingIndex()), "The box comes before the value.");

        Color panel = theme.Colour(UiColour.SurfaceRaised);
        Color shade = theme.Colour(UiColour.Shade);
        Color well = Color.Lerp(panel, shade, shade.a);
        well.a = 1f;
        Assert.That(DesignMath.ContrastRatio(theme.Colour(UiColour.Accent), well), Is.GreaterThanOrEqualTo(4.5), "The mark against its box.");
        Assert.That(DesignMath.ContrastRatio(theme.Colour(UiColour.ControlSecondary), panel), Is.GreaterThanOrEqualTo(3.0), "The box's frame against the panel.");

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
    /// The ability slot's states (Docs/UiRevamp.md, Phase 1) are skin pieces in their roles, hidden until shown and
    /// never taking the pointer: its frame (Border, Accent while casting), the corner brackets 1 UI px outside it in
    /// Accent, and the hatch, tiled in ControlDisabled inside the frame and under the name, so the name stays readable;
    /// since Phase 3 its key sits in its top left corner and its cooldown seconds in its top right. The message line is
    /// a variant of the notice line in Text outlined in Outline, and the HUD's ability bar shows its
    /// failures on one, hidden until a failure, above the status row.
    /// </summary>
    [Test]
    public void TheSlotStatesAndTheMessageLine_AreSkinnedAndThemed()
    {
        AbilitySlotView slot = AssetDatabase.LoadAssetAtPath<GameObject>($"{Components}SlotAbility.prefab").GetComponent<AbilitySlotView>();
        Assert.That(slot.Border.sprite, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(UiSkinRecipe.PathOf(UiSkinPiece.SlotFrame))));
        Assert.That(slot.Border.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.Border));
        Assert.That(slot.KeyText.rectTransform.anchorMin, Is.EqualTo(new Vector2(0f, 1f)), "The key in the top left corner (Docs/UiRevamp.md, Phase 3).");
        Assert.That(slot.KeyText.alignment, Is.EqualTo(TextAlignmentOptions.TopLeft));
        Assert.That(slot.CooldownText.rectTransform.anchorMin, Is.EqualTo(new Vector2(1f, 1f)), "The cooldown seconds in the top right corner.");
        Assert.That(slot.CooldownText.alignment, Is.EqualTo(TextAlignmentOptions.TopRight));

        Image queued = slot.Queued;
        Assert.That(queued.sprite, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(UiSkinRecipe.PathOf(UiSkinPiece.SlotCorners))));
        Assert.That(queued.type, Is.EqualTo(Image.Type.Sliced));
        Assert.That(queued.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.Accent));
        Assert.That(queued.rectTransform.offsetMin, Is.EqualTo(new Vector2(-1f, -1f)), "Just outside the slot.");
        Assert.That(queued.rectTransform.offsetMax, Is.EqualTo(new Vector2(1f, 1f)));

        Image hatch = slot.Hatch;
        Assert.That(hatch.sprite, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(UiSkinRecipe.PathOf(UiSkinPiece.Hatch))));
        Assert.That(hatch.type, Is.EqualTo(Image.Type.Tiled));
        Assert.That(hatch.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.ControlDisabled));
        Assert.That(hatch.transform.GetSiblingIndex(), Is.LessThan(slot.NameText.transform.GetSiblingIndex()), "Under the name.");
        foreach (Image state in new[] { queued, hatch })
        {
            Assert.That(state.enabled, Is.False, $"{state.name} hides until shown.");
            Assert.That(state.raycastTarget, Is.False, $"{state.name} never takes the pointer.");
        }

        GameObject line = AssetDatabase.LoadAssetAtPath<GameObject>($"{Components}MessageLine.prefab");
        Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(line)), Is.EqualTo($"{Components}NoticeLine.prefab"), "A variant of the notice line.");
        ThemedText text = line.GetComponent<ThemedText>();
        Assert.That(text.Colour, Is.EqualTo(UiColour.Text));
        Assert.That(text.Background, Is.EqualTo(UiColour.Outline));

        GameObject hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PlayerHudCanvas.prefab");
        TMP_Text message = hud.GetComponentInChildren<AbilityBarView>(true).MessageText;
        Assert.That(message, Is.Not.Null);
        Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(message.gameObject)), Is.EqualTo($"{Components}MessageLine.prefab"));
        Assert.That(message.gameObject.activeSelf, Is.False, "Hidden until a failure.");
        RectTransform statusRow = (RectTransform)hud.GetComponentInChildren<StatusBarView>(true).transform;
        Assert.That(message.rectTransform.anchoredPosition.y, Is.GreaterThanOrEqualTo(statusRow.anchoredPosition.y + statusRow.sizeDelta.y), "Above the status row.");
    }

    /// <summary>
    /// The command bar (Docs/UiRevamp.md, Phase 2): its health bar a <c>Bar</c> whose fill is HealthPlayer over a part
    /// just lost in HealthLost, both read against the track, with the skin's tick in HealthTrack as a hidden template,
    /// its text outlined; the weapon slot in the ability row, after the gap; the buffs' group of six <c>StatusIcon</c>s
    /// left of the abilities and the debuffs' of six <c>StatusIconDebuff</c>s right of them, each a 3 x 2 grid filling
    /// from the abilities outwards, bottom row first; and the debuff icon a variant of the status icon framed in Danger
    /// with the skin's notch in its top right corner.
    /// </summary>
    [Test]
    public void TheCommandBar_IsBuiltFromItsComponents()
    {
        GameObject hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PlayerHudCanvas.prefab");
        HealthBarView bar = hud.GetComponent<PlayerHudView>().HealthBar;
        Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(bar.gameObject)), Is.EqualTo($"{Components}Bar.prefab"));
        Assert.That(bar.Fill.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.HealthPlayer));
        Assert.That(bar.Lost.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.HealthLost));
        Assert.That(bar.Lost.transform.GetSiblingIndex(), Is.LessThan(bar.Fill.transform.GetSiblingIndex()), "The loss sits under the fill.");
        Assert.That(bar.Lost.enabled, Is.False);
        Assert.That(bar.Fill.GetComponent<ThemedGraphic>().Track, Is.Not.Null);
        Assert.That(bar.Lost.GetComponent<ThemedGraphic>().Track, Is.Not.Null);
        Image tick = (Image)new SerializedObject(bar).FindProperty("tickTemplate").objectReferenceValue;
        Assert.That(tick.sprite, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(UiSkinRecipe.PathOf(UiSkinPiece.BarTick))));
        Assert.That(tick.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.HealthTrack));
        Assert.That(tick.gameObject.activeSelf, Is.False, "A template.");
        Assert.That(bar.Text.GetComponent<ThemedText>().Background, Is.EqualTo(UiColour.Outline));
        PixelTextScale smaller = bar.Text.GetComponent<PixelTextScale>();
        Assert.That(smaller, Is.Not.Null, "The health text draws smaller than the zoom (Docs/UiRevamp.md, Phase 3).");
        Assert.That(smaller.TargetScale, Is.EqualTo(0.75f));
        Assert.That(((RectTransform)bar.transform).sizeDelta, Is.EqualTo(new Vector2(242f, 13f)), "13 tall, so the 9 px outlined text centres in its 11 px inside (Docs/UiRevamp.md, Phase 3).");

        AbilityBarView abilities = hud.GetComponentInChildren<AbilityBarView>(true);
        Transform weapon = hud.GetComponent<PlayerHudView>().WeaponSlot.transform;
        Assert.That(weapon.parent, Is.EqualTo(abilities.transform), "The weapon slot is in the ability row.");
        Assert.That(weapon.GetSiblingIndex(), Is.EqualTo(abilities.transform.Find("Spacer").GetSiblingIndex() + 1), "After the gap, before its two abilities.");
        Assert.That(((RectTransform)abilities.transform).sizeDelta.x, Is.EqualTo(242f));

        StatusBarView statuses = hud.GetComponentInChildren<StatusBarView>(true);
        foreach ((StatusKind kind, string prefab, GridLayoutGroup.Corner corner) in new[] { (StatusKind.Buff, "StatusIcon", GridLayoutGroup.Corner.LowerRight), (StatusKind.Debuff, "StatusIconDebuff", GridLayoutGroup.Corner.LowerLeft) })
        {
            Assert.That(statuses.GroupCapacity(kind), Is.EqualTo(6));
            GridLayoutGroup grid = statuses.Icon(kind, 0).transform.parent.GetComponent<GridLayoutGroup>();
            Assert.That(grid.startCorner, Is.EqualTo(corner), $"The {kind}s fill from the abilities outwards.");
            Assert.That(grid.startAxis, Is.EqualTo(GridLayoutGroup.Axis.Horizontal), "Bottom row first.");
            Assert.That(grid.constraintCount, Is.EqualTo(3));
            for (int i = 0; i < 6; i++)
            {
                Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(statuses.Icon(kind, i).gameObject)), Is.EqualTo($"{Components}{prefab}.prefab"));
            }
        }

        GameObject debuff = AssetDatabase.LoadAssetAtPath<GameObject>($"{Components}StatusIconDebuff.prefab");
        Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(debuff)), Is.EqualTo($"{Components}StatusIcon.prefab"), "A variant of the status icon.");
        Assert.That(debuff.transform.Find("Frame").GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.Danger));
        Image notch = debuff.transform.Find("Notch").GetComponent<Image>();
        Assert.That(notch.sprite, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(UiSkinRecipe.PathOf(UiSkinPiece.Notch))));
        Assert.That(notch.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.Danger));
        Assert.That(notch.raycastTarget, Is.False);
        Assert.That(notch.rectTransform.anchorMin, Is.EqualTo(Vector2.one), "In the top right corner.");
    }

    /// <summary>
    /// The HUD's notice line (Docs/UiRevamp.md, Phase 3): a <c>Panel</c> at the top centre, 8 UI px below the top edge,
    /// hidden until a notice shows and never taking the pointer, holding a wrapping <c>NoticeLine</c> in Accent read
    /// against the panel's Surface, centred, ending in an ellipsis past what fits, that <see cref="NoticeView"/> sizes,
    /// up to the notice band's 240 x 24 UI px and two lines; drawn before the tooltip.
    /// </summary>
    [Test]
    public void TheNoticeLine_IsAPanelHoldingAWrappingNoticeLine_AtTheTopCentre()
    {
        GameObject hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PlayerHudCanvas.prefab");
        NoticeView notice = hud.GetComponentInChildren<NoticeView>(true);
        Assert.That(notice, Is.Not.Null);
        Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(notice.gameObject)), Is.EqualTo($"{Components}Panel.prefab"));
        Assert.That(notice.gameObject.activeSelf, Is.False, "Hidden until a notice shows.");
        Assert.That(notice.GetComponent<Image>().raycastTarget, Is.False, "The world under it still takes clicks.");
        RectTransform panel = (RectTransform)notice.transform;
        Assert.That(panel.anchorMin, Is.EqualTo(new Vector2(0.5f, 1f)));
        Assert.That(panel.anchorMax, Is.EqualTo(new Vector2(0.5f, 1f)));
        Assert.That(panel.pivot, Is.EqualTo(new Vector2(0.5f, 1f)));
        Assert.That(panel.anchoredPosition, Is.EqualTo(new Vector2(0f, -8f)), "8 UI px below the top edge.");
        Assert.That(notice.MaxWidth, Is.EqualTo(240f));
        Assert.That(notice.MaxHeight, Is.EqualTo(24f));
        Assert.That(notice.MaxLines, Is.EqualTo(2));
        Assert.That(notice.transform.GetSiblingIndex(), Is.LessThan(hud.GetComponentInChildren<HudTooltip>(true).transform.GetSiblingIndex()), "The tooltip draws over it.");

        TMP_Text text = notice.Label;
        Assert.That(text.transform.parent, Is.EqualTo(notice.transform));
        Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(text.gameObject)), Is.EqualTo($"{Components}NoticeLine.prefab"));
        Assert.That(text.textWrappingMode, Is.EqualTo(TextWrappingModes.Normal), "The notice line wraps.");
        Assert.That(text.overflowMode, Is.EqualTo(TextOverflowModes.Ellipsis), "A notice past two lines ends in an ellipsis rather than leaving the band.");
        Assert.That(text.horizontalAlignment, Is.EqualTo(HorizontalAlignmentOptions.Center));
        Assert.That(text.raycastTarget, Is.False);
        ThemedText themed = text.GetComponent<ThemedText>();
        Assert.That(themed.Colour, Is.EqualTo(UiColour.Accent));
        Assert.That(themed.Background, Is.EqualTo(UiColour.Surface), "Read against the panel.");
        Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>($"{Components}NoticeLine.prefab").GetComponent<TMP_Text>().textWrappingMode, Is.EqualTo(TextWrappingModes.Normal), "The component wraps.");
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

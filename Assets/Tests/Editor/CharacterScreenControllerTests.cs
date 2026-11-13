using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Tests for <see cref="CharacterGroups"/> and <see cref="CharacterScreenController"/> (Docs/UiRevamp.md, Phase 7) on the
/// real Character screen prefab, the project's looks and colours, in-memory settings and a fake preview: the rows read
/// from the dyes, the chips a part not worn hides, the buttons and resets, Reset all behind its confirmation, the swatch
/// picker's names, previews and picks, the foot line, the preview's turn and pose, and Back.
/// </summary>
public class CharacterScreenControllerTests
{
    private const string ScreenPath = "Assets/Prefabs/UI/CharacterScreen.prefab";

    private GameObject canvas;
    private GameObject eventObject;
    private EventSystem events;
    private CharacterScreenView view;
    private FakeMenuInput menuInput;
    private MenuStack menus;
    private UserSettings settings;
    private FakeCharacterPreview preview;
    private CharacterScreenController controller;
    private CharacterColours colours;
    private int closedCount;

    /// <summary>
    /// Instantiates the screen in a 640 x 360 UI px parent beside an event system and starts the controller over
    /// in-memory settings and a fake preview.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        eventObject = new GameObject("EventSystem");
        events = eventObject.AddComponent<EventSystem>();
        canvas = new GameObject("Canvas", typeof(RectTransform));
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(640f, 360f);
        GameObject screen = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ScreenPath), canvas.transform);
        view = screen.GetComponent<CharacterScreenView>();
        colours = view.Colours;
        menuInput = new FakeMenuInput();
        menus = new MenuStack(menuInput);
        settings = new UserSettings(new MemorySaveStore());
        preview = new FakeCharacterPreview();
        controller = new CharacterScreenController(view, preview, menus, new UISelection(events), settings);
        controller.Start();
        closedCount = 0;
    }

    /// <summary>
    /// Disposes the controller and destroys the screen and the event system.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        controller.Dispose();
        Object.DestroyImmediate(canvas);
        Object.DestroyImmediate(eventObject);
    }

    /// <summary>
    /// The project's rows come from the dyes: the Look row, then Body with the skin, the eyes, the belt and the buckle
    /// (the skin shows under tops, legs and bare hands alike, so no one part owns it), then a row per part slot a player
    /// picks, each with the colours its options carry; the weapon's slot, which shows the equipped weapon's gear, has
    /// none, though its gear carries the belt's, buckle's and strap's dyes.
    /// </summary>
    [Test]
    public void TheRows_AreReadFromTheDyes()
    {
        List<CharacterGroup> groups = CharacterGroups.Build(view.Looks, colours);
        Assert.That(groups.Select(group => group.Label), Is.EqualTo(new[] { "Look", "Body", "Hair", "Headwear", "Top", "Cape", "Legs", "Boots", "Hands", "Bag" }));
        Assert.That(groups.Select(group => group.Kind).Take(3), Is.EqualTo(new[] { CharacterGroupKind.Look, CharacterGroupKind.Body, CharacterGroupKind.Part }));
        string[][] choices = groups.Select(group => group.Choices.Select(choice => colours.Get(choice).Name).ToArray()).ToArray();
        Assert.That(choices, Is.EqualTo(new[]
        {
            new string[0],
            new[] { "Skin", "Eyes", "Belt", "Buckle" },
            new[] { "Hair" },
            new[] { "Headwear" },
            new[] { "Tunic", "Shirt" },
            new[] { "Cape", "Lining" },
            new[] { "Trousers" },
            new[] { "Boots" },
            new[] { "Gloves" },
            new[] { "Strap", "Pouch" },
        }));
        Assert.That(controller.RowCount, Is.EqualTo(10));
        Assert.That(view.Rows.childCount, Is.EqualTo(10));
        Assert.That(controller.GetRow(4).Label.text, Is.EqualTo("Top"));
    }

    /// <summary>
    /// A colour choice carried by two part slots goes on the Body row, and one carried by a single slot on that slot's
    /// row; a look without parts gets only the Look row.
    /// </summary>
    [Test]
    public void AColourTwoPartsCarry_GoesOnTheBodyRow()
    {
        CharacterAnimationSet set = CharacterAnimationSet.Create(new CharacterAnimationClip[0], 1 << (int)CharacterDye.Eyes);
        CharacterParts parts = CharacterParts.Create(set, null, 0f, new[]
        {
            new CharacterPartSlot(CharacterSlot.Top, -1, new[] { new CharacterPartOption("Tunic", Mask(CharacterDye.Tunic, CharacterDye.Skin), new TextAsset[0]), new CharacterPartOption("Vest", Mask(CharacterDye.Tunic), new TextAsset[0]) }),
            new CharacterPartSlot(CharacterSlot.Legs, -1, new[] { new CharacterPartOption("Trousers", Mask(CharacterDye.Trousers), new TextAsset[0]), new CharacterPartOption("Shorts", Mask(CharacterDye.Trousers, CharacterDye.Skin), new TextAsset[0]) }),
            new CharacterPartSlot(CharacterSlot.Weapon, -1, new[] { new CharacterPartOption("Sword", Mask(CharacterDye.Belt), new TextAsset[0]) }),
        });
        CharacterLookCatalog looks = CharacterLookCatalog.Create(new CharacterLook("Hero", set, null, parts));
        CharacterSwatch swatch = new("Any", new ColourRamp(Color.black, Color.black, Color.white, Color.white));
        CharacterColours testColours = CharacterColours.Create(
            new[] { new CharacterSwatchList("List", new[] { swatch }) },
            new[]
            {
                new CharacterColourChoice("Skin", "List", "Any", CharacterDye.Skin),
                new CharacterColourChoice("Eyes", "List", "Any", CharacterDye.Eyes),
                new CharacterColourChoice("Tunic", "List", "Any", CharacterDye.Tunic),
                new CharacterColourChoice("Trousers", "List", "Any", CharacterDye.Trousers),
                new CharacterColourChoice("Belt", "List", "Any", CharacterDye.Belt),
            });
        try
        {
            List<CharacterGroup> groups = CharacterGroups.Build(looks, testColours);
            Assert.That(groups.Select(group => group.Label), Is.EqualTo(new[] { "Body", "Top", "Legs" }), "One look has no Look row, and the weapon's slot no row.");
            Assert.That(groups[0].Choices.Select(choice => testColours.Get(choice).Name), Is.EqualTo(new[] { "Skin", "Eyes", "Belt" }));
            Assert.That(groups[1].Choices.Select(choice => testColours.Get(choice).Name), Is.EqualTo(new[] { "Tunic" }));
            Assert.That(groups[2].Choices.Select(choice => testColours.Get(choice).Name), Is.EqualTo(new[] { "Trousers" }));
            Assert.That(CharacterGroups.ShowsChoice(groups[0], 0, testColours, looks.Get(0), parts, default), Is.True, "The skin shows under the tunic.");
            Assert.That(CharacterGroups.ShowsChoice(groups[0], 0, testColours, looks.Get(0), parts, default(CharacterPartPicks).With(CharacterSlot.Top, 1)), Is.False, "Under a vest and trousers no skin shows.");
            Assert.That(CharacterGroups.ShowsChoice(groups[0], 1, testColours, looks.Get(0), parts, default), Is.True, "The eyes are the body's own.");
        }
        finally
        {
            Object.DestroyImmediate(testColours);
            Object.DestroyImmediate(looks);
            Object.DestroyImmediate(parts);
            Object.DestroyImmediate(set);
        }
    }

    /// <summary>
    /// The rows show the settings' look and parts, and as chips the colours the parts worn carry: by default no
    /// headwear, cape, gloves or bag, so their rows have no chips; putting on a cape adds its colour and lining, an
    /// unlined one only its colour, and the Bandit, which wears no parts, leaves only the Look row. A chip shows its
    /// swatch's colour.
    /// </summary>
    [Test]
    public void TheChips_ShowTheColoursOfThePartsWorn()
    {
        Assert.That(Values(), Is.EqualTo(new[] { "Adventurer", null, "Tousled", "None", "Tunic", "None", "Trousers", "Tall", "Bare", "None" }));
        Assert.That(ChipCounts(), Is.EqualTo(new[] { 0, 4, 1, 0, 2, 0, 1, 1, 0, 0 }));
        Assert.That(Names(5), Is.Empty);

        settings.SetCharacterPart("Cape", "Cape");
        Assert.That(Names(5), Is.EqualTo(new[] { "Cape", "Lining" }));
        settings.SetCharacterPart("Cape", "Unlined");
        Assert.That(Names(5), Is.EqualTo(new[] { "Cape" }), "An unlined cape has no lining.");
        settings.SetCharacterPart("Top", "Shirt");
        Assert.That(Names(4), Is.EqualTo(new[] { "Shirt" }), "A shirt alone shows no tunic.");
        settings.SetCharacterPart("Hands", "Gloves");
        settings.SetCharacterPart("Bag", "Satchel");
        Assert.That(Names(8), Is.EqualTo(new[] { "Gloves" }));
        Assert.That(Names(9), Is.EqualTo(new[] { "Strap", "Pouch" }));

        settings.SetCharacterColour("Tunic", "Red");
        settings.SetCharacterPart("Top", string.Empty);
        int tunic = colours.IndexOf("Tunic");
        Color red = (Color)colours.Swatches(tunic)[colours.IndexOfSwatch(tunic, "Red")].Ramps[0].Middle;
        Assert.That(controller.GetRow(4).GetChip(0).Fill.color, Is.EqualTo(red));

        settings.SetCharacterLook("Bandit");
        Assert.That(Shown(), Is.EqualTo(new[] { true, false, false, false, false, false, false, false, false, false }));
        Assert.That(controller.GetRow(0).ValueText.text, Is.EqualTo("Bandit"));
        settings.SetCharacterLook(string.Empty);
        Assert.That(Names(5), Is.EqualTo(new[] { "Cape" }), "The picks a hidden row kept show again.");
    }

    /// <summary>
    /// A row's button keeps the next look or option in the settings, after the last the first (kept as no pick); a
    /// part's Reset puts back its first option and every colour of its row, those hidden too; the Look row's Reset the
    /// first look.
    /// </summary>
    [Test]
    public void TheButtonsAndResets_KeepTheirPicks()
    {
        controller.Open(null);
        controller.GetRow(2).ValueButton.onClick.Invoke();
        Assert.That(settings.CharacterPart("Hair"), Is.EqualTo("Crop"));
        Assert.That(controller.GetRow(2).ValueText.text, Is.EqualTo("Crop"));
        controller.GetRow(6).ValueButton.onClick.Invoke();
        controller.GetRow(6).ValueButton.onClick.Invoke();
        Assert.That(settings.CharacterPart("Legs"), Is.Empty, "After the last option, the first.");
        controller.GetRow(0).ValueButton.onClick.Invoke();
        Assert.That(settings.CharacterLook, Is.EqualTo("Bandit"));
        controller.GetRow(0).ResetButton.onClick.Invoke();
        Assert.That(settings.CharacterLook, Is.EqualTo("Adventurer"));

        settings.SetCharacterPart("Cape", "Long");
        settings.SetCharacterColour("Cape", "Navy");
        settings.SetCharacterColour("Lining", "Red");
        settings.SetCharacterPart("Cape", "Unlined");
        controller.GetRow(5).ResetButton.onClick.Invoke();
        Assert.That(settings.CharacterPart("Cape"), Is.Empty);
        Assert.That(settings.CharacterColour("Cape"), Is.Empty);
        Assert.That(settings.CharacterColour("Lining"), Is.Empty, "The hidden lining's pick goes too.");

        settings.SetCharacterColour("Skin", "Deep");
        controller.GetRow(1).ResetButton.onClick.Invoke();
        Assert.That(settings.CharacterColour("Skin"), Is.Empty);
        Assert.That(settings.CharacterPart("Hair"), Is.EqualTo("Crop"), "The Body row's Reset leaves the parts.");
    }

    /// <summary>
    /// Reset all asks first, with Cancel selected: Cancel keeps everything, and confirming puts back the look, every
    /// part and every colour. Nothing else answers while the confirmation shows.
    /// </summary>
    [Test]
    public void ResetAll_AsksFirst_ThenPutsEverythingBack()
    {
        settings.SetCharacterLook("Bandit");
        settings.SetCharacterPart("Hair", "Long");
        settings.SetCharacterColour("Tunic", "Red");
        controller.Open(null);
        view.ResetAllButton.onClick.Invoke();
        Assert.That(view.IsConfirmShown, Is.True);
        Assert.That(events.currentSelectedGameObject, Is.SameAs(view.ConfirmNoButton.gameObject));
        controller.GetRow(0).ValueButton.onClick.Invoke();
        Assert.That(settings.CharacterLook, Is.EqualTo("Bandit"), "The rows wait for the confirmation.");

        view.ConfirmNoButton.onClick.Invoke();
        Assert.That(view.IsConfirmShown, Is.False);
        Assert.That(settings.CharacterLook, Is.EqualTo("Bandit"));

        view.ResetAllButton.onClick.Invoke();
        view.ConfirmYesButton.onClick.Invoke();
        Assert.That(view.IsConfirmShown, Is.False);
        Assert.That(settings.CharacterLook, Is.Empty);
        Assert.That(settings.CharacterPart("Hair"), Is.Empty);
        Assert.That(settings.CharacterColour("Tunic"), Is.Empty);
        Assert.That(events.currentSelectedGameObject, Is.SameAs(view.ResetAllButton.gameObject));
    }

    /// <summary>
    /// Pressing a chip opens the swatch picker under it, titled with its choice, the swatch shown selected and named.
    /// Moving through the picker names each swatch and shows it on the preview without keeping it; pressing one keeps
    /// it, closes the picker and selects the chip again; the choice's own is kept as no pick.
    /// </summary>
    [Test]
    public void ThePicker_NamesAndPreviewsEachSwatch_AndKeepsThePressedOne()
    {
        controller.Open(null);
        SwatchChipView tunic = controller.GetRow(4).GetChip(0);
        tunic.Button.onClick.Invoke();
        SwatchPickerView picker = view.Picker;
        Assert.That(controller.IsPickerOpen, Is.True);
        Assert.That(picker.IsShown, Is.True);
        Assert.That(picker.Title.text, Is.EqualTo("Tunic"));
        Assert.That(picker.ShownCount, Is.EqualTo(12));
        Assert.That(events.currentSelectedGameObject, Is.SameAs(picker.GetChip(0).gameObject), "The tunic's own blue, first of the cloths.");
        Assert.That(picker.SwatchName.text, Is.EqualTo("Blue"));

        events.SetSelectedGameObject(picker.GetChip(1).gameObject);
        Assert.That(picker.SwatchName.text, Is.EqualTo("Red"));
        Assert.That(preview.PreviewedChoice, Is.EqualTo(colours.IndexOf("Tunic")));
        Assert.That(preview.PreviewedSwatch, Is.EqualTo("Red"));
        Assert.That(settings.CharacterColour("Tunic"), Is.Empty, "Focus only previews.");

        picker.GetChip(1).Button.onClick.Invoke();
        Assert.That(settings.CharacterColour("Tunic"), Is.EqualTo("Red"));
        Assert.That(controller.IsPickerOpen, Is.False);
        Assert.That(picker.IsShown, Is.False);
        Assert.That(events.currentSelectedGameObject, Is.SameAs(tunic.gameObject));

        tunic.Button.onClick.Invoke();
        Assert.That(events.currentSelectedGameObject, Is.SameAs(picker.GetChip(1).gameObject), "Red, now picked, is selected.");
        picker.GetChip(0).Button.onClick.Invoke();
        Assert.That(settings.CharacterColour("Tunic"), Is.Empty, "The tunic's own is kept as no pick.");
    }

    /// <summary>
    /// Back closes the picker before the screen, putting the preview's colours back and selecting the chip again; the
    /// selection leaving the picker closes it too. The picker's arrows wrap around its rows and columns.
    /// </summary>
    [Test]
    public void BackOrLeaving_ClosesThePicker_AndItsArrowsWrap()
    {
        controller.Open(() => closedCount++);
        SwatchChipView hair = controller.GetRow(2).GetChip(0);
        hair.Button.onClick.Invoke();
        SwatchPickerView picker = view.Picker;
        Assert.That(picker.ShownCount, Is.EqualTo(5), "The five hair colours, in one row.");
        Navigation first = picker.GetChip(0).Button.navigation;
        Assert.That(first.selectOnLeft, Is.SameAs(picker.GetChip(4).Button));
        Assert.That(first.selectOnRight, Is.SameAs(picker.GetChip(1).Button));
        Assert.That(first.selectOnUp, Is.SameAs(picker.GetChip(0).Button), "One row: up stays.");

        events.SetSelectedGameObject(picker.GetChip(2).gameObject);
        int shownBefore = preview.PickedColoursShown;
        TickWith(back: true);
        Assert.That(controller.IsPickerOpen, Is.False);
        Assert.That(controller.IsOpen, Is.True, "Back closed only the picker.");
        Assert.That(preview.PickedColoursShown, Is.EqualTo(shownBefore + 1));
        Assert.That(events.currentSelectedGameObject, Is.SameAs(hair.gameObject));
        Assert.That(settings.CharacterColour("Hair"), Is.Empty);

        controller.GetRow(4).GetChip(0).Button.onClick.Invoke();
        Navigation cloth = picker.GetChip(1).Button.navigation;
        Assert.That(cloth.selectOnUp, Is.SameAs(picker.GetChip(7).Button), "Two rows of six: up wraps to the second row.");
        events.SetSelectedGameObject(view.BackButton.gameObject);
        TickWith(back: false);
        Assert.That(controller.IsPickerOpen, Is.False, "The selection left the picker.");
        Assert.That(events.currentSelectedGameObject, Is.SameAs(view.BackButton.gameObject));

        TickWith(back: true);
        Assert.That(controller.IsOpen, Is.False);
        Assert.That(closedCount, Is.EqualTo(1));
    }

    /// <summary>
    /// Opening shows the preview in the given weapon, standing, with the Look row's button selected; the turn buttons
    /// turn it and the pose's button reads each pose in turn; the foot line names a focused chip and turn button and
    /// nothing else.
    /// </summary>
    [Test]
    public void ThePreviewsControls_TurnAndPose_AndTheFootLineNamesTheFocus()
    {
        PlayerWeapon sword = AssetDatabase.LoadAssetAtPath<PlayerWeapon>("Assets/Data/Weapons/Sword.asset");
        controller.Open(null, sword);
        Assert.That(preview.IsShown, Is.True);
        Assert.That(preview.Weapon, Is.EqualTo(sword));
        Assert.That(view.PoseText.text, Is.EqualTo("Standing"));
        Assert.That(events.currentSelectedGameObject, Is.SameAs(controller.GetRow(0).ValueButton.gameObject));
        Assert.That(view.FocusLine.text, Is.Empty);

        view.TurnLeftButton.onClick.Invoke();
        Assert.That(preview.Direction, Is.EqualTo(5), "From south, south-west.");
        view.TurnRightButton.onClick.Invoke();
        view.TurnRightButton.onClick.Invoke();
        Assert.That(preview.Direction, Is.EqualTo(7), "Then south-east.");
        view.PoseButton.onClick.Invoke();
        Assert.That(view.PoseText.text, Is.EqualTo("Running"));
        view.PoseButton.onClick.Invoke();
        Assert.That(view.PoseText.text, Is.EqualTo("Attacking"));
        view.PoseButton.onClick.Invoke();
        Assert.That(view.PoseText.text, Is.EqualTo("Standing"));

        events.SetSelectedGameObject(controller.GetRow(4).GetChip(1).gameObject);
        TickWith(back: false);
        Assert.That(view.FocusLine.text, Is.EqualTo("Shirt: Beige"));
        events.SetSelectedGameObject(view.TurnLeftButton.gameObject);
        TickWith(back: false);
        Assert.That(view.FocusLine.text, Is.EqualTo(CharacterScreenController.TurnLeftName));
        events.SetSelectedGameObject(view.BackButton.gameObject);
        TickWith(back: false);
        Assert.That(view.FocusLine.text, Is.Empty);

        controller.Close();
        Assert.That(preview.IsShown, Is.False);
        Assert.That(menus.Count, Is.Zero);
    }

    /// <summary>
    /// The rows' controls go Left and Right along a row and Left from its first to the pose's button, Up and Down to
    /// the same kind of control in the next row shown (its nearest chip, else its button), and Down from the last to
    /// Reset all; Right from Turn right goes to the first row.
    /// </summary>
    [Test]
    public void TheNavigation_FollowsTheRowsShown()
    {
        controller.Open(null);
        RowChipsView body = controller.GetRow(1);
        RowChipsView hair = controller.GetRow(2);
        RowChipsView headwear = controller.GetRow(3);
        RowChipsView bag = controller.GetRow(9);
        Assert.That(hair.ValueButton.navigation.selectOnLeft, Is.SameAs(view.PoseButton));
        Assert.That(hair.ValueButton.navigation.selectOnRight, Is.SameAs(hair.GetChip(0).Button));
        Assert.That(hair.ValueButton.navigation.selectOnUp, Is.SameAs(body.GetChip(0).Button), "The Body row has no button: its first chip.");
        Assert.That(body.GetChip(3).Button.navigation.selectOnDown, Is.SameAs(hair.GetChip(0).Button), "The hair's only chip.");
        Assert.That(hair.GetChip(0).Button.navigation.selectOnDown, Is.SameAs(headwear.ValueButton), "No headwear worn: no chip.");
        Assert.That(hair.ResetButton.navigation.selectOnDown, Is.SameAs(headwear.ResetButton));
        Assert.That(bag.ValueButton.navigation.selectOnDown, Is.SameAs(view.ResetAllButton));
        Assert.That(view.TurnRightButton.navigation.selectOnRight, Is.SameAs(controller.GetRow(0).ValueButton));

        settings.SetCharacterPart("Headwear", "Hood");
        Assert.That(hair.GetChip(0).Button.navigation.selectOnDown, Is.SameAs(headwear.GetChip(0).Button), "The hood's colour.");
    }

    /// <summary>
    /// A mask with a bit for each of <paramref name="dyes"/>.
    /// </summary>
    private static int Mask(params CharacterDye[] dyes)
    {
        int mask = 0;
        foreach (CharacterDye dye in dyes)
        {
            mask |= 1 << (int)dye;
        }

        return mask;
    }

    /// <summary>
    /// The text on each row's button, null where it shows none.
    /// </summary>
    private string[] Values()
    {
        string[] values = new string[controller.RowCount];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = controller.GetRow(i).IsValueShown ? controller.GetRow(i).ValueText.text : null;
        }

        return values;
    }

    /// <summary>
    /// How many chips each row shows.
    /// </summary>
    private int[] ChipCounts()
    {
        int[] counts = new int[controller.RowCount];
        for (int i = 0; i < counts.Length; i++)
        {
            for (int chip = 0; chip < controller.GetRow(i).ChipCount; chip++)
            {
                counts[i] += controller.GetRow(i).GetChip(chip).gameObject.activeSelf ? 1 : 0;
            }
        }

        return counts;
    }

    /// <summary>
    /// The names of the colour choices row <paramref name="row"/>'s chips show.
    /// </summary>
    private string[] Names(int row)
    {
        return controller.ShownChoices(row).Select(choice => colours.Get(choice).Name).ToArray();
    }

    /// <summary>
    /// Whether each row shows.
    /// </summary>
    private bool[] Shown()
    {
        bool[] shown = new bool[controller.RowCount];
        for (int i = 0; i < shown.Length; i++)
        {
            shown[i] = controller.GetRow(i).gameObject.activeSelf;
        }

        return shown;
    }

    /// <summary>
    /// Runs one frame of the menu stack and the controller, with Back pressed when <paramref name="back"/>.
    /// </summary>
    private void TickWith(bool back)
    {
        menuInput.BackPressedThisFrame = back;
        menus.Tick();
        controller.Tick();
        menuInput.BackPressedThisFrame = false;
    }
}

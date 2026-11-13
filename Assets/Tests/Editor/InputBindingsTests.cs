using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

/// <summary>
/// Tests for <see cref="InputBindings"/> on a copy of the project input actions and the project's rebindable
/// action list: defaults, assigning and swapping keys, refused inputs, labels, resets, and loading saved overrides.
/// </summary>
public class InputBindingsTests
{
    private const string ProjectActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string RebindableActionsPath = "Assets/Data/Input/RebindableActions.asset";

    private static readonly string[] RebindableNames = { "MoveClick", "AttackMove", "Stop", "Ability1", "Ability2", "Ability3", "Ability4", "Ability5", "Ability6", "Chat" };

    private static readonly string[] DefaultPaths =
    {
        "<Mouse>/rightButton", "<Keyboard>/a", "<Keyboard>/s", "<Keyboard>/q", "<Keyboard>/w", "<Keyboard>/e", "<Keyboard>/r", "<Keyboard>/1", "<Keyboard>/2", "<Keyboard>/enter",
    };

    private InputActionAsset actions;
    private RebindableActionList list;
    private MemorySaveStore store;
    private InputBindings bindings;
    private int changes;

    /// <summary>
    /// Copies the project input actions, loads the rebindable action list, and starts with an empty settings store
    /// and no recorded changes.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        InputActionAsset project = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath);
        actions = InputActionAsset.FromJson(project.ToJson());
        list = AssetDatabase.LoadAssetAtPath<RebindableActionList>(RebindableActionsPath);
        store = new MemorySaveStore();
        changes = 0;
    }

    /// <summary>
    /// Disposes the bindings, if created, and destroys the copied input actions.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        bindings?.Dispose();
        bindings = null;
        Object.DestroyImmediate(actions);
    }

    /// <summary>
    /// The project's rebindable list holds move, attack move, stop, the six abilities and the chat key (Docs/UiRevamp.md,
    /// Phase 12) in menu order, each with a label, a section and a description (Phase 8), and the bindings expose one
    /// entry per action with its description.
    /// </summary>
    [Test]
    public void ProjectList_HasTheGameplayActionsInMenuOrder_WithLabelsSectionsAndDescriptions()
    {
        Assert.That(list, Is.Not.Null);
        Assert.That(Enumerable.Range(0, list.Count).Select(i => list[i].Action.action.name), Is.EqualTo(RebindableNames));
        for (int i = 0; i < list.Count; i++)
        {
            Assert.That(list[i].Label, Is.Not.Empty);
            Assert.That(list[i].Section, Is.Not.Empty);
            Assert.That(list[i].Description, Is.Not.Empty, $"{list[i].Label} has a description.");
        }

        Create();
        Assert.That(bindings.Count, Is.EqualTo(RebindableNames.Length));
        Assert.That(bindings.GetDescription(Action("Stop")), Is.EqualTo(list[2].Description));
        Assert.That(bindings.GetDescription(Action("Point")), Is.Empty, "Not rebindable.");
    }

    /// <summary>
    /// An action is on its default until it is given another input, and again once it is reset; an action that is not
    /// rebindable always reads as on its default.
    /// </summary>
    [Test]
    public void IsDefault_FollowsAssignmentsAndResets()
    {
        Create();
        Assert.That(bindings.IsDefault(Action("Stop")), Is.True);

        bindings.TryAssign(Action("Stop"), "<Keyboard>/f", out _);
        Assert.That(bindings.IsDefault(Action("Stop")), Is.False);
        Assert.That(bindings.IsDefault(Action("AttackMove")), Is.True);

        bindings.Reset(Action("Stop"), out _);
        Assert.That(bindings.IsDefault(Action("Stop")), Is.True);
        Assert.That(bindings.IsDefault(Action("Point")), Is.True);
    }

    /// <summary>
    /// The default bindings are the controls from the spec, no two share an input, and Point and Menu are not
    /// rebindable.
    /// </summary>
    [Test]
    public void Defaults_AreTheSpecsControls_WithNoDuplicates()
    {
        Create();

        string[] paths = RebindableNames.Select(name => bindings.GetPath(Action(name))).ToArray();

        Assert.That(paths, Is.EqualTo(DefaultPaths));
        Assert.That(paths.Distinct().Count(), Is.EqualTo(paths.Length));
        Assert.That(bindings.IsRebindable(Action("Point")), Is.False);
        Assert.That(bindings.IsRebindable(Action("Menu")), Is.False);
    }

    /// <summary>
    /// Assigning a free key applies it to the action, writes the settings once and raises one change
    /// notification, with nothing swapped.
    /// </summary>
    [Test]
    public void TryAssign_AppliesTheKey_SavesAndNotifiesOnce()
    {
        Create();

        Assert.That(bindings.TryAssign(Action("Stop"), "<Keyboard>/f", out InputAction swapped), Is.True);

        Assert.That(swapped, Is.Null);
        Assert.That(bindings.GetPath(Action("Stop")), Is.EqualTo("<Keyboard>/f"));
        Assert.That(Action("Stop").bindings[0].effectivePath, Is.EqualTo("<Keyboard>/f"));
        Assert.That(changes, Is.EqualTo(1));
        Assert.That(store.WriteCount, Is.EqualTo(1));
    }

    /// <summary>
    /// Assigning a key another action uses gives that action the old key, reports it as swapped and notifies once.
    /// </summary>
    [Test]
    public void TryAssign_AKeyAnotherActionUses_SwapsTheTwo()
    {
        Create();

        Assert.That(bindings.TryAssign(Action("Stop"), "<Keyboard>/q", out InputAction swapped), Is.True);

        Assert.That(swapped, Is.SameAs(Action("Ability1")));
        Assert.That(bindings.GetPath(Action("Stop")), Is.EqualTo("<Keyboard>/q"));
        Assert.That(bindings.GetPath(Action("Ability1")), Is.EqualTo("<Keyboard>/s"));
        Assert.That(changes, Is.EqualTo(1));
    }

    /// <summary>
    /// Swapping two actions back to their defaults removes both overrides rather than storing overrides equal to
    /// the defaults.
    /// </summary>
    [Test]
    public void TryAssign_TheDefault_RemovesTheOverride()
    {
        Create();
        bindings.TryAssign(Action("Stop"), "<Keyboard>/q", out _);

        bindings.TryAssign(Action("Stop"), "<Keyboard>/s", out InputAction swapped);

        Assert.That(swapped, Is.SameAs(Action("Ability1")));
        Assert.That(Action("Stop").bindings[0].overridePath, Is.Null);
        Assert.That(Action("Ability1").bindings[0].overridePath, Is.Null);
        Assert.That(actions.SaveBindingOverridesAsJson(), Is.Empty);
    }

    /// <summary>
    /// Assigning an action the key it already has succeeds without notifying or writing the settings.
    /// </summary>
    [Test]
    public void TryAssign_TheCurrentKey_ChangesNothing()
    {
        Create();

        Assert.That(bindings.TryAssign(Action("Stop"), "<Keyboard>/s", out _), Is.True);

        Assert.That(changes, Is.Zero);
        Assert.That(store.WriteCount, Is.Zero);
    }

    /// <summary>
    /// Escape, any-key, a bare device path, empty or null paths, pointer axes and gamepad controls are refused,
    /// leaving the binding, notifications and settings untouched.
    /// </summary>
    [TestCase("<Keyboard>/escape")]
    [TestCase("<Keyboard>/anyKey")]
    [TestCase("<Keyboard>/")]
    [TestCase("<Mouse>/position")]
    [TestCase("<Mouse>/scroll")]
    [TestCase("<Pointer>/delta")]
    [TestCase("<Gamepad>/buttonSouth")]
    [TestCase("")]
    [TestCase(null)]
    public void TryAssign_RefusesInputsThatCannotBeBound(string path)
    {
        Create();

        Assert.That(bindings.TryAssign(Action("Ability1"), path, out _), Is.False);

        Assert.That(bindings.GetPath(Action("Ability1")), Is.EqualTo("<Keyboard>/q"));
        Assert.That(changes, Is.Zero);
        Assert.That(store.WriteCount, Is.Zero);
    }

    /// <summary>
    /// Assigning to an action outside the rebindable list, or to no action, is refused without writing.
    /// </summary>
    [Test]
    public void TryAssign_RefusesActionsThatAreNotRebindable()
    {
        Create();

        Assert.That(bindings.TryAssign(Action("Point"), "<Keyboard>/f", out _), Is.False);
        Assert.That(bindings.TryAssign(Action("Menu"), "<Keyboard>/f", out _), Is.False);
        Assert.That(bindings.TryAssign(null, "<Keyboard>/f", out _), Is.False);
        Assert.That(store.WriteCount, Is.Zero);
    }

    /// <summary>
    /// Each mouse button can be bound to an ability and gets its own short label (also used on the ability bar)
    /// and long label.
    /// </summary>
    [TestCase("<Mouse>/leftButton", "LMB", "Left mouse button")]
    [TestCase("<Mouse>/rightButton", "RMB", "Right mouse button")]
    [TestCase("<Mouse>/middleButton", "MMB", "Middle mouse button")]
    [TestCase("<Mouse>/backButton", "M4", "Mouse button 4")]
    [TestCase("<Mouse>/forwardButton", "M5", "Mouse button 5")]
    public void MouseButtons_CanBeBound_AndHaveTheirOwnLabels(string path, string shortLabel, string label)
    {
        Create();

        Assert.That(bindings.TryAssign(Action("Ability3"), path, out _), Is.True);

        Assert.That(bindings.GetShortLabel(Action("Ability3")), Is.EqualTo(shortLabel));
        Assert.That(bindings.GetAbilityKeyLabel(2), Is.EqualTo(shortLabel));
        Assert.That(bindings.GetLabel(Action("Ability3")), Is.EqualTo(label));
    }

    /// <summary>
    /// Short labels name the bound key or mouse button, and ability slots outside the bar's range have an empty
    /// label.
    /// </summary>
    [Test]
    public void KeyLabels_ComeFromTheKeyboard_AndSlotsOutsideTheBarHaveNone()
    {
        Create();

        Assert.That(bindings.GetShortLabel(Action("Stop")), Is.EqualTo("S"));
        Assert.That(bindings.GetShortLabel(Action("MoveClick")), Is.EqualTo("RMB"));
        Assert.That(bindings.GetAbilityKeyLabel(4), Is.EqualTo("1"));
        Assert.That(bindings.GetAbilityKeyLabel(-1), Is.Empty);
        Assert.That(bindings.GetAbilityKeyLabel(AbilitySlots.Count), Is.Empty);
    }

    /// <summary>
    /// Resetting one action restores its default, swapping with the action that held that key, and leaves no
    /// overrides.
    /// </summary>
    [Test]
    public void Reset_PutsTheDefaultBack_AndSwapsWithWhoeverHadIt()
    {
        Create();
        bindings.TryAssign(Action("Ability1"), "<Keyboard>/w", out _);

        Assert.That(bindings.Reset(Action("Ability1"), out InputAction swapped), Is.True);

        Assert.That(swapped, Is.SameAs(Action("Ability2")));
        Assert.That(bindings.GetPath(Action("Ability1")), Is.EqualTo("<Keyboard>/q"));
        Assert.That(bindings.GetPath(Action("Ability2")), Is.EqualTo("<Keyboard>/w"));
        Assert.That(actions.SaveBindingOverridesAsJson(), Is.Empty);
    }

    /// <summary>
    /// Resetting everything restores every default, notifies once and saves an empty override set.
    /// </summary>
    [Test]
    public void ResetAll_RestoresEveryDefault_SavesAndNotifies()
    {
        Create();
        bindings.TryAssign(Action("Stop"), "<Keyboard>/f", out _);
        bindings.TryAssign(Action("Ability6"), "<Mouse>/middleButton", out _);
        changes = 0;

        bindings.ResetAll();

        Assert.That(RebindableNames.Select(name => bindings.GetPath(Action(name))), Is.EqualTo(DefaultPaths));
        Assert.That(changes, Is.EqualTo(1));
        Assert.That(new UserSettings(store).InputBindingOverrides, Is.Empty);
    }

    /// <summary>
    /// Bindings saved in one session are applied by the next one on the same store, without rewriting the file.
    /// </summary>
    [Test]
    public void SavedBindings_AreAppliedByTheNextSession()
    {
        Create();
        bindings.TryAssign(Action("Stop"), "<Keyboard>/q", out _);
        bindings.Dispose();
        bindings = null;

        Create();

        Assert.That(bindings.GetPath(Action("Stop")), Is.EqualTo("<Keyboard>/q"));
        Assert.That(bindings.GetPath(Action("Ability1")), Is.EqualTo("<Keyboard>/s"));
        Assert.That(store.WriteCount, Is.EqualTo(1), "Loading valid bindings does not rewrite the file.");
    }

    /// <summary>
    /// A saved override that would duplicate another action's key is dropped with a warning, both actions keep
    /// their defaults, and the settings are rewritten without it.
    /// </summary>
    [Test]
    public void SavedDuplicate_IsDroppedWithAWarning_AndTheFileRewritten()
    {
        SaveOverrides(("Stop", "<Keyboard>/q"));
        LogAssert.Expect(LogType.Warning, new Regex("could not be used"));

        Create();

        Assert.That(bindings.GetPath(Action("Stop")), Is.EqualTo("<Keyboard>/s"));
        Assert.That(bindings.GetPath(Action("Ability1")), Is.EqualTo("<Keyboard>/q"));
        Assert.That(new UserSettings(store).InputBindingOverrides, Is.Empty);
    }

    /// <summary>
    /// Saved overrides on non-rebindable actions or to forbidden inputs are dropped with a warning, while valid
    /// ones alongside them are kept.
    /// </summary>
    [Test]
    public void SavedOverridesOnFixedActionsOrForbiddenInputs_AreDroppedWithAWarning()
    {
        SaveOverrides(("Point", "<Mouse>/scroll"), ("Ability2", "<Keyboard>/escape"), ("Ability3", "<Keyboard>/g"));
        LogAssert.Expect(LogType.Warning, new Regex("could not be used"));

        Create();

        Assert.That(Action("Point").bindings[0].overridePath, Is.Null);
        Assert.That(bindings.GetPath(Action("Ability2")), Is.EqualTo("<Keyboard>/w"));
        Assert.That(bindings.GetPath(Action("Ability3")), Is.EqualTo("<Keyboard>/g"), "Valid overrides are kept.");
    }

    /// <summary>
    /// Saved bindings that are not valid JSON are ignored with a warning, the defaults apply, and the saved
    /// overrides are cleared.
    /// </summary>
    [Test]
    public void MalformedSavedBindings_AreIgnoredWithAWarning()
    {
        new UserSettings(store).SetInputBindingOverrides("not json");
        LogAssert.Expect(LogType.Warning, new Regex("saved key bindings could not be read"));

        Create();

        Assert.That(RebindableNames.Select(name => bindings.GetPath(Action(name))), Is.EqualTo(DefaultPaths));
        Assert.That(new UserSettings(store).InputBindingOverrides, Is.Empty);
    }

    /// <summary>
    /// Two rebindable actions whose default inputs collide produce exactly one error on initialisation.
    /// </summary>
    [Test]
    public void TwoActionsSharingADefault_AreReportedOnce()
    {
        Action("Stop").ChangeBinding(0).WithPath("<Keyboard>/q");
        LogAssert.Expect(LogType.Error, new Regex("share the default input"));

        Create();

        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>
    /// Without a rebindable action list the bindings log an error, expose no actions and refuse every assignment.
    /// </summary>
    [Test]
    public void MissingList_LogsAnError_AndNothingIsRebindable()
    {
        LogAssert.Expect(LogType.Error, new Regex("needs an input asset and a RebindableActionList"));

        bindings = new InputBindings(actions, null, new UserSettings(store));
        bindings.Initialize();

        Assert.That(bindings.Count, Is.Zero);
        Assert.That(bindings.TryAssign(Action("Stop"), "<Keyboard>/f", out _), Is.False);
    }

    /// <summary>
    /// Creates and initialises the bindings on the copied actions and the current store, counting each
    /// <see cref="InputBindings.BindingsChanged"/> in <c>changes</c>.
    /// </summary>
    private void Create()
    {
        bindings = new InputBindings(actions, list, new UserSettings(store));
        bindings.BindingsChanged += () => changes++;
        bindings.Initialize();
    }

    /// <summary>
    /// Finds the action named <paramref name="name"/> in the copied asset, throwing when it does not exist.
    /// </summary>
    private InputAction Action(string name)
    {
        return actions.FindAction(name, true);
    }

    /// <summary>
    /// Writes <paramref name="overrides"/> into the settings as a previous session would have saved them, leaves
    /// the actions without overrides, and swaps in a fresh store holding those contents so write counts start at 0.
    /// </summary>
    private void SaveOverrides(params (string action, string path)[] overrides)
    {
        foreach ((string action, string path) in overrides)
        {
            Action(action).ApplyBindingOverride(0, path);
        }

        string json = actions.SaveBindingOverridesAsJson();
        actions.RemoveAllBindingOverrides();
        new UserSettings(store).SetInputBindingOverrides(json);
        store = new MemorySaveStore { Contents = store.Contents };
    }
}

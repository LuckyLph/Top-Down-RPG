using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

public class InputBindingsTests
{
    private const string ProjectActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string RebindableActionsPath = "Assets/Data/Input/RebindableActions.asset";

    private static readonly string[] RebindableNames = { "MoveClick", "Stop", "Ability1", "Ability2", "Ability3", "Ability4", "Ability5", "Ability6" };

    private static readonly string[] DefaultPaths =
    {
        "<Mouse>/rightButton", "<Keyboard>/x", "<Keyboard>/q", "<Keyboard>/w", "<Keyboard>/e", "<Keyboard>/r", "<Keyboard>/a", "<Keyboard>/s",
    };

    private InputActionAsset actions;
    private RebindableActionList list;
    private MemorySaveStore store;
    private InputBindings bindings;
    private int changes;

    [SetUp]
    public void SetUp()
    {
        InputActionAsset project = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath);
        actions = InputActionAsset.FromJson(project.ToJson());
        list = AssetDatabase.LoadAssetAtPath<RebindableActionList>(RebindableActionsPath);
        store = new MemorySaveStore();
        changes = 0;
    }

    [TearDown]
    public void TearDown()
    {
        bindings?.Dispose();
        bindings = null;
        Object.DestroyImmediate(actions);
    }

    [Test]
    public void ProjectList_HasTheGameplayActionsInMenuOrder_WithLabelsAndSections()
    {
        Assert.That(list, Is.Not.Null);
        Assert.That(Enumerable.Range(0, list.Count).Select(i => list[i].Action.action.name), Is.EqualTo(RebindableNames));
        for (int i = 0; i < list.Count; i++)
        {
            Assert.That(list[i].Label, Is.Not.Empty);
            Assert.That(list[i].Section, Is.Not.Empty);
        }

        Create();
        Assert.That(bindings.Count, Is.EqualTo(RebindableNames.Length));
    }

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

    [Test]
    public void TryAssign_AKeyAnotherActionUses_SwapsTheTwo()
    {
        Create();

        Assert.That(bindings.TryAssign(Action("Stop"), "<Keyboard>/q", out InputAction swapped), Is.True);

        Assert.That(swapped, Is.SameAs(Action("Ability1")));
        Assert.That(bindings.GetPath(Action("Stop")), Is.EqualTo("<Keyboard>/q"));
        Assert.That(bindings.GetPath(Action("Ability1")), Is.EqualTo("<Keyboard>/x"));
        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void TryAssign_TheDefault_RemovesTheOverride()
    {
        Create();
        bindings.TryAssign(Action("Stop"), "<Keyboard>/q", out _);

        bindings.TryAssign(Action("Stop"), "<Keyboard>/x", out InputAction swapped);

        Assert.That(swapped, Is.SameAs(Action("Ability1")));
        Assert.That(Action("Stop").bindings[0].overridePath, Is.Null);
        Assert.That(Action("Ability1").bindings[0].overridePath, Is.Null);
        Assert.That(actions.SaveBindingOverridesAsJson(), Is.Empty);
    }

    [Test]
    public void TryAssign_TheCurrentKey_ChangesNothing()
    {
        Create();

        Assert.That(bindings.TryAssign(Action("Stop"), "<Keyboard>/x", out _), Is.True);

        Assert.That(changes, Is.Zero);
        Assert.That(store.WriteCount, Is.Zero);
    }

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

    [Test]
    public void TryAssign_RefusesActionsThatAreNotRebindable()
    {
        Create();

        Assert.That(bindings.TryAssign(Action("Point"), "<Keyboard>/f", out _), Is.False);
        Assert.That(bindings.TryAssign(Action("Menu"), "<Keyboard>/f", out _), Is.False);
        Assert.That(bindings.TryAssign(null, "<Keyboard>/f", out _), Is.False);
        Assert.That(store.WriteCount, Is.Zero);
    }

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

    [Test]
    public void KeyLabels_ComeFromTheKeyboard_AndSlotsOutsideTheBarHaveNone()
    {
        Create();

        Assert.That(bindings.GetShortLabel(Action("Stop")), Is.EqualTo("X"));
        Assert.That(bindings.GetShortLabel(Action("MoveClick")), Is.EqualTo("RMB"));
        Assert.That(bindings.GetAbilityKeyLabel(4), Is.EqualTo("A"));
        Assert.That(bindings.GetAbilityKeyLabel(-1), Is.Empty);
        Assert.That(bindings.GetAbilityKeyLabel(AbilitySlots.Count), Is.Empty);
    }

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

    [Test]
    public void SavedBindings_AreAppliedByTheNextSession()
    {
        Create();
        bindings.TryAssign(Action("Stop"), "<Keyboard>/q", out _);
        bindings.Dispose();
        bindings = null;

        Create();

        Assert.That(bindings.GetPath(Action("Stop")), Is.EqualTo("<Keyboard>/q"));
        Assert.That(bindings.GetPath(Action("Ability1")), Is.EqualTo("<Keyboard>/x"));
        Assert.That(store.WriteCount, Is.EqualTo(1), "Loading valid bindings does not rewrite the file.");
    }

    [Test]
    public void Dispose_RemovesEveryOverrideFromTheAsset()
    {
        Create();
        bindings.TryAssign(Action("Stop"), "<Keyboard>/f", out _);

        bindings.Dispose();
        bindings = null;

        Assert.That(Action("Stop").bindings[0].overridePath, Is.Null);
    }

    [Test]
    public void SavedDuplicate_IsDroppedWithAWarning_AndTheFileRewritten()
    {
        SaveOverrides(("Stop", "<Keyboard>/q"));
        LogAssert.Expect(LogType.Warning, new Regex("could not be used"));

        Create();

        Assert.That(bindings.GetPath(Action("Stop")), Is.EqualTo("<Keyboard>/x"));
        Assert.That(bindings.GetPath(Action("Ability1")), Is.EqualTo("<Keyboard>/q"));
        Assert.That(new UserSettings(store).InputBindingOverrides, Is.Empty);
    }

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

    [Test]
    public void MalformedSavedBindings_AreIgnoredWithAWarning()
    {
        new UserSettings(store).SetInputBindingOverrides("not json");
        LogAssert.Expect(LogType.Warning, new Regex("saved key bindings could not be read"));

        Create();

        Assert.That(RebindableNames.Select(name => bindings.GetPath(Action(name))), Is.EqualTo(DefaultPaths));
        Assert.That(new UserSettings(store).InputBindingOverrides, Is.Empty);
    }

    [Test]
    public void TwoActionsSharingADefault_AreReportedOnce()
    {
        Action("Stop").ChangeBinding(0).WithPath("<Keyboard>/q");
        LogAssert.Expect(LogType.Error, new Regex("share the default input"));

        Create();

        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void MissingList_LogsAnError_AndNothingIsRebindable()
    {
        LogAssert.Expect(LogType.Error, new Regex("needs an input asset and a RebindableActionList"));

        bindings = new InputBindings(actions, null, new UserSettings(store));
        bindings.Initialize();

        Assert.That(bindings.Count, Is.Zero);
        Assert.That(bindings.TryAssign(Action("Stop"), "<Keyboard>/f", out _), Is.False);
    }

    private void Create()
    {
        bindings = new InputBindings(actions, list, new UserSettings(store));
        bindings.BindingsChanged += () => changes++;
        bindings.Initialize();
    }

    private InputAction Action(string name)
    {
        return actions.FindAction(name, true);
    }

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

using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Tests for <see cref="RuntimeInputActions"/>: the session copy of the project input asset, its hand-off to the
/// UI input module, and rebinding without touching the project asset.
/// </summary>
public class RuntimeInputActionsTests
{
    private const string ProjectActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string RebindableActionsPath = "Assets/Data/Input/RebindableActions.asset";

    private InputActionAsset project;
    private GameObject eventSystemObject;
    private InputSystemUIInputModule module;
    private RuntimeInputActions runtime;

    /// <summary>
    /// Loads the project input asset, builds an event system whose UI input module uses it, and creates the
    /// runtime copy against that event system.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        project = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath);
        eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
        module = eventSystemObject.AddComponent<InputSystemUIInputModule>();
        module.actionsAsset = project;
        runtime = new RuntimeInputActions(project, eventSystemObject.GetComponent<EventSystem>());
    }

    /// <summary>
    /// Disposes the runtime copy and destroys the event system.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        runtime.Dispose();
        Object.DestroyImmediate(eventSystemObject);
    }

    /// <summary>
    /// The copy is a separate asset whose actions keep the project asset's ids, and the UI input module and its
    /// actions now point at the copy.
    /// </summary>
    [Test]
    public void TheCopy_KeepsTheProjectsActionsAndIds_AndTheUIModuleUsesIt()
    {
        Assert.That(runtime.Actions, Is.Not.Null);
        Assert.That(runtime.Actions, Is.Not.SameAs(project));
        Assert.That(runtime.Actions.FindAction(project.FindAction("Player/Stop", true).id), Is.Not.Null, "Action ids survive the copy.");
        Assert.That(module.actionsAsset, Is.SameAs(runtime.Actions));
        Assert.That(module.cancel.action.actionMap.asset, Is.SameAs(runtime.Actions));
    }

    /// <summary>
    /// Rebinding an action through <see cref="InputBindings"/> on the copy changes the copy's binding and leaves
    /// the project asset's default binding in place.
    /// </summary>
    [Test]
    public void RebindingTheCopy_LeavesTheProjectAssetUntouched()
    {
        InputBindings bindings = new(runtime.Actions, AssetDatabase.LoadAssetAtPath<RebindableActionList>(RebindableActionsPath), new UserSettings(new MemorySaveStore()));
        bindings.Initialize();

        bindings.TryAssign(runtime.Actions.FindAction("Player/Stop", true), "<Keyboard>/f", out _);

        Assert.That(runtime.Actions.FindAction("Player/Stop", true).bindings[0].effectivePath, Is.EqualTo("<Keyboard>/f"));
        Assert.That(project.FindAction("Player/Stop", true).bindings[0].effectivePath, Is.EqualTo("<Keyboard>/x"));
        bindings.Dispose();
    }

    /// <summary>
    /// Disposing destroys the copy at once in EditMode.
    /// </summary>
    [Test]
    public void Dispose_DestroysTheCopy()
    {
        InputActionAsset copy = runtime.Actions;

        runtime.Dispose();

        Assert.That(copy == null, Is.True);
    }

    /// <summary>
    /// Without a project asset or event system there is no copy, and disposing is safe.
    /// </summary>
    [Test]
    public void WithoutAProjectAsset_ThereIsNoCopy()
    {
        RuntimeInputActions empty = new(null, null);

        Assert.That(empty.Actions, Is.Null);
        empty.Dispose();
    }
}

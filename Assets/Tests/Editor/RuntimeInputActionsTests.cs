using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

public class RuntimeInputActionsTests
{
    private const string ProjectActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string RebindableActionsPath = "Assets/Data/Input/RebindableActions.asset";

    private InputActionAsset project;
    private GameObject eventSystemObject;
    private InputSystemUIInputModule module;
    private RuntimeInputActions runtime;

    [SetUp]
    public void SetUp()
    {
        project = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath);
        eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
        module = eventSystemObject.AddComponent<InputSystemUIInputModule>();
        module.actionsAsset = project;
        runtime = new RuntimeInputActions(project, eventSystemObject.GetComponent<EventSystem>());
    }

    [TearDown]
    public void TearDown()
    {
        runtime.Dispose();
        Object.DestroyImmediate(eventSystemObject);
    }

    [Test]
    public void TheCopy_KeepsTheProjectsActionsAndIds_AndTheUIModuleUsesIt()
    {
        Assert.That(runtime.Actions, Is.Not.Null);
        Assert.That(runtime.Actions, Is.Not.SameAs(project));
        Assert.That(runtime.Actions.FindAction(project.FindAction("Player/Stop", true).id), Is.Not.Null, "Action ids survive the copy.");
        Assert.That(module.actionsAsset, Is.SameAs(runtime.Actions));
        Assert.That(module.cancel.action.actionMap.asset, Is.SameAs(runtime.Actions));
    }

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

    [Test]
    public void Dispose_DestroysTheCopy()
    {
        InputActionAsset copy = runtime.Actions;

        runtime.Dispose();

        Assert.That(copy == null, Is.True);
    }

    [Test]
    public void WithoutAProjectAsset_ThereIsNoCopy()
    {
        RuntimeInputActions empty = new(null, null);

        Assert.That(empty.Actions, Is.Null);
        empty.Dispose();
    }
}

using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

public class InputRebindingTests : InputTestFixture
{
    private const string ProjectActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string RebindableActionsPath = "Assets/Data/Input/RebindableActions.asset";

    private InputActionAsset actions;
    private InputBindings bindings;
    private Keyboard keyboard;
    private Mouse mouse;
    private int finishedCount;
    private RebindOutcome outcome;
    private InputAction swapped;

    public override void Setup()
    {
        base.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        actions = InputActionAsset.FromJson(AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath).ToJson());
        bindings = new InputBindings(actions, AssetDatabase.LoadAssetAtPath<RebindableActionList>(RebindableActionsPath), new UserSettings(new MemorySaveStore()));
        bindings.Initialize();
        finishedCount = 0;
        swapped = null;
    }

    public override void TearDown()
    {
        bindings.Dispose();
        Object.DestroyImmediate(actions);
        base.TearDown();
    }

    [Test]
    public void AKeyPress_BindsTheKey()
    {
        Assert.That(bindings.StartRebind(Action("Stop"), Finished), Is.True);
        Assert.That(bindings.IsRebinding, Is.True);

        PressAndSettle(keyboard.fKey);

        Assert.That(finishedCount, Is.EqualTo(1));
        Assert.That(outcome, Is.EqualTo(RebindOutcome.Assigned));
        Assert.That(swapped, Is.Null);
        Assert.That(bindings.GetPath(Action("Stop")), Is.EqualTo("<Keyboard>/f"));
        Assert.That(bindings.IsRebinding, Is.False);
    }

    [Test]
    public void AKeyAnotherActionUses_IsSwapped()
    {
        bindings.StartRebind(Action("Stop"), Finished);

        PressAndSettle(keyboard.qKey);

        Assert.That(outcome, Is.EqualTo(RebindOutcome.Assigned));
        Assert.That(swapped, Is.SameAs(Action("Ability1")));
        Assert.That(bindings.GetPath(Action("Ability1")), Is.EqualTo("<Keyboard>/x"));
    }

    [Test]
    public void Escape_Cancels_WithoutAChange()
    {
        bindings.StartRebind(Action("Stop"), Finished);

        PressAndSettle(keyboard.escapeKey);

        Assert.That(finishedCount, Is.EqualTo(1));
        Assert.That(outcome, Is.EqualTo(RebindOutcome.Cancelled));
        Assert.That(bindings.GetPath(Action("Stop")), Is.EqualTo("<Keyboard>/x"));
        Assert.That(bindings.IsRebinding, Is.False);
    }

    [Test]
    public void AMouseButton_BindsTheButton()
    {
        bindings.StartRebind(Action("Ability6"), Finished);

        PressAndSettle(mouse.middleButton);

        Assert.That(outcome, Is.EqualTo(RebindOutcome.Assigned));
        Assert.That(bindings.GetPath(Action("Ability6")), Is.EqualTo("<Mouse>/middleButton"));
    }

    [Test]
    public void PointerMovementAndScrolling_DoNotBind()
    {
        bindings.StartRebind(Action("Ability6"), Finished);

        Set(mouse.position, new Vector2(120f, 80f));
        Set(mouse.scroll, new Vector2(0f, 120f));
        currentTime += 0.2;
        InputSystem.Update();

        Assert.That(finishedCount, Is.Zero);
        Assert.That(bindings.IsRebinding, Is.True);
        Assert.That(bindings.GetPath(Action("Ability6")), Is.EqualTo("<Keyboard>/s"));
    }

    [Test]
    public void ARebind_WhileAnotherListens_IsRefused()
    {
        bindings.StartRebind(Action("Stop"), Finished);

        Assert.That(bindings.StartRebind(Action("Ability1"), Finished), Is.False);
        Assert.That(bindings.StartRebind(Action("Point"), Finished), Is.False);
    }

    [Test]
    public void AnEnabledAction_LogsAnError_AndDoesNotListen()
    {
        Action("Stop").Enable();
        LogAssert.Expect(LogType.Error, new Regex("while it is enabled"));

        Assert.That(bindings.StartRebind(Action("Stop"), Finished), Is.False);
        Assert.That(bindings.IsRebinding, Is.False);
    }

    [Test]
    public void CancelAndDispose_EndARunningRebindAsCancelled()
    {
        bindings.StartRebind(Action("Stop"), Finished);

        bindings.CancelRebind();

        Assert.That(finishedCount, Is.EqualTo(1));
        Assert.That(outcome, Is.EqualTo(RebindOutcome.Cancelled));

        bindings.StartRebind(Action("Stop"), Finished);
        bindings.Dispose();

        Assert.That(finishedCount, Is.EqualTo(2));
        Assert.That(bindings.IsRebinding, Is.False);
    }

    private void PressAndSettle(UnityEngine.InputSystem.Controls.ButtonControl button)
    {
        Press(button);
        currentTime += 0.2;
        InputSystem.Update();
        Release(button);
    }

    private void Finished(RebindOutcome result, InputAction swappedWith)
    {
        finishedCount++;
        outcome = result;
        swapped = swappedWith;
    }

    private InputAction Action(string name)
    {
        return actions.FindAction(name, true);
    }
}

using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputServiceTests
{
    private InputActionAsset actions;

    [SetUp]
    public void SetUp()
    {
        actions = ScriptableObject.CreateInstance<InputActionAsset>();
        InputActionMap playerMap = actions.AddActionMap("Player");
        playerMap.AddAction("Move", InputActionType.Value, "<Gamepad>/leftStick");
        playerMap.AddAction("Attack", InputActionType.Button, "<Keyboard>/space");
        actions.AddActionMap("UI").AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
    }

    [TearDown]
    public void TearDown()
    {
        actions.Disable();
        Object.DestroyImmediate(actions);
    }

    [Test]
    public void SetGameplayEnabled_TogglesOnlyThePlayerMap()
    {
        PlayerInputService service = new(actions);
        InputActionMap uiMap = actions.FindActionMap("UI");
        uiMap.Enable();

        service.SetGameplayEnabled(true);
        Assert.That(service.GameplayEnabled, Is.True);
        Assert.That(actions.FindActionMap("Player").enabled, Is.True);

        service.SetGameplayEnabled(false);
        Assert.That(service.GameplayEnabled, Is.False);
        Assert.That(uiMap.enabled, Is.True, "UI navigation belongs to the EventSystem and must stay enabled.");
    }

    [Test]
    public void DisabledGameplay_ReadsNeutralInput()
    {
        PlayerInputService service = new(actions);
        service.SetGameplayEnabled(false);

        Assert.That(service.Move, Is.EqualTo(Vector2.zero));
        Assert.That(service.AttackPressedThisFrame, Is.False);
    }

    [Test]
    public void Dispose_DisablesGameplayInput()
    {
        PlayerInputService service = new(actions);
        service.SetGameplayEnabled(true);

        service.Dispose();

        Assert.That(service.GameplayEnabled, Is.False);
    }
}

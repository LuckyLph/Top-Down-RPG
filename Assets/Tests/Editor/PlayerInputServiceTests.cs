using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

public class PlayerInputServiceTests
{
    private const string ProjectActionsPath = "Assets/InputSystem_Actions.inputactions";
    private static readonly string[] AbilityKeys = { "q", "w", "e", "r", "a", "s" };

    private InputActionAsset actions;

    [SetUp]
    public void SetUp()
    {
        actions = ScriptableObject.CreateInstance<InputActionAsset>();
        InputActionMap playerMap = actions.AddActionMap(PlayerInputService.PlayerMapName);
        playerMap.AddAction(PlayerInputService.MoveClickActionName, InputActionType.Button, "<Mouse>/rightButton");
        playerMap.AddAction(PlayerInputService.PointActionName, InputActionType.Value, "<Pointer>/position");
        playerMap.AddAction(PlayerInputService.StopActionName, InputActionType.Button, "<Keyboard>/x");
        for (int slot = 0; slot < AbilitySlots.Count; slot++)
        {
            playerMap.AddAction(PlayerInputService.AbilityActionName(slot), InputActionType.Button, $"<Keyboard>/{AbilityKeys[slot]}");
        }

        InputActionMap uiMap = actions.AddActionMap(PlayerInputService.UIMapName);
        uiMap.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
        uiMap.AddAction(PlayerInputService.CancelActionName, InputActionType.Button, "<Keyboard>/escape");
        actions.AddActionMap(PlayerInputService.MenusMapName).AddAction(PlayerInputService.MenuActionName, InputActionType.Button, "<Keyboard>/escape");
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
        PlayerInputService service = new(actions, null);
        InputActionMap uiMap = actions.FindActionMap("UI");
        uiMap.Enable();

        service.SetGameplayEnabled(true);
        Assert.That(service.GameplayEnabled, Is.True);
        Assert.That(actions.FindActionMap(PlayerInputService.PlayerMapName).enabled, Is.True);

        service.SetGameplayEnabled(false);
        Assert.That(service.GameplayEnabled, Is.False);
        Assert.That(uiMap.enabled, Is.True, "UI navigation belongs to the EventSystem and must stay enabled.");
    }

    [Test]
    public void DisabledGameplay_ReadsNeutralInput()
    {
        PlayerInputService service = new(actions, null);
        service.SetGameplayEnabled(false);

        Assert.That(service.PointerScreenPosition, Is.EqualTo(Vector2.zero));
        Assert.That(service.MovePressedThisFrame, Is.False);
        Assert.That(service.MoveHeld, Is.False);
        Assert.That(service.StopPressedThisFrame, Is.False);
        for (int slot = 0; slot < AbilitySlots.Count; slot++)
        {
            Assert.That(service.WasAbilityPressedThisFrame(slot), Is.False);
        }
    }

    [Test]
    public void Back_IsNeverPressedWhileTheUIMapIsDisabled_OrWithoutAPress()
    {
        PlayerInputService service = new(actions, null);

        Assert.That(service.BackPressedThisFrame, Is.False);

        actions.FindActionMap(PlayerInputService.UIMapName).Enable();
        Assert.That(service.BackPressedThisFrame, Is.False);
    }

    [Test]
    public void SetMenusEnabled_TogglesOnlyTheMenusMap_AndTheMenuKeyIsNeutralWhileOff()
    {
        PlayerInputService service = new(actions, null);

        service.SetMenusEnabled(true);
        Assert.That(service.MenusEnabled, Is.True);
        Assert.That(service.GameplayEnabled, Is.False);
        Assert.That(service.MenuPressedThisFrame, Is.False);

        service.SetMenusEnabled(false);
        Assert.That(service.MenusEnabled, Is.False);
        Assert.That(service.MenuPressedThisFrame, Is.False);
    }

    [Test]
    public void AbilitySlotsOutsideTheBar_AreNeverPressed()
    {
        PlayerInputService service = new(actions, null);
        service.SetGameplayEnabled(true);

        Assert.That(service.WasAbilityPressedThisFrame(-1), Is.False);
        Assert.That(service.WasAbilityPressedThisFrame(AbilitySlots.Count), Is.False);
    }

    [Test]
    public void WithoutAnEventSystem_ThePointerIsNeverOverUI()
    {
        PlayerInputService service = new(actions, null);

        Assert.That(service.IsPointerOverUI, Is.False);
    }

    [Test]
    public void MissingActions_LogAnError()
    {
        InputActionAsset incomplete = ScriptableObject.CreateInstance<InputActionAsset>();
        incomplete.AddActionMap(PlayerInputService.PlayerMapName).AddAction(PlayerInputService.MoveClickActionName, InputActionType.Button);
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("needs an input asset"));

        new PlayerInputService(incomplete, null);

        Object.DestroyImmediate(incomplete);
    }

    [Test]
    public void Dispose_DisablesGameplayInput()
    {
        PlayerInputService service = new(actions, null);
        service.SetGameplayEnabled(true);
        service.SetMenusEnabled(true);

        service.Dispose();

        Assert.That(service.GameplayEnabled, Is.False);
        Assert.That(service.MenusEnabled, Is.False);
    }

    [Test]
    public void ProjectAsset_BindsMouseMoveStopAndAbilityKeys()
    {
        InputActionAsset projectActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath);
        Assert.That(projectActions, Is.Not.Null);
        InputActionMap playerMap = projectActions.FindActionMap(PlayerInputService.PlayerMapName, true);

        AssertBoundTo(playerMap, PlayerInputService.MoveClickActionName, "<Mouse>/rightButton");
        AssertBoundTo(playerMap, PlayerInputService.PointActionName, "<Pointer>/position");
        AssertBoundTo(playerMap, PlayerInputService.StopActionName, "<Keyboard>/x");
        for (int slot = 0; slot < AbilitySlots.Count; slot++)
        {
            AssertBoundTo(playerMap, PlayerInputService.AbilityActionName(slot), $"<Keyboard>/{AbilityKeys[slot]}");
        }

        new PlayerInputService(projectActions, null);
    }

    [Test]
    public void ProjectAsset_PlayerMapHoldsOnlyTheGameplayActions_AndMenusMapOnlyTheMenuKey()
    {
        InputActionAsset projectActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath);
        InputActionMap playerMap = projectActions.FindActionMap(PlayerInputService.PlayerMapName, true);
        string[] expected = new[] { PlayerInputService.MoveClickActionName, PlayerInputService.PointActionName, PlayerInputService.StopActionName }
            .Concat(Enumerable.Range(0, AbilitySlots.Count).Select(PlayerInputService.AbilityActionName))
            .ToArray();

        Assert.That(playerMap.actions.Select(action => action.name), Is.EquivalentTo(expected));
        Assert.That(playerMap.bindings.Count, Is.EqualTo(expected.Length), "One binding per gameplay action.");

        InputActionMap menusMap = projectActions.FindActionMap("Menus", true);
        Assert.That(menusMap.actions.Select(action => action.name), Is.EqualTo(new[] { "Menu" }));
        AssertBoundTo(menusMap, "Menu", InputBindings.ReservedKeyPath);
        AssertBoundTo(menusMap, "Menu", "<Gamepad>/start");
    }

    private static void AssertBoundTo(InputActionMap map, string actionName, string path)
    {
        InputAction action = map.FindAction(actionName);
        Assert.That(action, Is.Not.Null, $"Missing action '{actionName}'.");
        Assert.That(action.bindings.Any(binding => binding.path == path), Is.True, $"'{actionName}' should be bound to '{path}'.");
    }
}

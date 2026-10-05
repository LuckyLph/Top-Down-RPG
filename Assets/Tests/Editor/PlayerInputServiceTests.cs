using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

/// <summary>
/// Tests for <see cref="PlayerInputService"/> on a hand-built action asset (map toggling, neutral input while
/// disabled, missing-action errors, disposal), plus the bindings in the project's input asset.
/// </summary>
public class PlayerInputServiceTests
{
    private const string ProjectActionsPath = "Assets/InputSystem_Actions.inputactions";
    private static readonly string[] AbilityKeys = { "q", "w", "e", "r", "1", "2" };

    private InputActionAsset actions;

    /// <summary>
    /// Builds an action asset with the player map (move click, point, stop and one action per ability slot), a UI
    /// map with submit and cancel, and a menus map with the menu key.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        actions = ScriptableObject.CreateInstance<InputActionAsset>();
        InputActionMap playerMap = actions.AddActionMap(PlayerInputService.PlayerMapName);
        playerMap.AddAction(PlayerInputService.MoveClickActionName, InputActionType.Button, "<Mouse>/rightButton");
        playerMap.AddAction(PlayerInputService.PointActionName, InputActionType.Value, "<Pointer>/position");
        playerMap.AddAction(PlayerInputService.StopActionName, InputActionType.Button, "<Keyboard>/s");
        for (int slot = 0; slot < AbilitySlots.Count; slot++)
        {
            playerMap.AddAction(PlayerInputService.AbilityActionName(slot), InputActionType.Button, $"<Keyboard>/{AbilityKeys[slot]}");
        }

        InputActionMap uiMap = actions.AddActionMap(PlayerInputService.UIMapName);
        uiMap.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
        uiMap.AddAction(PlayerInputService.CancelActionName, InputActionType.Button, "<Keyboard>/escape");
        actions.AddActionMap(PlayerInputService.MenusMapName).AddAction(PlayerInputService.MenuActionName, InputActionType.Button, "<Keyboard>/escape");
    }

    /// <summary>
    /// Disables and destroys the built action asset.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        actions.Disable();
        Object.DestroyImmediate(actions);
    }

    /// <summary>
    /// Turning gameplay on and off toggles the player map and leaves the UI map enabled.
    /// </summary>
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

    /// <summary>
    /// With gameplay disabled, the pointer reads zero and no move, stop or ability press is reported.
    /// </summary>
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

    /// <summary>
    /// Back reads as not pressed while the UI map is disabled, and while it is enabled with no key pressed.
    /// </summary>
    [Test]
    public void Back_IsNeverPressedWhileTheUIMapIsDisabled_OrWithoutAPress()
    {
        PlayerInputService service = new(actions, null);

        Assert.That(service.BackPressedThisFrame, Is.False);

        actions.FindActionMap(PlayerInputService.UIMapName).Enable();
        Assert.That(service.BackPressedThisFrame, Is.False);
    }

    /// <summary>
    /// Turning menus on and off toggles the menus map without enabling gameplay, and the menu key reads as not
    /// pressed both ways when nothing is pressed.
    /// </summary>
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

    /// <summary>
    /// Ability slots below zero or past the end of the bar always read as not pressed.
    /// </summary>
    [Test]
    public void AbilitySlotsOutsideTheBar_AreNeverPressed()
    {
        PlayerInputService service = new(actions, null);
        service.SetGameplayEnabled(true);

        Assert.That(service.WasAbilityPressedThisFrame(-1), Is.False);
        Assert.That(service.WasAbilityPressedThisFrame(AbilitySlots.Count), Is.False);
    }

    /// <summary>
    /// Without an event system the pointer never counts as over UI.
    /// </summary>
    [Test]
    public void WithoutAnEventSystem_ThePointerIsNeverOverUI()
    {
        PlayerInputService service = new(actions, null);

        Assert.That(service.IsPointerOverUI, Is.False);
    }

    /// <summary>
    /// Creating the service on an asset that lacks required actions logs an error.
    /// </summary>
    [Test]
    public void MissingActions_LogAnError()
    {
        InputActionAsset incomplete = ScriptableObject.CreateInstance<InputActionAsset>();
        incomplete.AddActionMap(PlayerInputService.PlayerMapName).AddAction(PlayerInputService.MoveClickActionName, InputActionType.Button);
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("needs an input asset"));

        new PlayerInputService(incomplete, null);

        Object.DestroyImmediate(incomplete);
    }

    /// <summary>
    /// Disposing the service turns off both gameplay and menus input.
    /// </summary>
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

    /// <summary>
    /// The project input asset binds move to the right mouse button, point to the pointer position, stop to S and
    /// the ability slots to Q, W, E, R, 1 and 2, and the service accepts it without logging an error.
    /// </summary>
    [Test]
    public void ProjectAsset_BindsMouseMoveStopAndAbilityKeys()
    {
        InputActionAsset projectActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath);
        Assert.That(projectActions, Is.Not.Null);
        InputActionMap playerMap = projectActions.FindActionMap(PlayerInputService.PlayerMapName, true);

        AssertBoundTo(playerMap, PlayerInputService.MoveClickActionName, "<Mouse>/rightButton");
        AssertBoundTo(playerMap, PlayerInputService.PointActionName, "<Pointer>/position");
        AssertBoundTo(playerMap, PlayerInputService.StopActionName, "<Keyboard>/s");
        for (int slot = 0; slot < AbilitySlots.Count; slot++)
        {
            AssertBoundTo(playerMap, PlayerInputService.AbilityActionName(slot), $"<Keyboard>/{AbilityKeys[slot]}");
        }

        new PlayerInputService(projectActions, null);
    }

    /// <summary>
    /// The project's player map holds exactly the gameplay actions with one binding each, and its menus map holds
    /// only the menu action, bound to the reserved key and the gamepad start button.
    /// </summary>
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

    /// <summary>
    /// Asserts <paramref name="map"/> has an action named <paramref name="actionName"/> with a binding to
    /// <paramref name="path"/>.
    /// </summary>
    private static void AssertBoundTo(InputActionMap map, string actionName, string path)
    {
        InputAction action = map.FindAction(actionName);
        Assert.That(action, Is.Not.Null, $"Missing action '{actionName}'.");
        Assert.That(action.bindings.Any(binding => binding.path == path), Is.True, $"'{actionName}' should be bound to '{path}'.");
    }
}

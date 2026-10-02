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

        service.Dispose();

        Assert.That(service.GameplayEnabled, Is.False);
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
    public void ProjectAsset_HasNoWasdMovementOrOldAttack_AndNoOtherActionOnTheGameplayKeys()
    {
        InputActionAsset projectActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath);
        InputActionMap playerMap = projectActions.FindActionMap(PlayerInputService.PlayerMapName, true);

        Assert.That(playerMap.FindAction("Move"), Is.Null, "WASD movement is replaced by click-to-move.");
        Assert.That(playerMap.FindAction("Attack"), Is.Null, "Auto attacks come from right clicking an enemy.");

        string[] gameplayPaths = AbilityKeys.Select(key => $"<Keyboard>/{key}")
            .Append("<Keyboard>/x")
            .Append("<Keyboard>/d")
            .Append("<Mouse>/rightButton")
            .ToArray();
        foreach (InputBinding binding in playerMap.bindings)
        {
            if (!gameplayPaths.Contains(binding.path))
            {
                continue;
            }

            bool isGameplayAction = binding.action == PlayerInputService.MoveClickActionName
                || binding.action == PlayerInputService.StopActionName
                || binding.action.StartsWith(PlayerInputService.AbilityActionPrefix);
            Assert.That(isGameplayAction, Is.True, $"'{binding.path}' is also bound to '{binding.action}'.");
        }
    }

    private static void AssertBoundTo(InputActionMap map, string actionName, string path)
    {
        InputAction action = map.FindAction(actionName);
        Assert.That(action, Is.Not.Null, $"Missing action '{actionName}'.");
        Assert.That(action.bindings.Any(binding => binding.path == path), Is.True, $"'{actionName}' should be bound to '{path}'.");
    }
}

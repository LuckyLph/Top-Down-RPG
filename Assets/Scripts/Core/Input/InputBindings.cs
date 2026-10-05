using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer.Unity;

/// <summary>
/// Owns the player's key bindings on the shared <see cref="InputActionAsset"/>: applies the saved overrides at boot,
/// assigns keys to the actions in the <see cref="RebindableActionList"/> (swapping with the action that had the key),
/// resets them, saves every change through <see cref="UserSettings"/>, and gives labels for the HUD and the options
/// screen, and runs interactive rebinds. It works on the session's <see cref="RuntimeInputActions"/> copy, never on the
/// project asset.
/// </summary>
public sealed class InputBindings : IInitializable, IDisposable, IInputBindingLabels
{
    public const string BindingGroup = "Keyboard&Mouse";
    public const string ReservedKeyPath = "<Keyboard>/escape";

    private const string KeyboardPath = "<Keyboard>";
    private const string KeyboardPrefix = KeyboardPath + "/";
    private const string AnyKeyPath = "<Keyboard>/anyKey";

    private static readonly string[] MousePaths =
    {
        "<Mouse>/leftButton", "<Mouse>/rightButton", "<Mouse>/middleButton", "<Mouse>/backButton", "<Mouse>/forwardButton",
    };

    private static readonly string[] MouseShortLabels = { "LMB", "RMB", "MMB", "M4", "M5" };

    private static readonly string[] MouseLabels =
    {
        "Left mouse button", "Right mouse button", "Middle mouse button", "Mouse button 4", "Mouse button 5",
    };

    private readonly InputActionAsset actions;
    private readonly UserSettings settings;
    private readonly List<InputAction> rebindable = new();
    private readonly List<int> bindingIndices = new();
    private readonly List<string> displayNames = new();
    private readonly List<string> sections = new();
    private readonly InputAction[] abilityActions = new InputAction[AbilitySlots.Count];
    private InputActionRebindingExtensions.RebindingOperation rebinding;
    private Action<RebindOutcome, InputAction> rebindFinished;

    /// <summary>
    /// Looks up the ability actions and the keyboard-and-mouse binding of every action listed in
    /// <paramref name="rebindableActions"/> on <paramref name="actions"/>, logging an error for a listed action that is
    /// missing, duplicated or has no such binding, and for a missing asset or list (nothing is rebindable then).
    /// </summary>
    public InputBindings(InputActionAsset actions, RebindableActionList rebindableActions, UserSettings settings)
    {
        this.actions = actions;
        this.settings = settings;
        for (int slot = 0; slot < abilityActions.Length && actions != null; slot++)
        {
            abilityActions[slot] = actions.FindAction($"{PlayerInputService.PlayerMapName}/{PlayerInputService.AbilityActionName(slot)}", false);
        }

        if (actions == null || rebindableActions == null)
        {
            Debug.LogError($"{nameof(InputBindings)} needs an input asset and a {nameof(RebindableActionList)}; nothing can be rebound.");
            return;
        }

        for (int i = 0; i < rebindableActions.Count; i++)
        {
            InputActionReference reference = rebindableActions[i].Action;
            InputAction listed = reference != null ? reference.action : null;
            InputAction action = listed != null ? actions.FindAction(listed.id) : null;
            int bindingIndex = action != null ? FindBindingIndex(action) : -1;
            if (bindingIndex < 0 || rebindable.Contains(action))
            {
                Debug.LogError($"Rebindable action {i} ('{rebindableActions[i].Label}') is missing, listed twice, or has no '{BindingGroup}' binding in '{actions.name}'.");
                continue;
            }

            rebindable.Add(action);
            bindingIndices.Add(bindingIndex);
            displayNames.Add(rebindableActions[i].Label);
            sections.Add(rebindableActions[i].Section);
        }
    }

    public event Action BindingsChanged;

    public int Count => rebindable.Count;

    public bool IsRebinding => rebinding != null;

    /// <summary>
    /// The rebindable action at <paramref name="index"/> (0 to <see cref="Count"/> - 1), in the list's order.
    /// </summary>
    public InputAction GetAction(int index)
    {
        return rebindable[index];
    }

    /// <summary>
    /// Whether <paramref name="action"/> is one of the actions the player may rebind; false for null.
    /// </summary>
    public bool IsRebindable(InputAction action)
    {
        return IndexOf(action) >= 0;
    }

    /// <summary>
    /// The name the options screen gives <paramref name="action"/>, such as "Class ability 1", or an empty string
    /// when it is not rebindable.
    /// </summary>
    public string GetDisplayName(InputAction action)
    {
        int index = IndexOf(action);
        return index >= 0 ? displayNames[index] : string.Empty;
    }

    /// <summary>
    /// The options screen section <paramref name="action"/> is listed under, or an empty string when it is not rebindable.
    /// </summary>
    public string GetSection(InputAction action)
    {
        int index = IndexOf(action);
        return index >= 0 ? sections[index] : string.Empty;
    }

    /// <summary>
    /// The control path <paramref name="action"/> is bound to now, override included, or null when it is not rebindable.
    /// </summary>
    public string GetPath(InputAction action)
    {
        int index = IndexOf(action);
        return index >= 0 ? EffectivePath(index) : null;
    }

    /// <summary>
    /// The short HUD label of the input bound to ability <paramref name="slot"/>, or an empty string for a slot out
    /// of range or an ability action missing from the asset.
    /// </summary>
    public string GetAbilityKeyLabel(int slot)
    {
        return slot >= 0 && slot < abilityActions.Length ? GetShortLabel(abilityActions[slot]) : string.Empty;
    }

    /// <summary>
    /// A short label for the HUD: the keyboard layout's name for a key, or LMB, RMB, MMB, M4 and M5 for mouse buttons.
    /// </summary>
    public string GetShortLabel(InputAction action)
    {
        return Label(action, true);
    }

    /// <summary>
    /// A label for the options screen: the keyboard layout's name for a key, or the mouse button spelled out.
    /// </summary>
    public string GetLabel(InputAction action)
    {
        return Label(action, false);
    }

    /// <summary>
    /// Whether a rebindable action may be bound to <paramref name="path"/>: any key but Escape, or a mouse button.
    /// </summary>
    public static bool IsAllowedPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (MouseIndex(path) >= 0)
        {
            return true;
        }

        return path.StartsWith(KeyboardPrefix, StringComparison.OrdinalIgnoreCase)
            && path.Length > KeyboardPrefix.Length
            && !SamePath(path, ReservedKeyPath)
            && !SamePath(path, AnyKeyPath);
    }

    /// <summary>
    /// Binds <paramref name="action"/> to <paramref name="path"/>, saves and raises <see cref="BindingsChanged"/>.
    /// If another rebindable action had that input, it gets <paramref name="action"/>'s previous one and is
    /// returned in <paramref name="swappedWith"/>. Returns false, changing nothing, for an action that is not
    /// rebindable or a path that is not allowed; returns true without a change when the action already has the path.
    /// </summary>
    public bool TryAssign(InputAction action, string path, out InputAction swappedWith)
    {
        swappedWith = null;
        int index = IndexOf(action);
        if (index < 0 || !IsAllowedPath(path))
        {
            return false;
        }

        string previous = EffectivePath(index);
        if (SamePath(previous, path))
        {
            return true;
        }

        int other = IndexOfPath(path, index);
        SetPath(index, path);
        if (other >= 0)
        {
            SetPath(other, previous);
            swappedWith = rebindable[other];
        }

        SaveAndNotify();
        return true;
    }

    /// <summary>
    /// Puts <paramref name="action"/> back on its default input, swapping with the action that had it, like
    /// <see cref="TryAssign"/>.
    /// </summary>
    public bool Reset(InputAction action, out InputAction swappedWith)
    {
        int index = IndexOf(action);
        if (index < 0)
        {
            swappedWith = null;
            return false;
        }

        return TryAssign(action, DefaultPath(index), out swappedWith);
    }

    /// <summary>
    /// Listens for the next key or mouse button and assigns it to <paramref name="action"/> through
    /// <see cref="TryAssign"/>. Escape cancels. <paramref name="finished"/> gets the outcome and the action that
    /// swapped inputs with this one, if any. Returns false, without listening, for an action that is not rebindable or
    /// is enabled, or while another rebind runs.
    /// </summary>
    public bool StartRebind(InputAction action, Action<RebindOutcome, InputAction> finished)
    {
        int index = IndexOf(action);
        if (index < 0 || rebinding != null)
        {
            return false;
        }

        if (action.enabled)
        {
            Debug.LogError($"Cannot rebind '{action.name}' while it is enabled; gameplay input must be off while the options screen listens.");
            return false;
        }

        bool assigned = false;
        InputAction swappedWith = null;
        rebindFinished = finished;
        rebinding = action.PerformInteractiveRebinding(bindingIndices[index])
            .WithCancelingThrough(ReservedKeyPath)
            .WithControlsHavingToMatchPath(KeyboardPath)
            .WithControlsExcluding(AnyKeyPath);
        for (int i = 0; i < MousePaths.Length; i++)
        {
            rebinding.WithControlsHavingToMatchPath(MousePaths[i]);
        }

        rebinding
            .OnApplyBinding((_, path) => assigned = TryAssign(action, path, out swappedWith))
            .OnComplete(_ => FinishRebind(assigned ? RebindOutcome.Assigned : RebindOutcome.Cancelled, swappedWith))
            .OnCancel(_ => FinishRebind(RebindOutcome.Cancelled, null))
            .Start();
        return true;
    }

    /// <summary>
    /// Cancels the running interactive rebind, if any, which reports <see cref="RebindOutcome.Cancelled"/> to its
    /// callback.
    /// </summary>
    public void CancelRebind()
    {
        if (rebinding != null)
        {
            rebinding.Cancel();
        }
    }

    /// <summary>
    /// Removes every binding override on the asset, putting all actions back on their defaults, then saves and
    /// raises <see cref="BindingsChanged"/>.
    /// </summary>
    public void ResetAll()
    {
        if (actions == null)
        {
            return;
        }

        actions.RemoveAllBindingOverrides();
        SaveAndNotify();
    }

    /// <summary>
    /// Starts watching for keyboard layout changes and applies the saved binding overrides.
    /// </summary>
    public void Initialize()
    {
        InputSystem.onDeviceChange += HandleDeviceChange;
        Load();
    }

    /// <summary>
    /// Cancels any running rebind and stops watching device changes.
    /// </summary>
    public void Dispose()
    {
        CancelRebind();
        InputSystem.onDeviceChange -= HandleDeviceChange;
    }

    /// <summary>
    /// Applies the overrides saved in <see cref="UserSettings"/>, then drops any that are unusable or make two
    /// rebindable actions share an input, warning and saving the cleaned set when something was dropped.
    /// </summary>
    private void Load()
    {
        if (actions == null)
        {
            return;
        }

        string overrides = settings.InputBindingOverrides;
        if (!string.IsNullOrEmpty(overrides))
        {
            ApplySaved(overrides);
        }

        bool dropped = DropInvalidOverrides();
        dropped |= DropDuplicateOverrides();
        if (dropped)
        {
            Debug.LogWarning("Some saved key bindings could not be used and were reset to their defaults.");
            settings.SetInputBindingOverrides(actions.SaveBindingOverridesAsJson());
        }
    }

    /// <summary>
    /// Loads the <paramref name="overrides"/> JSON onto the asset; when it cannot be read, warns, falls back to the
    /// defaults and clears the saved overrides.
    /// </summary>
    private void ApplySaved(string overrides)
    {
        try
        {
            actions.LoadBindingOverridesFromJson(overrides);
        }
        catch (Exception exception) when (exception is ArgumentException || exception is NullReferenceException)
        {
            Debug.LogWarning($"The saved key bindings could not be read ({exception.Message}); using the defaults.");
            actions.RemoveAllBindingOverrides();
            settings.SetInputBindingOverrides(string.Empty);
        }
    }

    /// <summary>
    /// Removes every override on the asset that sits anywhere but a rebindable action's keyboard-and-mouse binding,
    /// or whose path is not allowed. Returns whether any was removed.
    /// </summary>
    private bool DropInvalidOverrides()
    {
        bool dropped = false;
        foreach (InputActionMap map in actions.actionMaps)
        {
            foreach (InputAction action in map.actions)
            {
                int index = IndexOf(action);
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    string overridePath = action.bindings[i].overridePath;
                    if (overridePath == null)
                    {
                        continue;
                    }

                    bool usable = index >= 0 && bindingIndices[index] == i && IsAllowedPath(overridePath);
                    if (!usable)
                    {
                        action.RemoveBindingOverride(i);
                        dropped = true;
                    }
                }
            }
        }

        return dropped;
    }

    /// <summary>
    /// Repeatedly finds two rebindable actions on the same input and removes both their overrides until no such pair
    /// has an override left; logs an error for a pair whose defaults already clash. Returns whether any was removed.
    /// </summary>
    private bool DropDuplicateOverrides()
    {
        bool dropped = false;
        bool found = true;
        while (found)
        {
            found = false;
            for (int i = 0; i < rebindable.Count && !found; i++)
            {
                int other = IndexOfPath(EffectivePath(i), i);
                if (other < i)
                {
                    continue;
                }

                found = RemoveOverride(i) | RemoveOverride(other);
                if (!found)
                {
                    Debug.LogError($"'{rebindable[i].name}' and '{rebindable[other].name}' share the default input '{EffectivePath(i)}'.");
                }
            }

            dropped |= found;
        }

        return dropped;
    }

    /// <summary>
    /// Removes the override on rebindable action <paramref name="index"/>'s binding; returns false when it had none.
    /// </summary>
    private bool RemoveOverride(int index)
    {
        int bindingIndex = bindingIndices[index];
        if (rebindable[index].bindings[bindingIndex].overridePath == null)
        {
            return false;
        }

        rebindable[index].RemoveBindingOverride(bindingIndex);
        return true;
    }

    /// <summary>
    /// Binds rebindable action <paramref name="index"/> to <paramref name="path"/>, removing the override instead
    /// when the path is its default so only real changes are saved.
    /// </summary>
    private void SetPath(int index, string path)
    {
        if (SamePath(DefaultPath(index), path))
        {
            RemoveOverride(index);
        }
        else
        {
            rebindable[index].ApplyBindingOverride(bindingIndices[index], path);
        }
    }

    /// <summary>
    /// Disposes the finished rebinding operation and reports <paramref name="outcome"/> and the swapped action to the
    /// callback given to <see cref="StartRebind"/>, clearing it first so it runs once.
    /// </summary>
    private void FinishRebind(RebindOutcome outcome, InputAction swappedWith)
    {
        rebinding.Dispose();
        rebinding = null;
        Action<RebindOutcome, InputAction> finished = rebindFinished;
        rebindFinished = null;
        finished?.Invoke(outcome, swappedWith);
    }

    /// <summary>
    /// Stores the asset's current overrides in <see cref="UserSettings"/> and raises <see cref="BindingsChanged"/>.
    /// </summary>
    private void SaveAndNotify()
    {
        settings.SetInputBindingOverrides(actions.SaveBindingOverridesAsJson());
        BindingsChanged?.Invoke();
    }

    /// <summary>
    /// Raises <see cref="BindingsChanged"/> when a keyboard's configuration (its layout) changes, since key labels
    /// follow the layout.
    /// </summary>
    private void HandleDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device is Keyboard && change == InputDeviceChange.ConfigurationChanged)
        {
            BindingsChanged?.Invoke();
        }
    }

    /// <summary>
    /// The label of <paramref name="action"/>'s keyboard-and-mouse binding (also for actions that are not
    /// rebindable): a mouse button name, short or spelled out, or the binding's display string for a key. Empty
    /// for null or an action without such a binding.
    /// </summary>
    private string Label(InputAction action, bool shortLabel)
    {
        if (action == null)
        {
            return string.Empty;
        }

        int index = IndexOf(action);
        int bindingIndex = index >= 0 ? bindingIndices[index] : FindBindingIndex(action);
        if (bindingIndex < 0)
        {
            return string.Empty;
        }

        int mouse = MouseIndex(action.bindings[bindingIndex].effectivePath);
        if (mouse >= 0)
        {
            return shortLabel ? MouseShortLabels[mouse] : MouseLabels[mouse];
        }

        return action.GetBindingDisplayString(bindingIndex, InputBinding.DisplayStringOptions.DontIncludeInteractions);
    }

    /// <summary>
    /// The position of <paramref name="action"/> in the rebindable list, or -1 for null or an action not on it.
    /// </summary>
    private int IndexOf(InputAction action)
    {
        return action != null ? rebindable.IndexOf(action) : -1;
    }

    /// <summary>
    /// The first rebindable action other than <paramref name="except"/> currently bound to <paramref name="path"/>,
    /// or -1 when there is none.
    /// </summary>
    private int IndexOfPath(string path, int except)
    {
        for (int i = 0; i < rebindable.Count; i++)
        {
            if (i != except && SamePath(EffectivePath(i), path))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The path rebindable action <paramref name="index"/> is bound to now, override included.
    /// </summary>
    private string EffectivePath(int index)
    {
        return rebindable[index].bindings[bindingIndices[index]].effectivePath;
    }

    /// <summary>
    /// The path rebindable action <paramref name="index"/> has in the asset, ignoring any override.
    /// </summary>
    private string DefaultPath(int index)
    {
        return rebindable[index].bindings[bindingIndices[index]].path;
    }

    /// <summary>
    /// The index of <paramref name="action"/>'s first non-composite binding in the <see cref="BindingGroup"/> group,
    /// or -1 when it has none.
    /// </summary>
    private static int FindBindingIndex(InputAction action)
    {
        InputBinding mask = InputBinding.MaskByGroup(BindingGroup);
        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding binding = action.bindings[i];
            if (!binding.isComposite && !binding.isPartOfComposite && mask.Matches(binding))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The index of <paramref name="path"/> among the supported mouse buttons, or -1 when it is not one of them.
    /// </summary>
    private static int MouseIndex(string path)
    {
        for (int i = 0; i < MousePaths.Length; i++)
        {
            if (SamePath(MousePaths[i], path))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Whether two control paths are equal, ignoring case.
    /// </summary>
    private static bool SamePath(string a, string b)
    {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}

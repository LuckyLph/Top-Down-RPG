using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using Object = UnityEngine.Object;

/// <summary>
/// This session's copy of the project input asset, which every runtime reader uses, so binding overrides never touch
/// the asset itself. Points the <see cref="EventSystem"/>'s UI module at the copy and destroys the copy when disposed.
/// </summary>
public sealed class RuntimeInputActions : IDisposable
{
    public RuntimeInputActions(InputActionAsset projectActions, EventSystem eventSystem)
    {
        if (projectActions == null)
        {
            return;
        }

        Actions = Object.Instantiate(projectActions);
        Actions.name = projectActions.name;
        InputSystemUIInputModule module = eventSystem != null ? eventSystem.GetComponent<InputSystemUIInputModule>() : null;
        if (module != null)
        {
            bool wasEnabled = module.enabled;
            module.enabled = false;
            module.actionsAsset = Actions;
            module.enabled = wasEnabled;
        }
    }

    /// <summary>The copy, or null without a project asset.</summary>
    public InputActionAsset Actions { get; }

    public void Dispose()
    {
        if (Actions == null)
        {
            return;
        }

        Actions.Disable();
        if (Application.isPlaying)
        {
            Object.Destroy(Actions);
        }
        else
        {
            Object.DestroyImmediate(Actions);
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The actions the player can rebind, in the order the options screen lists them, each with its label, section and the
/// one-line description the options screen shows while its row is focused (Docs/UiRevamp.md, Phase 8).
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Input/Rebindable Action List", fileName = "RebindableActions")]
public sealed class RebindableActionList : ScriptableObject
{
    /// <summary>One rebindable action with the label, section and description the options screen shows it with.</summary>
    [Serializable]
    public struct Entry
    {
        [SerializeField] private InputActionReference action;
        [SerializeField] private string label;
        [SerializeField] private string section;
        [SerializeField, Tooltip("What the action does, in a line or two, shown at the options screen's foot while its row is focused.")]
        private string description;

        /// <summary>
        /// Creates an entry for <paramref name="action"/>, shown as <paramref name="label"/> under
        /// <paramref name="section"/> and described by <paramref name="description"/>.
        /// </summary>
        public Entry(InputActionReference action, string label, string section, string description = "")
        {
            this.action = action;
            this.label = label;
            this.section = section;
            this.description = description;
        }

        public InputActionReference Action => action;
        public string Label => label;
        public string Section => section;
        public string Description => description;
    }

    [SerializeField] private Entry[] entries = Array.Empty<Entry>();

    public int Count => entries != null ? entries.Length : 0;

    public Entry this[int index] => entries[index];
}

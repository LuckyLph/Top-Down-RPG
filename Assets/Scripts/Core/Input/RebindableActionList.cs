using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The actions the player can rebind, in the order the options screen lists them, each with its label and section.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Input/Rebindable Action List", fileName = "RebindableActions")]
public sealed class RebindableActionList : ScriptableObject
{
    /// <summary>One rebindable action with the label and section the options screen shows it under.</summary>
    [Serializable]
    public struct Entry
    {
        [SerializeField] private InputActionReference action;
        [SerializeField] private string label;
        [SerializeField] private string section;

        /// <summary>
        /// Creates an entry for <paramref name="action"/>, shown as <paramref name="label"/> under
        /// <paramref name="section"/>.
        /// </summary>
        public Entry(InputActionReference action, string label, string section)
        {
            this.action = action;
            this.label = label;
            this.section = section;
        }

        public InputActionReference Action => action;
        public string Label => label;
        public string Section => section;
    }

    [SerializeField] private Entry[] entries = Array.Empty<Entry>();

    public int Count => entries != null ? entries.Length : 0;

    public Entry this[int index] => entries[index];
}

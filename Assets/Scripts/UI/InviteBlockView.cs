using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The invite block (Docs/UiRevamp.md, Phases 6 and 9), the `InviteBlock` component shared by the in-game menu's Party
/// page and Play together's host column: "Friends join at" and the port, then each address friends join this machine at
/// with its network's name under it and a Copy button at the right, or a line saying there is none. Passive:
/// <see cref="HostInvite"/> fills it, and the page holding it wires its Copy buttons into its navigation.
/// </summary>
[DisallowMultipleComponent]
public class InviteBlockView : MonoBehaviour
{
    [SerializeField] private TMP_Text port;
    [SerializeField, Tooltip("The line shown when no address was found.")] private GameObject none;
    [SerializeField, Tooltip("An address row per address there can be.")] private GameObject[] addressRows = new GameObject[0];
    [SerializeField] private TMP_Text[] addresses = new TMP_Text[0];
    [SerializeField] private TMP_Text[] networks = new TMP_Text[0];
    [SerializeField] private Button[] copyButtons = new Button[0];
    [SerializeField] private TMP_Text[] copyTexts = new TMP_Text[0];

    public int AddressCapacity => addressRows != null ? addressRows.Length : 0;

    /// <summary>
    /// How many addresses show.
    /// </summary>
    public int ShownAddresses { get; private set; }

    /// <summary>
    /// Logs an error when a part is missing or the address arrays differ in length.
    /// </summary>
    private void Awake()
    {
        int count = AddressCapacity;
        if (port == null || none == null || count == 0 || addresses.Length != count || networks.Length != count || copyButtons.Length != count || copyTexts.Length != count)
        {
            Debug.LogError($"{name} needs its port, none line and as many addresses, networks and Copy buttons as address rows.", this);
        }
    }

    /// <summary>
    /// The Copy button of the address at <paramref name="index"/>.
    /// </summary>
    public Button CopyButton(int index)
    {
        return copyButtons[index];
    }

    /// <summary>
    /// Shows <paramref name="portText"/> and the first <see cref="AddressCapacity"/> of <paramref name="hostAddresses"/>,
    /// each with its Copy button reading <paramref name="copyLabel"/>, or the none line without any. Returns how many
    /// show.
    /// </summary>
    public int Show(IReadOnlyList<HostAddress> hostAddresses, string portText, string copyLabel)
    {
        port.text = portText;
        int count = Mathf.Min(hostAddresses.Count, AddressCapacity);
        for (int i = 0; i < AddressCapacity; i++)
        {
            bool shown = i < count;
            addressRows[i].SetActive(shown);
            if (shown)
            {
                addresses[i].text = hostAddresses[i].Address;
                networks[i].text = hostAddresses[i].Network;
                copyTexts[i].text = copyLabel;
            }
        }

        none.SetActive(count == 0);
        ShownAddresses = count;
        return count;
    }

    /// <summary>
    /// Shows <paramref name="label"/> on the Copy button of the address at <paramref name="index"/>.
    /// </summary>
    public void SetCopyLabel(int index, string label)
    {
        if (index >= 0 && index < AddressCapacity && copyTexts[index] != null)
        {
            copyTexts[index].text = label;
        }
    }

    /// <summary>
    /// The label on the Copy button of the address at <paramref name="index"/>, for tests.
    /// </summary>
    internal string CopyLabel(int index)
    {
        return copyTexts[index].text;
    }

    /// <summary>
    /// The address shown at <paramref name="index"/> and its network's name, for tests.
    /// </summary>
    internal (string address, string network) Address(int index)
    {
        return (addresses[index].text, networks[index].text);
    }
}

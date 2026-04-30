using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerWeaponHud : MonoBehaviour
{
    private TextMeshProUGUI weaponNameText;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    public void SetWeaponName(string weaponName)
    {
        ResolveReferences();
        if (weaponNameText == null)
        {
            return;
        }

        weaponNameText.text = weaponName;
    }

    private void ResolveReferences()
    {
        if (weaponNameText == null)
        {
            weaponNameText = GetComponent<TextMeshProUGUI>();
        }
    }
}

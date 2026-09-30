using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Passive HUD view: holds the prefab's UI references and renders what PlayerHudPresenter tells it.
[DisallowMultipleComponent]
public class PlayerHudView : MonoBehaviour
{
    private const string HealthTextFormat = "HP {0}";
    private const string UnknownHealthText = "HP --";

    [SerializeField] private Image healthFill;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private Image weaponIcon;
    [SerializeField] private Color activeWeaponIconTint = Color.white;
    [SerializeField] private Color missingWeaponIconTint = new(1f, 1f, 1f, 0.25f);

    public void SetHealth(int current, int max)
    {
        int clampedMax = Mathf.Max(1, max);
        int clampedCurrent = Mathf.Clamp(current, 0, clampedMax);

        if (healthFill != null)
        {
            healthFill.fillAmount = (float)clampedCurrent / clampedMax;
        }

        if (healthText != null)
        {
            healthText.text = string.Format(HealthTextFormat, clampedCurrent);
        }
    }

    public void ClearHealth()
    {
        if (healthFill != null)
        {
            healthFill.fillAmount = 0f;
        }

        if (healthText != null)
        {
            healthText.text = UnknownHealthText;
        }
    }

    public void SetWeapon(Sprite icon)
    {
        if (weaponIcon == null)
        {
            return;
        }

        weaponIcon.sprite = icon;
        weaponIcon.color = icon == null ? missingWeaponIconTint : activeWeaponIconTint;
    }

    // Used by tests and editor tooling that build the HUD in code.
    internal void ConfigureReferences(Image fill, TextMeshProUGUI text, Image icon)
    {
        healthFill = fill;
        healthText = text;
        weaponIcon = icon;
    }
}

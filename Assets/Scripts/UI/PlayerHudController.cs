using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

[DisallowMultipleComponent]
public class PlayerHudController : MonoBehaviour
{
    private const string HealthFillName = "HealthFill";
    private const string HealthTextName = "HealthText";
    private const string WeaponIconName = "WeaponIcon";
    private const string HealthTextFormat = "HP {0}";

    [SerializeField] private Color activeWeaponIconTint = Color.white;
    [SerializeField] private Color missingWeaponIconTint = new(1f, 1f, 1f, 0.25f);

    private Image healthFillImage;
    private TextMeshProUGUI healthText;
    private Image weaponIconImage;
    private Health playerHealth;
    private PlayerWeaponController playerWeaponController;
    private bool subscribedToPlayer;
    private bool warnedMissingUiReferences;

    private void Awake()
    {
        ResolveLocalUiReferences();
    }

    private void OnEnable()
    {
        ResolveLocalUiReferences();
        SubscribeToPlayerEvents();
        UpdateAllDisplay();
    }

    // Injected by the Gameplay LifetimeScope with the session's player.
    [Inject]
    public void Construct(PlayerController player)
    {
        Bind(
            player != null ? player.GetComponent<Health>() : null,
            player != null ? player.GetComponent<PlayerWeaponController>() : null);
    }

    public void Bind(Health health, PlayerWeaponController weaponController)
    {
        UnsubscribeFromPlayerEvents();
        playerHealth = health;
        playerWeaponController = weaponController;
        SubscribeToPlayerEvents();
        UpdateAllDisplay();
    }

    private void OnDisable()
    {
        UnsubscribeFromPlayerEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeFromPlayerEvents();
    }

    private void HandlePlayerDamaged(Health.DamageEvent _)
    {
        UpdateHealthDisplay();
    }

    private void HandlePlayerDied(Health _)
    {
        UpdateHealthDisplay();
    }

    private void HandleEquippedWeaponChanged(PlayerWeapon weapon)
    {
        UpdateWeaponDisplay(weapon);
    }

    private void SubscribeToPlayerEvents()
    {
        if (subscribedToPlayer)
        {
            return;
        }

        if (playerHealth != null)
        {
            playerHealth.Damaged += HandlePlayerDamaged;
            playerHealth.Died += HandlePlayerDied;
        }

        if (playerWeaponController != null)
        {
            playerWeaponController.EquippedWeaponChanged += HandleEquippedWeaponChanged;
        }

        subscribedToPlayer = true;
    }

    private void UnsubscribeFromPlayerEvents()
    {
        if (!subscribedToPlayer)
        {
            return;
        }

        if (playerHealth != null)
        {
            playerHealth.Damaged -= HandlePlayerDamaged;
            playerHealth.Died -= HandlePlayerDied;
        }

        if (playerWeaponController != null)
        {
            playerWeaponController.EquippedWeaponChanged -= HandleEquippedWeaponChanged;
        }

        subscribedToPlayer = false;
    }

    private void UpdateAllDisplay()
    {
        UpdateHealthDisplay();
        UpdateWeaponDisplay(playerWeaponController != null ? playerWeaponController.CurrentWeapon : null);
    }

    private void UpdateHealthDisplay()
    {
        if (healthFillImage == null && healthText == null)
        {
            return;
        }

        if (playerHealth == null)
        {
            if (healthFillImage != null)
            {
                healthFillImage.fillAmount = 0f;
            }

            if (healthText != null)
            {
                healthText.text = "HP --";
            }

            return;
        }

        int maxHealth = Mathf.Max(1, playerHealth.MaxHealth);
        int currentHealth = Mathf.Clamp(playerHealth.CurrentHealth, 0, maxHealth);

        if (healthFillImage != null)
        {
            healthFillImage.fillAmount = (float)currentHealth / maxHealth;
        }

        if (healthText != null)
        {
            healthText.text = string.Format(HealthTextFormat, currentHealth);
        }
    }

    private void UpdateWeaponDisplay(PlayerWeapon weapon)
    {
        if (weaponIconImage == null)
        {
            return;
        }

        Sprite icon = weapon != null ? weapon.HudIcon : null;
        weaponIconImage.sprite = icon;
        weaponIconImage.color = icon == null ? missingWeaponIconTint : activeWeaponIconTint;
    }

    private void ResolveLocalUiReferences()
    {
        if (healthFillImage == null)
        {
            healthFillImage = FindNamedComponentInChildren<Image>(HealthFillName);
        }

        if (healthText == null)
        {
            healthText = FindNamedComponentInChildren<TextMeshProUGUI>(HealthTextName);
        }

        if (weaponIconImage == null)
        {
            weaponIconImage = FindNamedComponentInChildren<Image>(WeaponIconName);
        }

        bool hasAllReferences =
            healthFillImage != null &&
            healthText != null &&
            weaponIconImage != null;

        if (!hasAllReferences && !warnedMissingUiReferences)
        {
            Debug.LogWarning("PlayerHudController could not resolve all local UI references.");
            warnedMissingUiReferences = true;
        }
        else if (hasAllReferences)
        {
            warnedMissingUiReferences = false;
        }
    }

    private T FindNamedComponentInChildren<T>(string objectName) where T : Component
    {
        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || candidate.name != objectName)
            {
                continue;
            }

            if (candidate.TryGetComponent(out T component))
            {
                return component;
            }
        }

        return null;
    }
}

using UnityEngine;

[DisallowMultipleComponent]
public class PlayerWeaponController : MonoBehaviour
{
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerWeapon startingWeapon;

    private PlayerWeapon currentWeapon;
    private PlayerWeaponHud hud;
    private SpriteRenderer ownerSpriteRenderer;
    private float nextAttackTime;

    public PlayerWeapon CurrentWeapon => currentWeapon;
    public string CurrentWeaponName
    {
        get
        {
            if (currentWeapon != null)
            {
                return currentWeapon.DisplayName;
            }

            return startingWeapon != null ? startingWeapon.DisplayName : string.Empty;
        }
    }
    public Vector2 CurrentFacingDirection => playerController != null ? playerController.FacingDirection : Vector2.down;

    private void Awake()
    {
        ResolveReferences();
        ownerSpriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        Equip(startingWeapon);
    }

    private void OnValidate()
    {
        ResolveReferences();
        UpdateWeaponNameLabel();
    }

    public void Equip(PlayerWeapon weapon)
    {
        ResolveReferences();
        currentWeapon = weapon;
        UpdateWeaponNameLabel();
    }

    public bool TryAttack()
    {
        ResolveReferences();

        if (currentWeapon == null || Time.time < nextAttackTime)
        {
            return false;
        }

        Vector2 attackDirection = CurrentFacingDirection;
        if (attackDirection.sqrMagnitude <= 0.0001f)
        {
            attackDirection = Vector2.down;
        }

        PlayerSlashAttack.Spawn(transform, currentWeapon, attackDirection, ownerSpriteRenderer);
        nextAttackTime = Time.time + currentWeapon.AttackCooldown;
        return true;
    }

    private void ResolveReferences()
    {
        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }

        if (hud == null)
        {
            hud = FindAnyObjectByType<PlayerWeaponHud>(FindObjectsInactive.Include);
        }
    }

    private void UpdateWeaponNameLabel()
    {
        if (hud == null)
        {
            return;
        }

        hud.SetWeaponName(CurrentWeaponName);
    }
}

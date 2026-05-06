using System;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerWeaponController : MonoBehaviour
{
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerWeapon startingWeapon;

    private PlayerWeapon currentWeapon;
    private SpriteRenderer ownerSpriteRenderer;
    private float nextAttackTime;

    public event Action<PlayerWeapon> EquippedWeaponChanged;

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
    }

    public void Equip(PlayerWeapon weapon)
    {
        ResolveReferences();
        currentWeapon = weapon;
        EquippedWeaponChanged?.Invoke(currentWeapon);
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

        SwordSlashAttack.Spawn(transform, currentWeapon, attackDirection, ownerSpriteRenderer);
        nextAttackTime = Time.time + currentWeapon.AttackCooldown;
        return true;
    }

    private void ResolveReferences()
    {
        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }
    }
}

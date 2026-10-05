using System;
using UnityEngine;

/// <summary>
/// A unit's hit points, starting full. Raises <see cref="Damaged"/> and <see cref="Died"/> when damage lands,
/// <see cref="Healed"/> for HP added and <see cref="Restored"/> when refilled. Gameplay damage and heals go through
/// <see cref="DamageService"/>; <see cref="NetworkHealth"/> mirrors HP to clients.
/// </summary>
[DisallowMultipleComponent]
public class Health : MonoBehaviour
{
    /// <summary>
    /// One application of damage as raised by <see cref="Damaged"/>: the HP actually removed, the source, the HP
    /// before and after, and the damage type and flags.
    /// </summary>
    [Serializable]
    public readonly struct DamageEvent
    {
        /// <summary>
        /// Creates the event for <paramref name="amount"/> HP removed from <paramref name="target"/>.
        /// </summary>
        public DamageEvent(Health target, int amount, GameObject source, int previousHealth, int currentHealth, DamageType type, DamageFlags flags)
        {
            Target = target;
            Amount = amount;
            Source = source;
            PreviousHealth = previousHealth;
            CurrentHealth = currentHealth;
            Type = type;
            Flags = flags;
        }

        public Health Target { get; }
        public int Amount { get; }
        public GameObject Source { get; }
        public int PreviousHealth { get; }
        public int CurrentHealth { get; }
        public DamageType Type { get; }
        public DamageFlags Flags { get; }
    }

    [SerializeField, Min(1)] private int maxHealth = 10;

    private int currentHealth;
    private bool initialized;

    public event Action<DamageEvent> Damaged;
    public event Action<Health> Died;
    public event Action<Health> Restored;
    public event Action<Health, int> Healed;

    public int MaxHealth => maxHealth;

    public int CurrentHealth
    {
        get
        {
            InitializeIfNeeded();
            return currentHealth;
        }
    }

    public bool IsDead => CurrentHealth <= 0;

    /// <summary>
    /// Fills HP to the maximum unless something already read or changed it.
    /// </summary>
    private void Awake()
    {
        InitializeIfNeeded();
    }

    /// <summary>
    /// Keeps the maximum at 1 or more.
    /// </summary>
    private void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
    }

    /// <summary>
    /// Removes up to <paramref name="amount"/> HP from a living unit and returns what was removed, raising
    /// <see cref="Damaged"/> and, when HP reaches zero, <see cref="Died"/>. Does nothing and returns 0 for a
    /// non-positive amount or a dead unit. Applies no faction or authority rules; that is <see cref="DamageService"/>.
    /// </summary>
    public int ApplyDamage(int amount, GameObject source = null, DamageType type = DamageType.Physical, DamageFlags flags = DamageFlags.None)
    {
        InitializeIfNeeded();

        if (amount <= 0 || currentHealth <= 0)
        {
            return 0;
        }

        int previousHealth = currentHealth;
        currentHealth = Mathf.Max(0, currentHealth - amount);
        int appliedDamage = previousHealth - currentHealth;

        if (appliedDamage <= 0)
        {
            return 0;
        }

        Damaged?.Invoke(new DamageEvent(this, appliedDamage, source, previousHealth, currentHealth, type, flags));

        if (currentHealth == 0)
        {
            Died?.Invoke(this);
        }

        return appliedDamage;
    }

    /// <summary>
    /// Adds up to <paramref name="amount"/> HP without exceeding the maximum and raises <see cref="Healed"/> with
    /// what was added. Never brings a dead unit back; that is <see cref="Restore"/>.
    /// </summary>
    public int Heal(int amount)
    {
        InitializeIfNeeded();

        if (amount <= 0 || currentHealth <= 0)
        {
            return 0;
        }

        int previousHealth = currentHealth;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        int healed = currentHealth - previousHealth;
        if (healed > 0)
        {
            Healed?.Invoke(this, healed);
        }

        return healed;
    }

    /// <summary>
    /// Sets HP to <paramref name="value"/> (clamped) for network sync without raising <see cref="Damaged"/>. Raises
    /// <see cref="Died"/> when it takes a living unit to zero, so death reactions run on machines that only learn
    /// the result, such as a client that receives an already dead player.
    /// </summary>
    public void SyncTo(int value)
    {
        InitializeIfNeeded();
        bool wasAlive = currentHealth > 0;
        currentHealth = Mathf.Clamp(value, 0, maxHealth);
        if (wasAlive && currentHealth == 0)
        {
            Died?.Invoke(this);
        }
    }

    /// <summary>
    /// Refills HP to the maximum, reviving a dead unit, and raises <see cref="Restored"/>; does nothing when already
    /// full.
    /// </summary>
    public void Restore()
    {
        InitializeIfNeeded();

        if (currentHealth == maxHealth)
        {
            return;
        }

        currentHealth = maxHealth;
        Restored?.Invoke(this);
    }

    /// <summary>
    /// Sets HP to the maximum the first time it is needed, so reads and changes before <c>Awake</c> see a full unit.
    /// </summary>
    private void InitializeIfNeeded()
    {
        if (initialized)
        {
            return;
        }

        currentHealth = Mathf.Max(1, maxHealth);
        initialized = true;
    }
}

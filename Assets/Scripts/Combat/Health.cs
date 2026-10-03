using System;
using UnityEngine;

[DisallowMultipleComponent]
public class Health : MonoBehaviour
{
    [Serializable]
    public readonly struct DamageEvent
    {
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

    private void Awake()
    {
        InitializeIfNeeded();
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
    }

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

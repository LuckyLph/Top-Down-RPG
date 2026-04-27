using System;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class Health : MonoBehaviour
{
    [Serializable]
    public readonly struct DamageEvent
    {
        public DamageEvent(Health target, int amount, GameObject source, int previousHealth, int currentHealth)
        {
            Target = target;
            Amount = amount;
            Source = source;
            PreviousHealth = previousHealth;
            CurrentHealth = currentHealth;
        }

        public Health Target { get; }
        public int Amount { get; }
        public GameObject Source { get; }
        public int PreviousHealth { get; }
        public int CurrentHealth { get; }
    }

    [SerializeField, Min(1)] private int maxHealth = 10;

    private int currentHealth;
    private bool initialized;

    public event Action<DamageEvent> Damaged;
    public event Action<Health> Died;

    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth;
    public bool IsDead => initialized && currentHealth <= 0;

    private void Awake()
    {
        InitializeIfNeeded();
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
    }

    public int ApplyDamage(int amount, GameObject source = null)
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

        Damaged?.Invoke(new DamageEvent(this, appliedDamage, source, previousHealth, currentHealth));

        if (currentHealth == 0)
        {
            Died?.Invoke(this);
        }

        return appliedDamage;
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

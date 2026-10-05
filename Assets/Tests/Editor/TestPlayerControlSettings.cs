using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds <see cref="PlayerControlSettings"/> instances for tests and assigns them to controllers, without
/// touching the project's settings asset.
/// </summary>
public static class TestPlayerControlSettings
{
    /// <summary>
    /// Creates a hidden, unsaved settings instance with the given tuning, the Enemy layer as enemies and the Player
    /// layer as allies; the caller destroys it.
    /// </summary>
    public static PlayerControlSettings Create(
        float moveSpeed = 5f,
        float stuckTimeout = 0.75f,
        float holdReevaluateInterval = 0.15f,
        float repathInterval = 0.5f,
        float targetMoveRepathDistance = 0.5f,
        float pickRadius = 0.35f,
        float castBufferWindow = 0.4f,
        TerrainMovementProfile2D movementProfile = null,
        float attackMoveAcquireRange = 3f)
    {
        PlayerControlSettings settings = ScriptableObject.CreateInstance<PlayerControlSettings>();
        settings.hideFlags = HideFlags.HideAndDontSave;
        SerializedObject serialized = new(settings);
        serialized.FindProperty("moveSpeed").floatValue = moveSpeed;
        serialized.FindProperty("stuckTimeout").floatValue = stuckTimeout;
        serialized.FindProperty("holdReevaluateInterval").floatValue = holdReevaluateInterval;
        serialized.FindProperty("repathInterval").floatValue = repathInterval;
        serialized.FindProperty("targetMoveRepathDistance").floatValue = targetMoveRepathDistance;
        serialized.FindProperty("pickRadius").floatValue = pickRadius;
        serialized.FindProperty("castBufferWindow").floatValue = castBufferWindow;
        serialized.FindProperty("attackMoveAcquireRange").floatValue = attackMoveAcquireRange;
        serialized.FindProperty("movementProfile").objectReferenceValue = movementProfile;
        serialized.FindProperty("enemyLayers").intValue = LayerMask.GetMask("Enemy");
        serialized.FindProperty("allyLayers").intValue = LayerMask.GetMask("Player");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return settings;
    }

    /// <summary>
    /// Sets <paramref name="controller"/>'s serialized control settings to <paramref name="settings"/>.
    /// </summary>
    public static void Assign(PlayerController controller, PlayerControlSettings settings)
    {
        SerializedObject serialized = new(controller);
        serialized.FindProperty("controlSettings").objectReferenceValue = settings;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}

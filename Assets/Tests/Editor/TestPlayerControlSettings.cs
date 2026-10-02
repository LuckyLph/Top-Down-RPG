using UnityEditor;
using UnityEngine;

public static class TestPlayerControlSettings
{
    public static PlayerControlSettings Create(
        float moveSpeed = 5f,
        float stuckTimeout = 0.75f,
        float holdReevaluateInterval = 0.15f,
        float repathInterval = 0.5f,
        float targetMoveRepathDistance = 0.5f,
        float pickRadius = 0.35f,
        TerrainMovementProfile2D movementProfile = null)
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
        serialized.FindProperty("movementProfile").objectReferenceValue = movementProfile;
        serialized.FindProperty("enemyLayers").intValue = LayerMask.GetMask("Enemy");
        serialized.FindProperty("allyLayers").intValue = LayerMask.GetMask("Player");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return settings;
    }

    public static void Assign(PlayerController controller, PlayerControlSettings settings)
    {
        SerializedObject serialized = new(controller);
        serialized.FindProperty("controlSettings").objectReferenceValue = settings;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}

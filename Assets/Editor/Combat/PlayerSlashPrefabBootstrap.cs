using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PlayerSlashPrefabBootstrap
{
    private const string PrefabPath = "Assets/Prefabs/Combat/PlayerSlash.prefab";
    private const string SwordAssetPath = "Assets/Data/Weapons/Sword.asset";

    static PlayerSlashPrefabBootstrap()
    {
        EditorApplication.delayCall += EnsureAssetsExist;
    }

    [MenuItem("Tools/TopDownRPG/Rebuild Player Slash Prefab")]
    public static void RebuildPrefab()
    {
        PlayerSlashAttack slashPrefab = CreateOrUpdatePrefab();
        LinkSwordWeapon(slashPrefab);
    }

    private static void EnsureAssetsExist()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
        {
            return;
        }

        GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        PlayerSlashAttack slashPrefab = prefabRoot != null ? prefabRoot.GetComponent<PlayerSlashAttack>() : null;
        if (slashPrefab == null)
        {
            slashPrefab = CreateOrUpdatePrefab();
        }

        LinkSwordWeapon(slashPrefab);
    }

    private static PlayerSlashAttack CreateOrUpdatePrefab()
    {
        string directoryPath = Path.Combine(Application.dataPath, "Prefabs", "Combat");
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        GameObject root = BuildPrefabRoot();
        GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        return prefabAsset != null ? prefabAsset.GetComponent<PlayerSlashAttack>() : null;
    }

    private static GameObject BuildPrefabRoot()
    {
        GameObject root = new("PlayerSlash");
        root.AddComponent<Animator>();

        BoxCollider2D hitbox = root.AddComponent<BoxCollider2D>();
        hitbox.isTrigger = true;
        hitbox.offset = new Vector2(0f, 0.1f);
        hitbox.size = new Vector2(0.9f, 0.9f);

        root.AddComponent<PlayerSlashAttack>();

        GameObject visual = new("Visual");
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = Vector3.one * 0.14f;
        visual.AddComponent<SpriteRenderer>();

        return root;
    }

    private static void LinkSwordWeapon(PlayerSlashAttack slashPrefab)
    {
        if (slashPrefab == null)
        {
            return;
        }

        PlayerWeapon swordWeapon = AssetDatabase.LoadAssetAtPath<PlayerWeapon>(SwordAssetPath);
        if (swordWeapon == null)
        {
            return;
        }

        SerializedObject serializedWeapon = new(swordWeapon);
        SerializedProperty prefabProperty = serializedWeapon.FindProperty("slashPrefab");
        if (prefabProperty == null || prefabProperty.objectReferenceValue == slashPrefab)
        {
            return;
        }

        prefabProperty.objectReferenceValue = slashPrefab;
        serializedWeapon.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(swordWeapon);
        AssetDatabase.SaveAssetIfDirty(swordWeapon);
    }
}

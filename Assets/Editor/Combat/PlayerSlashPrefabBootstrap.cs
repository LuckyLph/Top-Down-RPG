using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor bootstrap that builds the sword slash prefab and links it to the Sword weapon asset, automatically after
/// each domain load and on demand from the Tools menu.
/// </summary>
[InitializeOnLoad]
public static class PlayerSlashPrefabBootstrap
{
    private const string PrefabPath = "Assets/Prefabs/Combat/SwordSlash.prefab";
    private const string SwordAssetPath = "Assets/Data/Weapons/Sword.asset";
    private const string SlashAnimationPath = "Assets/Animations/Weapons/Slash.anim";

    /// <summary>
    /// Defers <see cref="EnsureAssetsExist"/> until the editor has finished loading.
    /// </summary>
    static PlayerSlashPrefabBootstrap()
    {
        EditorApplication.delayCall += EnsureAssetsExist;
    }

    /// <summary>
    /// Rebuilds the sword slash prefab from scratch, overwriting the existing asset, and links it to the Sword weapon.
    /// </summary>
    [MenuItem("Tools/TopDownRPG/Rebuild Player Slash Prefab")]
    public static void RebuildPrefab()
    {
        GameObject slashPrefab = CreateOrUpdatePrefab();
        LinkSwordWeapon(slashPrefab);
    }

    /// <summary>
    /// Creates the slash prefab when it is missing or has no <see cref="SwordSlashAttack"/>, then links it to the Sword
    /// weapon. Does nothing while the editor is compiling, importing or building a player.
    /// </summary>
    private static void EnsureAssetsExist()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
        {
            return;
        }

        GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefabRoot == null || prefabRoot.GetComponent<SwordSlashAttack>() == null)
        {
            prefabRoot = CreateOrUpdatePrefab();
        }

        LinkSwordWeapon(prefabRoot);
    }

    /// <summary>
    /// Builds a fresh slash hierarchy, saves it over the prefab path (creating the folder if needed), destroys the
    /// temporary scene object and returns the saved prefab asset.
    /// </summary>
    private static GameObject CreateOrUpdatePrefab()
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
        return prefabAsset;
    }

    /// <summary>
    /// Creates the temporary slash hierarchy: a root with an <see cref="Animator"/>, a trigger hitbox and a
    /// <see cref="SwordSlashAttack"/>, plus a scaled Visual child holding the sprite, wired to the Slash clip.
    /// </summary>
    private static GameObject BuildPrefabRoot()
    {
        GameObject root = new("SwordSlash");
        Animator animator = root.AddComponent<Animator>();

        BoxCollider2D hitbox = root.AddComponent<BoxCollider2D>();
        hitbox.isTrigger = true;
        hitbox.offset = new Vector2(0f, 0.1f);
        hitbox.size = new Vector2(0.9f, 0.9f);

        GameObject visual = new("Visual");
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = Vector3.one * 0.14f;
        SpriteRenderer spriteRenderer = visual.AddComponent<SpriteRenderer>();

        SwordSlashAttack slashAttack = root.AddComponent<SwordSlashAttack>();
        AnimationClip slashAnimation = AssetDatabase.LoadAssetAtPath<AnimationClip>(SlashAnimationPath);
        slashAttack.ConfigureReferences(visual.transform, spriteRenderer, hitbox, animator, slashAnimation);

        return root;
    }

    /// <summary>
    /// Points the Sword weapon asset's slash prefab field at <paramref name="slashPrefab"/> and saves the asset. Does
    /// nothing when either asset is missing or the link is already in place.
    /// </summary>
    private static void LinkSwordWeapon(GameObject slashPrefab)
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

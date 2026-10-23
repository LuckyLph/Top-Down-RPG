using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// An editor-only recipe for a character drawn by hand in Aseprite (Docs/AsepriteCharacters.md): its name, which names
/// its files (<c>&lt;name&gt;_&lt;clip&gt;.aseprite</c> under <see cref="ArtRoot"/>) and its sheets, slices and
/// animation set as the baker's are named (<see cref="CharacterSheetWriter"/>); the prefab drawn with the set, whose
/// weapons, abilities or mob attack name the clips it needs (<see cref="CharacterClipNeeds"/>); and its clips, one file
/// each (<see cref="AsepriteClip"/>). <see cref="AsepriteCharacterWriter"/> copies the files' frames into sheets and
/// writes the set; the inspector previews a clip and generates.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Characters/Aseprite Character Recipe", fileName = "AsepriteRecipe")]
public sealed class AsepriteCharacterRecipe : ScriptableObject, IAsepriteRecipe
{
    public const string ArtRoot = "Assets/Art/Characters";
    public const string Extension = ".aseprite";

    [Tooltip("The character's name: its files are <name>_<clip>.aseprite, its sheets and animation set go under Assets/Sprites/Characters/<name>/. Letters and digits only, starting with a letter.")]
    [SerializeField] private string characterName = "Bandit";
    [Tooltip("The prefab drawn with the set, if any: Generate lists the clips its class's and weapons' abilities, its weapons' attacks or its mob attack need and the set lacks.")]
    [SerializeField] private GameObject character;
    [Tooltip("The clips, one .aseprite file each.")]
    [SerializeField] private List<AsepriteClip> clips = new();

    public string CharacterName => characterName;
    public GameObject Character => character;
    public IReadOnlyList<AsepriteClip> Clips => clips;
    public IEnumerable<string> FilePaths => clips.Where(c => c?.FilePath != null).Select(c => c.FilePath);

    /// <summary>
    /// Creates an unsaved recipe for the character <paramref name="name"/> drawn with <paramref name="prefab"/> (or
    /// none), for tests and tools.
    /// </summary>
    public static AsepriteCharacterRecipe Create(string name, GameObject prefab, IEnumerable<AsepriteClip> recipeClips)
    {
        AsepriteCharacterRecipe recipe = CreateInstance<AsepriteCharacterRecipe>();
        recipe.characterName = name;
        recipe.character = prefab;
        recipe.clips = new List<AsepriteClip>(recipeClips);
        return recipe;
    }

    /// <summary>
    /// The name of <paramref name="clip"/>, the part of its file's name after <c>&lt;character&gt;_</c>, or null when
    /// it has no <c>.aseprite</c> file named so with a clip name of letters, digits and underscores.
    /// </summary>
    public string ClipName(AsepriteClip clip)
    {
        string path = clip?.FilePath;
        if (string.IsNullOrEmpty(path) || !path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string fileName = Path.GetFileNameWithoutExtension(path);
        string prefix = characterName + "_";
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string name = fileName.Substring(prefix.Length);
        return CharacterRecipe.IsName(name, true) ? name : null;
    }

    /// <summary>
    /// Whether the recipe can generate: a name of letters and digits, at least one clip, and every clip with an
    /// <c>.aseprite</c> file under <see cref="ArtRoot"/> named <c>&lt;character&gt;_&lt;clip&gt;</c>, listed once, a
    /// walk, run or sprint with its natural speed, and its feet pixel not below or left of the canvas (whether it lies
    /// inside is the file's to say). Returns the reason in <paramref name="problem"/> when it cannot.
    /// </summary>
    public bool CanGenerate(out string problem)
    {
        problem = null;
        if (!CharacterRecipe.IsName(characterName, false))
        {
            problem = "the character's name must be letters and digits, starting with a letter.";
            return false;
        }

        if (clips.Count == 0)
        {
            problem = "the recipe has no clips.";
            return false;
        }

        HashSet<string> names = new();
        for (int i = 0; i < clips.Count; i++)
        {
            AsepriteClip clip = clips[i];
            string name = ClipName(clip);
            if (name == null)
            {
                problem = $"clip {i + 1} needs an {Extension} file named {characterName}_<clip>{Extension}, the clip's name letters, digits and underscores.";
                return false;
            }

            if (!clip.FilePath.StartsWith(ArtRoot + "/", StringComparison.Ordinal))
            {
                problem = $"clip {name}'s file must be under {ArtRoot}, where its import settings are kept.";
                return false;
            }

            if (!names.Add(name))
            {
                problem = $"clip {name} is listed twice.";
                return false;
            }

            if (clip.Moves && clip.NaturalSpeed <= 0f)
            {
                problem = $"clip {name} is a {clip.Gait.ToString().ToLowerInvariant()} and needs its natural speed.";
                return false;
            }

            if (clip.Feet.x < 0 || clip.Feet.y < 0)
            {
                problem = $"clip {name}'s feet pixel cannot be negative.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Writes the clips' sheets and the animation set (<see cref="AsepriteCharacterWriter.Generate"/>).
    /// </summary>
    public AsepriteReport Generate()
    {
        return AsepriteCharacterWriter.Generate(this);
    }
}

/// <summary>
/// One clip of an <see cref="AsepriteCharacterRecipe"/>: its <c>.aseprite</c> file, the gait it shows (none for an
/// attack or a cast), whether it is drawn with a weapon, a walk's, run's or sprint's natural speed (how fast its
/// planted feet travel over the ground at the file's frame durations, east or west, in units per second), and the
/// pixel the feet stand on in every frame, from the canvas's bottom left.
/// </summary>
[Serializable]
public sealed class AsepriteClip
{
    [Tooltip("The clip's file, <character>_<clip>.aseprite: eight tags E, NE, N, NW, W, SW, S and SE over the same number of frames with the same durations; an attack or a cast marks its contact (release) frame with the user data \"contact\" on a cel, in every direction.")]
    [SerializeField] private Object file;
    [Tooltip("The gait the clip shows, or None for an attack or a cast.")]
    [SerializeField] private CharacterGait gait;
    [Tooltip("Whether the clip is drawn with a weapon: an attack or a weapon's cast. A gait drawn armed is never picked for locomotion.")]
    [SerializeField] private bool armed;
    [Tooltip("For a walk, run or sprint: how fast its planted feet travel over the ground at the file's frame durations, east or west, in units per second.")]
    [SerializeField, Min(0f)] private float naturalSpeed;
    [Tooltip("The pixel the feet stand on in every frame, from the canvas's bottom left: the sprites' pivot.")]
    [SerializeField] private Vector2Int feet = new(32, 12);

    /// <summary>
    /// Creates an empty clip, for Unity serialization and the inspector.
    /// </summary>
    public AsepriteClip()
    {
    }

    /// <summary>
    /// Creates a clip, for tests and tools.
    /// </summary>
    public AsepriteClip(Object clipFile, CharacterGait clipGait, bool drawnArmed, float clipNaturalSpeed, Vector2Int clipFeet)
    {
        file = clipFile;
        gait = clipGait;
        armed = drawnArmed;
        naturalSpeed = clipNaturalSpeed;
        feet = clipFeet;
    }

    public Object File => file;
    public string FilePath => file != null ? AssetDatabase.GetAssetPath(file) : null;
    public CharacterGait Gait => gait;
    public bool Armed => armed;
    public float NaturalSpeed => naturalSpeed;
    public Vector2Int Feet => feet;

    /// <summary>
    /// Whether the clip is a walk, run or sprint, played at a rate from its natural speed.
    /// </summary>
    public bool Moves => gait == CharacterGait.Walk || gait == CharacterGait.Run || gait == CharacterGait.Sprint;
}

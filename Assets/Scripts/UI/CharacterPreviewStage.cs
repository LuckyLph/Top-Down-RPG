using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The Character screen's preview stage (Docs/UiRevamp.md, Phase 7): the local player's character drawn into a texture
/// of <see cref="Size"/> art pixels a side, which the screen shows at a whole multiple of its pixels. Its
/// <see cref="PlayerLook"/> follows the local settings while shown, so its outfit is composed as in game by the scope's
/// <see cref="CharacterOutfits"/> (in game the local player's own, shared), and its <see cref="CharacterVisuals"/>
/// animate on an <see cref="UnscaledClock"/>, so the character moves while the game is paused. Nothing draws it but this
/// stage: its sprite renderer stays off, and each frame while shown a command buffer draws it with the sprite shader's
/// unlit pass into the texture, its feet <see cref="FeetRow"/> pixels above the bottom, so no camera, light, sun shadow
/// or day and night reaches it and it shows the sheets' own colours exactly. It turns through the 8 directions and
/// stands, runs at the middle of the run's speeds, or attacks with the shown weapon's attacks in turn, pausing between
/// them. Runs after the visuals each frame, so it draws the sprite they just showed.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class CharacterPreviewStage : MonoBehaviour, ICharacterPreview
{
    public const int Size = 80;
    public const int FeetRow = 24;
    public const int South = 6;
    private const float ArtPixelsPerUnit = 32f;
    private const float CameraDistance = 10f;
    private const string LightMode = "LightMode";
    private const string UnlitLightMode = "UniversalForward";

    [SerializeField, Tooltip("The character's visuals, on an inactive child whose sprite renderer stays off: the stage draws them itself.")]
    private CharacterVisuals visuals;

    [SerializeField, Tooltip("The look the character is drawn in, following the local settings while the stage shows.")]
    private PlayerLook look;

    [SerializeField, Tooltip("The weapon whose gear and attacks show where no weapon is given: the main menu's, the starting weapon.")]
    private PlayerWeapon startingWeapon;

    [SerializeField, Min(0f), Tooltip("Seconds the character stands between two attacks in the attacking pose.")]
    private float attackPause = 0.5f;

    private IClock clock = new UnscaledClock();
    private RenderTexture texture;
    private CommandBuffer commands;
    private SpriteRenderer body;
    private PlayerWeapon weapon;
    private int unlitPass = -1;
    private int attack;
    private float nextAttack;
    private bool shown;

    public Texture Texture => texture;
    public int Direction { get; private set; } = South;
    public CharacterPreviewPose Pose { get; private set; }
    public bool IsShown => shown;
    internal PlayerLook Look => look;
    internal CharacterVisuals Visuals => visuals;

    /// <summary>
    /// Fails loudly without its visuals or look.
    /// </summary>
    private void Awake()
    {
        if (visuals == null || look == null || startingWeapon == null)
        {
            Debug.LogError($"{name} needs its character's visuals, its look and a starting weapon.", this);
        }
    }

    /// <summary>
    /// Releases the texture and the command buffer.
    /// </summary>
    private void OnDestroy()
    {
        commands?.Release();
        commands = null;
        if (texture != null)
        {
            texture.Release();
            Destroy(texture);
        }
    }

    /// <summary>
    /// Starts drawing the character facing south and standing, its visuals on the unscaled clock, following the local
    /// settings in the gear and with the attacks of <paramref name="shownWeapon"/>, the starting weapon for none.
    /// </summary>
    public void Show(PlayerWeapon shownWeapon)
    {
        if (visuals == null || look == null)
        {
            return;
        }

        weapon = shownWeapon != null ? shownWeapon : startingWeapon;
        Direction = South;
        Pose = CharacterPreviewPose.Standing;
        attack = 0;
        EnsureTexture();
        visuals.Construct(clock);
        visuals.gameObject.SetActive(true);
        look.ShowWeapon(weapon);
        look.FollowLocalChoice();
        shown = true;
    }

    /// <summary>
    /// Stops drawing the character and following the local settings.
    /// </summary>
    public void Hide()
    {
        if (!shown)
        {
            return;
        }

        shown = false;
        look.StopFollowing();
        visuals.gameObject.SetActive(false);
    }

    /// <summary>
    /// Turns the character <paramref name="steps"/> directions, toward the screen's left for a negative count.
    /// </summary>
    public void Turn(int steps)
    {
        Direction = ((Direction + steps) % CharacterDirections.Count + CharacterDirections.Count) % CharacterDirections.Count;
    }

    /// <summary>
    /// Shows the next pose; an attack showing stops when the attacking pose ends, and the first attack plays at once
    /// when it begins.
    /// </summary>
    public void NextPose()
    {
        if (Pose == CharacterPreviewPose.Attacking && visuals != null)
        {
            visuals.StopAction(CharacterActionKind.Attack);
        }

        Pose = (CharacterPreviewPose)(((int)Pose + 1) % 3);
        attack = 0;
        nextAttack = clock.Time;
    }

    /// <summary>
    /// Shows the local settings' colours with <paramref name="choice"/> in the swatch named <paramref name="swatch"/>.
    /// </summary>
    public void PreviewColour(int choice, string swatch)
    {
        if (look != null && look.Colours != null)
        {
            look.ShowColours(look.Colours.Pick(look.LocalColours(), choice, swatch));
        }
    }

    /// <summary>
    /// Shows the local settings' colours again.
    /// </summary>
    public void ShowPickedColours()
    {
        if (look != null)
        {
            look.ShowColours(look.LocalColours());
        }
    }

    /// <summary>
    /// Gives the visuals the frame's movement before they animate: still, or running toward the facing, and the next
    /// attack in the attacking pose.
    /// </summary>
    private void Update()
    {
        if (shown)
        {
            Move();
        }
    }

    /// <summary>
    /// Draws the sprite the visuals showed this frame.
    /// </summary>
    private void LateUpdate()
    {
        if (shown)
        {
            Draw();
        }
    }

    /// <summary>
    /// Sets the visuals' movement for the pose and the direction, and starts the shown weapon's next attack once the
    /// last has ended and the pause after it has passed.
    /// </summary>
    internal void Move()
    {
        float angle = CharacterDirections.Angle(Direction) * Mathf.Deg2Rad;
        Vector2 facing = new(Mathf.Cos(angle), Mathf.Sin(angle));
        CharacterLocomotionSettings locomotion = visuals.Locomotion;
        float speed = Pose == CharacterPreviewPose.Running && locomotion != null ? (locomotion.RunFrom + locomotion.SprintFrom) * 0.5f : 0f;
        visuals.SetLocomotion(facing * speed, facing);
        if (Pose != CharacterPreviewPose.Attacking || weapon == null || clock.Time < nextAttack)
        {
            return;
        }

        CharacterAnimator animator = visuals.Animator;
        CharacterAnimationSet set = visuals.AnimationSet;
        if (animator == null || set == null || animator.IsActing)
        {
            return;
        }

        CharacterAnimationClip clip = set.Find(weapon.AttackClipOf(attack)) ?? set.Find(weapon.AttackClip);
        attack = (attack + 1) % weapon.AttackCount;
        if (clip == null)
        {
            return;
        }

        AttackTimeline timeline = weapon.Timeline;
        visuals.PlayAction(clip, facing, timeline, false);
        nextAttack = clock.Time + timeline.Duration + attackPause;
    }

    /// <summary>
    /// Draws the visuals' sprite into the texture with the sprite shader's unlit pass, the stage's own transform
    /// undone, so wherever it sits (under a canvas, scaled) the character is drawn at one texel an art pixel.
    /// </summary>
    internal void Draw()
    {
        if (body == null)
        {
            body = visuals.GetComponent<SpriteRenderer>();
        }

        Material material = body != null ? body.sharedMaterial : null;
        if (material == null || body.sprite == null || texture == null)
        {
            return;
        }

        if (unlitPass < 0 || unlitPass >= material.passCount)
        {
            unlitPass = UnlitPassOf(material.shader);
        }

        commands ??= new CommandBuffer { name = "Character preview" };
        commands.Clear();
        commands.SetRenderTarget(texture);
        commands.ClearRenderTarget(false, true, Color.clear);
        float half = Size * 0.5f / ArtPixelsPerUnit;
        Vector3 eye = new(0f, (Size * 0.5f - FeetRow) / ArtPixelsPerUnit, -CameraDistance);
        Matrix4x4 view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.Translate(-eye) * transform.worldToLocalMatrix;
        commands.SetViewProjectionMatrices(view, Matrix4x4.Ortho(-half, half, -half, half, 0.01f, CameraDistance * 2f));
        commands.DrawRenderer(body, material, 0, Mathf.Max(0, unlitPass));
        Graphics.ExecuteCommandBuffer(commands);
    }

    /// <summary>
    /// Makes the texture the character is drawn into, once.
    /// </summary>
    private void EnsureTexture()
    {
        if (texture != null)
        {
            return;
        }

        texture = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            name = $"{name} Preview",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.Create();
    }

    /// <summary>
    /// The index of <paramref name="shader"/>'s unlit pass (light mode UniversalForward), or -1.
    /// </summary>
    private static int UnlitPassOf(Shader shader)
    {
        ShaderTagId lightMode = new(LightMode);
        ShaderTagId unlit = new(UnlitLightMode);
        for (int i = 0; shader != null && i < shader.passCount; i++)
        {
            if (shader.FindPassTagValue(i, lightMode) == unlit)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Test setup without the prefab: sets the visuals, the look, the starting weapon and the clock.
    /// </summary>
    internal void Configure(CharacterVisuals characterVisuals, PlayerLook characterLook, PlayerWeapon weaponShown, IClock stageClock)
    {
        visuals = characterVisuals;
        look = characterLook;
        startingWeapon = weaponShown;
        clock = stageClock;
    }
}

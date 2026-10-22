using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How a unit shows the hits it takes and lands (Docs/Animation.md, Phase 6), played by
/// <see cref="HitFeedbackPresenter"/> from the hits each machine learns of. Its body sprite flashes through a property
/// block on its flash-capable material (<c>TopDownRPG/Sprite Lit Flash</c>); its animation holds, through its
/// <see cref="CharacterVisuals"/> when it has them, or else, for a sprite Mecanim animates, by keeping the sprite on
/// screen; and the transform holding the sprite is nudged by whole pixels away from the hit and back, while the body and
/// its colliders stay put. Visual only, timed on <see cref="Time.deltaTime"/>, each from the frame it starts; nothing
/// shows while the unit is disabled (dead), and disabling puts everything back. Allocates nothing.
/// </summary>
[DisallowMultipleComponent]
public class HitFeedback : MonoBehaviour
{
    private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
    private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");

    [SerializeField, Tooltip("The body's sprite renderer, drawn with a material that can flash (TopDownRPG/Sprite Lit Flash).")]
    private SpriteRenderer body;
    [SerializeField, Tooltip("The transform holding the body's sprite, nudged by whole pixels; never the root, so the body and its colliders stay put.")]
    private Transform visuals;
    [SerializeField, Tooltip("The character animator's visuals, which hold the animation; empty for a sprite Mecanim animates, which holds by keeping its sprite.")]
    private CharacterVisuals characterVisuals;
    [SerializeField, Tooltip("The body's collider: an impact lands where the line from the hit's source to its middle enters it.")]
    private Collider2D bodyCollider;
    [SerializeField, Min(0f), Tooltip("How high above the ground point an impact shows, in units: about the body's middle.")]
    private float impactHeight = 0.7f;

    private MaterialPropertyBlock block;
    private Vector3 restPosition;
    private float flashLeft;
    private bool flashFresh;
    private IReadOnlyList<int> nudgeSteps;
    private float nudgeStepSeconds;
    private float nudgeElapsed;
    private bool nudgeFresh;
    private int nudgePixelsPerUnit = 32;
    private bool nudging;
    private Sprite heldSprite;
    private float holdLeft;
    private bool holdFresh;

    public float ImpactHeight => impactHeight;
    public SpriteRenderer Body => body;
    public Transform Visuals => visuals;

    internal bool IsFlashing => flashLeft > 0f;
    internal Color FlashColor { get; private set; }
    internal float FlashAmount { get; private set; }
    internal int FlashCount { get; private set; }
    internal Vector2 LastNudgeDirection { get; private set; }
    internal int NudgeCount { get; private set; }
    internal int HoldCount { get; private set; }

    /// <summary>
    /// Where the sprite sits off its rest place now, in units.
    /// </summary>
    internal Vector2 NudgeOffset => visuals != null ? (Vector2)(visuals.localPosition - restPosition) : Vector2.zero;

    /// <summary>
    /// Whether the unit's animation holds now.
    /// </summary>
    internal bool IsHolding => characterVisuals != null ? characterVisuals.Animator != null && characterVisuals.Animator.IsHolding : holdLeft > 0f;

    /// <summary>
    /// Whether the unit shows hits now: enabled on an active object (a dead player's is disabled).
    /// </summary>
    private bool IsShowing => enabled && gameObject.activeInHierarchy;

    /// <summary>
    /// Keeps the sprite's rest place and fails loudly on a missing renderer or transform, or a material that cannot
    /// flash.
    /// </summary>
    private void Awake()
    {
        if (body == null || visuals == null)
        {
            Debug.LogError($"{name} needs its body renderer and the transform holding it to show hits.", this);
            return;
        }

        restPosition = visuals.localPosition;
        if (body.sharedMaterial == null || !body.sharedMaterial.HasProperty(FlashAmountId))
        {
            Debug.LogError($"{name}'s body material cannot flash; give it the TopDownRPG/Sprite Lit Flash shader.", this);
        }
    }

    /// <summary>
    /// Moves the flash, nudge and hold on once the frame's animation has run.
    /// </summary>
    private void LateUpdate()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// Puts the sprite, its place and its material back.
    /// </summary>
    private void OnDisable()
    {
        Restore();
    }

    /// <summary>
    /// Mixes the body <paramref name="amount"/> of the way to <paramref name="colour"/>, after lighting, for
    /// <paramref name="seconds"/>, replacing any flash showing. Nothing while disabled.
    /// </summary>
    public void Flash(Color colour, float amount, float seconds)
    {
        if (!IsShowing || body == null || seconds <= 0f || amount <= 0f)
        {
            return;
        }

        flashLeft = seconds;
        flashFresh = true;
        FlashColor = colour;
        FlashAmount = amount;
        FlashCount++;
        block ??= new MaterialPropertyBlock();
        body.GetPropertyBlock(block);
        block.SetColor(FlashColorId, colour);
        block.SetFloat(FlashAmountId, amount);
        body.SetPropertyBlock(block);
    }

    /// <summary>
    /// Holds the animation for <paramref name="seconds"/> (a hit-stop): through the character animator, or by keeping
    /// the sprite showing now. Nothing while disabled.
    /// </summary>
    public void Hold(float seconds)
    {
        if (!IsShowing || seconds <= 0f)
        {
            return;
        }

        HoldCount++;
        if (characterVisuals != null)
        {
            characterVisuals.Hold(seconds);
            return;
        }

        if (body == null)
        {
            return;
        }

        if (holdLeft <= 0f)
        {
            heldSprite = body.sprite;
        }

        holdLeft = Mathf.Max(holdLeft, seconds);
        holdFresh = true;
    }

    /// <summary>
    /// Holds the attack's contact frame for <paramref name="seconds"/> when the character shows it on its next update
    /// (an attacker whose hit just landed); returns whether it holds. A sprite Mecanim animates never holds this way.
    /// </summary>
    public bool HoldContact(float seconds)
    {
        if (!IsShowing || characterVisuals == null || !characterVisuals.HoldContact(seconds))
        {
            return false;
        }

        HoldCount++;
        return true;
    }

    /// <summary>
    /// Moves the sprite away along <paramref name="direction"/> by each of <paramref name="stepPixels"/> whole pixels
    /// (at <paramref name="pixelsPerUnit"/>) for <paramref name="stepSeconds"/> in turn, then back. Nothing while
    /// disabled, or without a direction or steps.
    /// </summary>
    public void Nudge(Vector2 direction, IReadOnlyList<int> stepPixels, float stepSeconds, int pixelsPerUnit)
    {
        if (!IsShowing || visuals == null || stepPixels == null || stepPixels.Count == 0 || stepSeconds <= 0f || direction.sqrMagnitude < 1e-8f)
        {
            return;
        }

        LastNudgeDirection = direction.normalized;
        NudgeCount++;
        nudgeSteps = stepPixels;
        nudgeStepSeconds = stepSeconds;
        nudgePixelsPerUnit = Mathf.Max(1, pixelsPerUnit);
        nudgeElapsed = 0f;
        nudgeFresh = true;
        nudging = true;
        PlaceNudge(0);
    }

    /// <summary>
    /// The ground point a hit from <paramref name="source"/> lands on: where the line from it to the middle of the body
    /// collider's bounds enters them, as an attack aimed at the unit, or a homing arrow, meets it; the middle without a
    /// source or from inside; the unit's position without an enabled collider.
    /// </summary>
    public Vector2 ImpactGround(Vector2 source, bool hasSource)
    {
        if (bodyCollider == null || !bodyCollider.enabled)
        {
            return transform.position;
        }

        Bounds bounds = bodyCollider.bounds;
        Vector2 middle = bounds.center;
        Vector2 toMiddle = middle - source;
        if (!hasSource || bounds.Contains(new Vector3(source.x, source.y, bounds.center.z)))
        {
            return middle;
        }

        float enter = 0f;
        for (int axis = 0; axis < 2; axis++)
        {
            float along = toMiddle[axis];
            if (Mathf.Abs(along) < 1e-6f)
            {
                continue;
            }

            float near = ((along > 0f ? bounds.min[axis] : bounds.max[axis]) - source[axis]) / along;
            enter = Mathf.Max(enter, near);
        }

        return source + toMiddle * Mathf.Clamp01(enter);
    }

    /// <summary>
    /// Moves the flash, nudge and hold on by <paramref name="deltaTime"/> seconds; one started since the last tick
    /// shows this frame whole.
    /// </summary>
    internal void Tick(float deltaTime)
    {
        if (flashLeft > 0f)
        {
            if (flashFresh)
            {
                flashFresh = false;
            }
            else
            {
                flashLeft -= deltaTime;
                if (flashLeft <= 0f)
                {
                    ClearFlash();
                }
            }
        }

        if (nudging)
        {
            if (nudgeFresh)
            {
                nudgeFresh = false;
            }
            else
            {
                nudgeElapsed += deltaTime;
            }

            int step = Mathf.FloorToInt(nudgeElapsed / nudgeStepSeconds);
            if (step >= nudgeSteps.Count)
            {
                nudging = false;
                visuals.localPosition = restPosition;
            }
            else
            {
                PlaceNudge(step);
            }
        }

        if (holdLeft > 0f)
        {
            if (holdFresh)
            {
                holdFresh = false;
            }
            else
            {
                holdLeft -= deltaTime;
            }

            if (holdLeft > 0f && heldSprite != null && body != null)
            {
                body.sprite = heldSprite;
            }
            else if (holdLeft <= 0f)
            {
                heldSprite = null;
            }
        }
    }

    /// <summary>
    /// Sets the renderer, sprite transform, character visuals and collider in code, for tests that build a unit without
    /// its prefab, and keeps the sprite's rest place.
    /// </summary>
    internal void Configure(SpriteRenderer bodyRenderer, Transform sprite, CharacterVisuals character, Collider2D collider, float height)
    {
        body = bodyRenderer;
        visuals = sprite;
        characterVisuals = character;
        bodyCollider = collider;
        impactHeight = height;
        restPosition = visuals != null ? visuals.localPosition : Vector3.zero;
    }

    /// <summary>
    /// Puts the sprite <paramref name="step"/>'s whole pixels away from its rest place along the nudge.
    /// </summary>
    private void PlaceNudge(int step)
    {
        float pixels = nudgeSteps[step];
        Vector2 offset = new(Mathf.Round(LastNudgeDirection.x * pixels), Mathf.Round(LastNudgeDirection.y * pixels));
        visuals.localPosition = restPosition + (Vector3)(offset / nudgePixelsPerUnit);
    }

    /// <summary>
    /// Ends the flash: the body draws as its material does.
    /// </summary>
    private void ClearFlash()
    {
        flashLeft = 0f;
        if (body != null)
        {
            body.SetPropertyBlock(null);
        }
    }

    /// <summary>
    /// Ends the flash, the nudge and the hold at once.
    /// </summary>
    private void Restore()
    {
        if (flashLeft > 0f)
        {
            ClearFlash();
        }

        if (nudging && visuals != null)
        {
            visuals.localPosition = restPosition;
        }

        nudging = false;
        holdLeft = 0f;
        heldSprite = null;
    }
}

using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for <see cref="MobMotor2D"/> feeding the Weasel's baked <see cref="CharacterVisuals"/> on a
/// <see cref="ManualClock"/> (Docs/Animation.md, Phase 7): the gait and direction it shows from the velocity it moves
/// with or a client's replicated move, standing while only crowd separation drifts it, its attack clip on a timeline
/// (held until contact on a copy), holding still at a speed scale of 0, its facing under steering, and no allocation.
/// </summary>
public class MobMotor2DTests
{
    private const string SetPath = "Assets/Sprites/Characters/Weasel/Weasel_AnimationSet.asset";
    private const string LocomotionPath = "Assets/Data/Characters/Locomotion_Weasel.asset";

    private GameObject root;
    private MobConfig config;
    private ManualClock clock;
    private CharacterVisuals visuals;

    /// <summary>
    /// Destroys the mob object and config the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (root != null)
        {
            Object.DestroyImmediate(root);
        }

        if (config != null)
        {
            Object.DestroyImmediate(config);
        }
    }

    /// <summary>
    /// Moving on purpose at its move speed shows the run toward the movement; slowed to 0.6 of it, the walk.
    /// </summary>
    [Test]
    public void FixedTick_ShowsTheGaitOfTheVelocityItMovesWith()
    {
        MobMotor2D motor = CreateMotor();

        motor.SetDesiredVelocity(Vector2.right * motor.MoveSpeed);
        Step(motor, 10);
        Assert.That(visuals.Animator.Gait, Is.EqualTo(CharacterGait.Run));
        Assert.That(visuals.Animator.Direction, Is.EqualTo(CharacterDirections.East));

        motor.SetSpeedScale(0.6f);
        motor.SetDesiredVelocity(Vector2.right * motor.MoveSpeed);
        Step(motor, 10);
        Assert.That(visuals.Animator.Gait, Is.EqualTo(CharacterGait.Walk), "A chilled Weasel walks.");
    }

    /// <summary>
    /// A stopped mob that crowd separation drifts shows itself standing and keeps its facing.
    /// </summary>
    [Test]
    public void FixedTick_ShowsStanding_WhenOnlySteeringDriftsIt()
    {
        MobMotor2D motor = CreateMotor();
        motor.FaceTowards(motor.Position + Vector2.up);
        motor.Stop();
        motor.SetSteeringVelocity(Vector2.left);

        Step(motor, 5);

        Assert.That(root.GetComponent<Rigidbody2D>().linearVelocity.x, Is.LessThan(0f), "The drift itself applies.");
        Assert.That(motor.IsMoving, Is.False);
        Assert.That(motor.CurrentMove, Is.EqualTo(Vector2.zero));
        Assert.That(visuals.Animator.Gait, Is.EqualTo(CharacterGait.Idle));
        Assert.That(visuals.Animator.Direction, Is.EqualTo(CharacterDirections.North));
    }

    /// <summary>
    /// On a client the replicated move, a share of the move speed, picks the gait at the copy's own move speed, and its
    /// facing the direction; a zero move stands.
    /// </summary>
    [Test]
    public void ShowRemoteMovement_ShowsTheReplicatedMove()
    {
        MobMotor2D motor = CreateMotor();

        motor.ShowRemoteMovement(new Vector2(0f, 1f), Vector2.up);
        visuals.Tick();
        Assert.That(motor.IsMoving, Is.True);
        Assert.That(motor.CurrentMove, Is.EqualTo(new Vector2(0f, 1f)));
        Assert.That(visuals.Animator.Gait, Is.EqualTo(CharacterGait.Run));
        Assert.That(visuals.Animator.Direction, Is.EqualTo(CharacterDirections.North));

        clock.Advance(0.1f);
        motor.ShowRemoteMovement(new Vector2(0f, 0.6f), Vector2.up);
        visuals.Tick();
        Assert.That(visuals.Animator.Gait, Is.EqualTo(CharacterGait.Walk), "A slowed host mob walks on the copy too.");

        clock.Advance(0.1f);
        motor.ShowRemoteMovement(Vector2.zero, Vector2.left);
        visuals.Tick();
        Assert.That(motor.IsMoving, Is.False);
        Assert.That(visuals.Animator.Gait, Is.EqualTo(CharacterGait.Idle));
        Assert.That(motor.FacingDirection, Is.EqualTo(Vector2.left));
    }

    /// <summary>
    /// Playing an attack faces its direction and shows the config's attack clip toward it; stopping it ends it.
    /// </summary>
    [Test]
    public void PlayAttack_ShowsTheConfiguredClipTowardTheDirection_UntilStopped()
    {
        MobMotor2D motor = CreateMotor();

        motor.PlayAttack(new Vector2(-3f, 0f), config.AttackTimeline, false);
        visuals.Tick();

        Assert.That(motor.FacingDirection, Is.EqualTo(Vector2.left));
        Assert.That(visuals.Animator.IsActing, Is.True);
        Assert.That(visuals.Animator.Clip.Name, Is.EqualTo("DaggerSlash"));
        Assert.That(visuals.Animator.Direction, Is.EqualTo(CharacterDirections.West));

        motor.StopAttack();
        visuals.Tick();
        Assert.That(visuals.Animator.IsActing, Is.False);
    }

    /// <summary>
    /// A copy's attack, played until contact, holds the windup's last frame past its end until the contact arrives,
    /// then shows the contact frame.
    /// </summary>
    [Test]
    public void PlayAttack_UntilContact_HoldsTheWindupUntilTheContactArrives()
    {
        MobMotor2D motor = CreateMotor();
        motor.PlayAttack(Vector2.down, config.AttackTimeline, true);
        visuals.Tick();
        int contact = visuals.Animator.Clip.ContactFrame;

        clock.Advance(config.AttackTimeline.Windup + 0.2f);
        visuals.Tick();
        Assert.That(visuals.Animator.Frame, Is.EqualTo(contact - 1), "The windup holds its last frame.");

        motor.ShowAttackContact();
        visuals.Tick();
        Assert.That(visuals.Animator.Frame, Is.EqualTo(contact));
    }

    /// <summary>
    /// At a speed scale of 0 (an attack, a root or a stun) the body stops at once, crowd separation included.
    /// </summary>
    [Test]
    public void FixedTick_HoldsTheBodyStill_AtASpeedScaleOfZero()
    {
        MobMotor2D motor = CreateMotor();
        motor.SetDesiredVelocity(Vector2.right * motor.MoveSpeed);
        Step(motor, 10);
        Assert.That(root.GetComponent<Rigidbody2D>().linearVelocity.magnitude, Is.GreaterThan(1f));

        motor.SetSpeedScale(0f);
        motor.SetSteeringVelocity(Vector2.up);
        motor.FixedTick();

        Assert.That(root.GetComponent<Rigidbody2D>().linearVelocity, Is.EqualTo(Vector2.zero));
    }

    /// <summary>
    /// A moving mob faces its desired direction even when steering deflects its actual velocity.
    /// </summary>
    [Test]
    public void FixedTick_FacesIntendedDirection_EvenWhenSteeringDeflectsTheMob()
    {
        MobMotor2D motor = CreateMotor();

        motor.SetDesiredVelocity(Vector2.up * 2f);
        motor.SetSteeringVelocity(Vector2.left * 1.5f);
        motor.FixedTick();

        Assert.That(motor.FacingDirection.y, Is.GreaterThan(0.99f));
    }

    /// <summary>
    /// A config naming a clip the visuals' set lacks logs an error.
    /// </summary>
    [Test]
    public void Initialize_LogsAnError_WhenTheSetLacksTheAttackClip()
    {
        MobMotor2D motor = CreateMotor();
        MobConfig missing = MobConfig.Create(attackClip: "NoSuchClip");

        LogAssert.Expect(LogType.Error, "Mob's CharacterAnimationSet has no clip 'NoSuchClip' for its attack.");
        motor.Initialize(missing);
        Object.DestroyImmediate(missing);
    }

    /// <summary>
    /// Once warm, moving, showing replicated movement and attacking allocate nothing.
    /// </summary>
    [Test]
    public void MovingAndAttacking_AllocateNothing_OnceWarm()
    {
        MobMotor2D motor = CreateMotor();
        motor.SetDesiredVelocity(Vector2.right * motor.MoveSpeed);
        Step(motor, 3);
        motor.PlayAttack(Vector2.left, config.AttackTimeline, false);
        visuals.Tick();
        motor.StopAttack();
        visuals.Tick();

        Assert.That(() =>
        {
            motor.FixedTick();
            clock.Advance(0.02f);
            visuals.Tick();
            motor.ShowRemoteMovement(Vector2.right, Vector2.right);
            motor.PlayAttack(Vector2.left, config.AttackTimeline, false);
            visuals.Tick();
            motor.StopAttack();
        }, Is.Not.AllocatingGCMemory());
    }

    /// <summary>
    /// Steps the motor <paramref name="ticks"/> physics steps, each followed by the visuals' update on the clock.
    /// </summary>
    private void Step(MobMotor2D motor, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            motor.FixedTick();
            clock.Advance(Time.fixedDeltaTime);
            visuals.Tick();
        }
    }

    /// <summary>
    /// Creates a mob with a gravity-free rigidbody, a Visuals child drawing the Weasel's baked set with its locomotion
    /// settings on a manual clock, and a <see cref="MobMotor2D"/> initialised from a config moving at 2 units per
    /// second and naming the Weasel's attack clip.
    /// </summary>
    private MobMotor2D CreateMotor()
    {
        root = new GameObject("Mob");
        Rigidbody2D rb = root.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        GameObject child = new("Visuals");
        child.transform.SetParent(root.transform, false);
        child.AddComponent<SpriteRenderer>();
        visuals = child.AddComponent<CharacterVisuals>();
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(SetPath);
        CharacterLocomotionSettings locomotion = AssetDatabase.LoadAssetAtPath<CharacterLocomotionSettings>(LocomotionPath);
        Assert.That(set, Is.Not.Null);
        Assert.That(locomotion, Is.Not.Null);
        SerializedObject serialized = new(visuals);
        serialized.FindProperty("animationSet").objectReferenceValue = set;
        serialized.FindProperty("locomotion").objectReferenceValue = locomotion;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        clock = new ManualClock();
        visuals.Construct(clock);

        MobMotor2D motor = root.AddComponent<MobMotor2D>();
        config = MobConfig.Create(moveSpeed: 2f, attackClip: "DaggerSlash");
        motor.Initialize(config);
        return motor;
    }
}

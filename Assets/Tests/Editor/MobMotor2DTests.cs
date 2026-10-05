using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for <see cref="MobMotor2D"/> with the weasel animator: attack animation flags, timing and events,
/// replicated movement display, allocation, and facing under steering.
/// </summary>
public class MobMotor2DTests
{
    private GameObject root;

    /// <summary>
    /// Destroys the mob object the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (root != null)
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// Playing an attack with an animator that supports it marks the attack animation active and faces the attack
    /// direction.
    /// </summary>
    [Test]
    public void PlayAttackAnimation_SetsAnimatorAttackFlag()
    {
        MobMotor2D motor = CreateMotorWithWeaselAnimator();

        motor.PlayAttackAnimation(Vector2.right);

        Assert.That(motor.SupportsAttackAnimation, Is.True);
        Assert.That(motor.IsAttackAnimationActive, Is.True);

        Vector2 lastMoveDirection = motor.FacingDirection;
        Assert.That(lastMoveDirection.x, Is.GreaterThan(0.9f));
        Assert.That(lastMoveDirection.y, Is.EqualTo(0f).Within(0.001f));
    }

    /// <summary>
    /// The attack animation stays active until the injected clock passes its duration.
    /// </summary>
    [Test]
    public void AttackAnimation_EndsOnTheInjectedClock()
    {
        MobMotor2D motor = CreateMotorWithWeaselAnimator();
        ManualClock clock = new();
        motor.Construct(clock);

        motor.PlayAttackAnimation(Vector2.right);
        motor.UpdateAttackAnimation();
        Assert.That(motor.IsAttackAnimationActive, Is.True);

        clock.Advance(10f);
        motor.UpdateAttackAnimation();
        Assert.That(motor.IsAttackAnimationActive, Is.False);
    }

    /// <summary>
    /// Replicated movement sets the moving state and facing, and the moving state survives an attack animation
    /// ending while the facing follows the attack.
    /// </summary>
    [Test]
    public void ShowRemoteMovement_DrivesMovingAndFacing_AndSurvivesTheEndOfAnAttack()
    {
        MobMotor2D motor = CreateMotorWithWeaselAnimator();
        ManualClock clock = new();
        motor.Construct(clock);

        motor.ShowRemoteMovement(true, new Vector2(0f, 2f));
        Assert.That(motor.IsMoving, Is.True);
        Assert.That(motor.FacingDirection, Is.EqualTo(Vector2.up));

        motor.PlayAttackAnimation(Vector2.left);
        clock.Advance(10f);
        motor.UpdateAttackAnimation();

        Assert.That(motor.IsMoving, Is.True, "A replicated mob keeps the host's movement state after its attack animation.");
        Assert.That(motor.FacingDirection, Is.EqualTo(Vector2.left));
    }

    /// <summary>
    /// Playing an attack raises <see cref="MobMotor2D.AttackAnimationPlayed"/> with the normalised attack
    /// direction.
    /// </summary>
    [Test]
    public void PlayAttackAnimation_RaisesAttackAnimationPlayed_WithTheFacing()
    {
        MobMotor2D motor = CreateMotorWithWeaselAnimator();
        Vector2? played = null;
        motor.AttackAnimationPlayed += direction => played = direction;

        motor.PlayAttackAnimation(new Vector2(3f, 0f));

        Assert.That(played, Is.EqualTo(Vector2.right));
    }

    /// <summary>
    /// After the first attack, playing another attack animation allocates no GC memory.
    /// </summary>
    [Test]
    public void PlayAttackAnimation_DoesNotAllocate_AfterFirstAttack()
    {
        MobMotor2D motor = CreateMotorWithWeaselAnimator();
        motor.PlayAttackAnimation(Vector2.right);

        Assert.That(() => motor.PlayAttackAnimation(Vector2.left), Is.Not.AllocatingGCMemory());
    }

    /// <summary>
    /// Removing the animator controller makes the next attack report that attack animations are not supported.
    /// </summary>
    [Test]
    public void PlayAttackAnimation_RechecksAttackSupport_WhenControllerChanges()
    {
        MobMotor2D motor = CreateMotorWithWeaselAnimator();
        motor.PlayAttackAnimation(Vector2.right);
        Assert.That(motor.SupportsAttackAnimation, Is.True);

        root.GetComponent<Animator>().runtimeAnimatorController = null;
        motor.PlayAttackAnimation(Vector2.right);

        Assert.That(motor.SupportsAttackAnimation, Is.False);
    }

    /// <summary>
    /// A stopped mob pushed only by steering drifts with it but keeps facing its target instead of the drift.
    /// </summary>
    [Test]
    public void FixedTick_KeepsFacing_WhenOnlySteeringDriftMovesTheMob()
    {
        MobMotor2D motor = CreateMotorWithWeaselAnimator();
        motor.Initialize(null);
        motor.FaceTowards(motor.Position + Vector2.right);

        // Holding still (no desired velocity) while crowd separation nudges the mob left.
        motor.Stop();
        motor.SetSteeringVelocity(Vector2.left);
        motor.FixedTick();

        Assert.That(root.GetComponent<Rigidbody2D>().linearVelocity.x, Is.LessThan(0f), "The drift itself should still apply.");
        Assert.That(motor.FacingDirection.x, Is.GreaterThan(0.9f), "Facing should stay on the target, not turn to face the drift.");
    }

    /// <summary>
    /// A moving mob faces its desired direction even when steering deflects its actual velocity.
    /// </summary>
    [Test]
    public void FixedTick_FacesIntendedDirection_EvenWhenSteeringDeflectsTheMob()
    {
        MobMotor2D motor = CreateMotorWithWeaselAnimator();
        motor.Initialize(null);

        motor.SetDesiredVelocity(Vector2.up * 2f);
        motor.SetSteeringVelocity(Vector2.left * 1.5f);
        motor.FixedTick();

        Assert.That(motor.FacingDirection.y, Is.GreaterThan(0.99f));
    }

    /// <summary>
    /// Creates a mob with a gravity-free rigidbody, an animator using the weasel controller (asserted to exist)
    /// and a <see cref="MobMotor2D"/>.
    /// </summary>
    private MobMotor2D CreateMotorWithWeaselAnimator()
    {
        root = new GameObject("Mob");
        Rigidbody2D rb = root.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;

        Animator animator = root.AddComponent<Animator>();
        RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Mobs/Weasel/Weasel.controller");
        Assert.That(controller, Is.Not.Null);
        animator.runtimeAnimatorController = controller;

        return root.AddComponent<MobMotor2D>();
    }
}

using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

public class MobMotor2DTests
{
    private GameObject root;

    [TearDown]
    public void TearDown()
    {
        if (root != null)
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void PlayAttackAnimation_SetsAnimatorAttackFlag()
    {
        MobMotor2D motor = CreateMotorWithWeaselAnimator();

        motor.PlayAttackAnimation(Vector2.right);

        Assert.That(motor.SupportsAttackAnimation, Is.True);
        Assert.That(motor.IsAttackAnimationActive, Is.True);

        Vector2 lastMoveDirection = motor.LastMoveDirection;
        Assert.That(lastMoveDirection.x, Is.GreaterThan(0.9f));
        Assert.That(lastMoveDirection.y, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void PlayAttackAnimation_DoesNotAllocate_AfterFirstAttack()
    {
        MobMotor2D motor = CreateMotorWithWeaselAnimator();
        motor.PlayAttackAnimation(Vector2.right);

        Assert.That(() => motor.PlayAttackAnimation(Vector2.left), Is.Not.AllocatingGCMemory());
    }

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

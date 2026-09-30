using NUnit.Framework;
using UnityEngine;
using UnityEditor;

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
        root = new GameObject("Mob");
        Rigidbody2D rb = root.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;

        Animator animator = root.AddComponent<Animator>();
        RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Mobs/Weasel/Weasel.controller");
        Assert.That(controller, Is.Not.Null);
        animator.runtimeAnimatorController = controller;

        MobMotor2D motor = root.AddComponent<MobMotor2D>();

        motor.PlayAttackAnimation(Vector2.right);

        Assert.That(motor.SupportsAttackAnimation, Is.True);
        Assert.That(motor.IsAttackAnimationActive, Is.True);

        Vector2 lastMoveDirection = motor.LastMoveDirection;
        Assert.That(lastMoveDirection.x, Is.GreaterThan(0.9f));
        Assert.That(lastMoveDirection.y, Is.EqualTo(0f).Within(0.001f));
    }
}

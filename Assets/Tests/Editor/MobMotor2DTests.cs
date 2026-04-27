using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using System.Reflection;

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

        Assert.That(GetPrivateField<bool>(motor, "supportsAttackAnimation"), Is.True);
        Assert.That(GetPrivateField<bool>(motor, "isAttackAnimationActive"), Is.True);

        Vector2 lastMoveDirection = GetPrivateField<Vector2>(motor, "lastMoveDirection");
        Assert.That(lastMoveDirection.x, Is.GreaterThan(0.9f));
        Assert.That(lastMoveDirection.y, Is.EqualTo(0f).Within(0.001f));
    }

    private static T GetPrivateField<T>(Object target, string fieldName)
    {
        FieldInfo field = typeof(MobMotor2D).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Expected field '{fieldName}' to exist on {nameof(MobMotor2D)}.");
        return (T)field.GetValue(target);
    }
}

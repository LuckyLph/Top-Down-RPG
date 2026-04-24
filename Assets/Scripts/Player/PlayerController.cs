using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField, Min(0.1f)] private float walkAnimationSpeed = 0.85f;
    [SerializeField] private Animator animator;

    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int LastMoveXHash = Animator.StringToHash("LastMoveX");
    private static readonly int LastMoveYHash = Animator.StringToHash("LastMoveY");

    private readonly InputAction moveAction = new("Move");

    private Rigidbody2D rb;
    private Vector2 moveInput;
    private Vector2 lastMoveDirection = Vector2.down;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        ResolveAnimator();

        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");

        moveAction.AddBinding("<Gamepad>/leftStick");
    }

    private void OnValidate()
    {
        ResolveAnimator();
    }

    private void OnEnable()
    {
        moveAction.Enable();
    }

    private void OnDisable()
    {
        moveAction.Disable();
    }

    private void Update()
    {
        moveInput = moveAction.ReadValue<Vector2>().normalized;

        bool isMoving = moveInput.sqrMagnitude > 0.0001f;
        if (isMoving)
        {
            lastMoveDirection = moveInput;
        }

        Vector2 animationDirection = isMoving ? moveInput : lastMoveDirection;

        if (animator != null)
        {
            animator.SetBool(IsMovingHash, isMoving);
            animator.SetFloat(MoveXHash, animationDirection.x);
            animator.SetFloat(MoveYHash, animationDirection.y);
            animator.SetFloat(LastMoveXHash, lastMoveDirection.x);
            animator.SetFloat(LastMoveYHash, lastMoveDirection.y);
            animator.speed = isMoving ? walkAnimationSpeed : 1f;
        }
    }

    private void FixedUpdate()
    {
        rb.linearVelocity = moveInput * moveSpeed;
    }

    private void ResolveAnimator()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }
    }
}

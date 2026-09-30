using UnityEngine;
using VContainer;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField, Min(0.1f)] private float walkAnimationSpeed = 0.85f;
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerWeaponController weaponController;

    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int LastMoveXHash = Animator.StringToHash("LastMoveX");
    private static readonly int LastMoveYHash = Animator.StringToHash("LastMoveY");

    private Rigidbody2D rb;
    private IPlayerInput input;
    private Vector2 moveInput;
    private Vector2 lastMoveDirection = Vector2.down;

    public Vector2 FacingDirection => lastMoveDirection;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        ResolveAnimator();
        ResolveWeaponController();

        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    private void OnValidate()
    {
        ResolveAnimator();
        ResolveWeaponController();
    }

    [Inject]
    public void Construct(IPlayerInput playerInput)
    {
        input = playerInput;
    }

    private void Update()
    {
        moveInput = input != null ? input.Move.normalized : Vector2.zero;

        bool isMoving = moveInput.sqrMagnitude > 0.0001f;
        if (isMoving)
        {
            lastMoveDirection = moveInput;
        }

        if (input != null && input.AttackPressedThisFrame)
        {
            weaponController?.TryAttack();
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

    public void Teleport(Vector2 position)
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
        }

        rb.position = position;
        rb.linearVelocity = Vector2.zero;
        transform.position = new Vector3(position.x, position.y, transform.position.z);
    }

    internal void Face(Vector2 direction)
    {
        if (direction.sqrMagnitude > 0.0001f)
        {
            lastMoveDirection = direction.normalized;
        }
    }

    private void ResolveAnimator()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }
    }

    private void ResolveWeaponController()
    {
        if (weaponController == null)
        {
            weaponController = GetComponent<PlayerWeaponController>();
        }
    }
}

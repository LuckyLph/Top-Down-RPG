using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField, Min(0.1f)] private float walkAnimationSpeed = 0.85f;
    [SerializeField] private Animator animator;
    [SerializeField] private InputActionAsset inputActionsAsset;
    [SerializeField] private PlayerWeaponController weaponController;
    [SerializeField] private string playerActionMapName = "Player";
    [SerializeField] private string moveActionName = "Move";
    [SerializeField] private string attackActionName = "Attack";

    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int LastMoveXHash = Animator.StringToHash("LastMoveX");
    private static readonly int LastMoveYHash = Animator.StringToHash("LastMoveY");

    private Rigidbody2D rb;
    private InputAction moveAction;
    private InputAction attackAction;
    private Vector2 moveInput;
    private Vector2 lastMoveDirection = Vector2.down;
    private bool usingFallbackActions;
    private bool inputEnabled = true;

    public Vector2 FacingDirection => lastMoveDirection;
    public bool InputEnabled => inputEnabled;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        ResolveAnimator();
        ResolveWeaponController();
        ResolveInputActions();

        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    private void OnValidate()
    {
        ResolveAnimator();
        ResolveWeaponController();
    }

    private void OnEnable()
    {
        ResolveInputActions();
        moveAction?.Enable();
        attackAction?.Enable();
    }

    private void OnDisable()
    {
        moveAction?.Disable();
        attackAction?.Disable();
    }

    private void OnDestroy()
    {
        DisposeFallbackActions();
    }

    private void Update()
    {
        moveInput = inputEnabled && moveAction != null ? moveAction.ReadValue<Vector2>().normalized : Vector2.zero;

        bool isMoving = moveInput.sqrMagnitude > 0.0001f;
        if (isMoving)
        {
            lastMoveDirection = moveInput;
        }

        if (inputEnabled && attackAction != null && attackAction.WasPressedThisFrame())
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

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
        if (!enabled)
        {
            moveInput = Vector2.zero;
        }
    }

    public void Teleport(Vector2 position)
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
        }

        // Physics2D auto-sync is off, so move both the body and the transform.
        rb.position = position;
        rb.linearVelocity = Vector2.zero;
        transform.position = new Vector3(position.x, position.y, transform.position.z);
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

    private void ResolveInputActions()
    {
        if (moveAction != null && attackAction != null)
        {
            return;
        }

        if (inputActionsAsset != null)
        {
            InputActionMap playerMap = inputActionsAsset.FindActionMap(playerActionMapName, false);
            moveAction = playerMap?.FindAction(moveActionName, false);
            attackAction = playerMap?.FindAction(attackActionName, false);

            if (moveAction != null && attackAction != null)
            {
                usingFallbackActions = false;
                return;
            }

            // Never mix asset-owned and fallback actions: fallbacks are disposed on destroy.
            moveAction = null;
            attackAction = null;
        }

        CreateFallbackActions();
    }

    private void CreateFallbackActions()
    {
        // Editor and builds resolve input identically: an unassigned or incomplete asset uses these
        // hardcoded bindings everywhere, so rebinding in the asset has no effect until it is assigned.
        Debug.LogWarning(
            $"{nameof(PlayerController)} on '{name}' has no usable '{playerActionMapName}' map with '{moveActionName}' and '{attackActionName}' actions; using hardcoded fallback bindings.",
            this);

        if (moveAction == null)
        {
            moveAction = new InputAction(moveActionName);
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

        if (attackAction == null)
        {
            attackAction = new InputAction(attackActionName, InputActionType.Button);
            attackAction.AddBinding("<Keyboard>/space");
            attackAction.AddBinding("<Mouse>/leftButton");
            attackAction.AddBinding("<Gamepad>/buttonWest");
        }

        usingFallbackActions = true;
    }

    private void DisposeFallbackActions()
    {
        if (!usingFallbackActions)
        {
            return;
        }

        moveAction?.Dispose();
        attackAction?.Dispose();
        moveAction = null;
        attackAction = null;
        usingFallbackActions = false;
    }
}

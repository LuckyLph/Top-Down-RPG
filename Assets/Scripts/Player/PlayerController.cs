using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

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

    public Vector2 FacingDirection => lastMoveDirection;

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
        moveInput = moveAction != null ? moveAction.ReadValue<Vector2>().normalized : Vector2.zero;

        bool isMoving = moveInput.sqrMagnitude > 0.0001f;
        if (isMoving)
        {
            lastMoveDirection = moveInput;
        }

        if (attackAction != null && attackAction.WasPressedThisFrame())
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

        InputActionAsset resolvedAsset = inputActionsAsset != null ? inputActionsAsset : LoadDefaultInputActionsAsset();
        if (resolvedAsset != null)
        {
            InputActionMap playerMap = resolvedAsset.FindActionMap(playerActionMapName, false);
            moveAction = playerMap?.FindAction(moveActionName, false);
            attackAction = playerMap?.FindAction(attackActionName, false);

            if (moveAction != null && attackAction != null)
            {
                usingFallbackActions = false;
                return;
            }
        }

        CreateFallbackActions();
    }

    private void CreateFallbackActions()
    {
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

    private static InputActionAsset LoadDefaultInputActionsAsset()
    {
#if UNITY_EDITOR
        return AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
#else
        return null;
#endif
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

namespace Farm
{
    [RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer), typeof(Animator))]
    public sealed class FarmPlayerController : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float moveSpeed = 3.5f;

        private static readonly int Moving = Animator.StringToHash("Moving");
        private static readonly int Direction = Animator.StringToHash("Direction");
        private Rigidbody2D body;
        private Animator animator;
        private InputAction movement;
        private Vector2 input;
        private int facing; // 0 down, 1 left, 2 right, 3 up.

        public float MoveSpeed => moveSpeed;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            animator = GetComponent<Animator>();
            movement = new InputAction("Move", InputActionType.Value);
            movement.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        }

        private void OnEnable() => movement.Enable();

        private void Update()
        {
            input = Vector2.ClampMagnitude(movement.ReadValue<Vector2>(), 1f);
            bool moving = input.sqrMagnitude > 0.001f;
            if (moving)
                facing = Mathf.Abs(input.x) >= Mathf.Abs(input.y)
                    ? (input.x < 0 ? 1 : 2) : (input.y < 0 ? 0 : 3);
            animator.SetInteger(Direction, facing);
            animator.SetBool(Moving, moving);
        }

        private void FixedUpdate() => body.linearVelocity = input * moveSpeed;

        private void OnDisable()
        {
            movement?.Disable();
            input = Vector2.zero;
            if (body != null) body.linearVelocity = Vector2.zero;
        }

        private void OnDestroy() => movement?.Dispose();
    }
}

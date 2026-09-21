using System;
using System.Collections;
using System.Collections.Generic;
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
        public bool IsWorking { get; private set; }
        private Action<bool> workFinished;
        private List<Vector2> route;
        private int waypoint;
        private Action<bool> walkFinished;
        private Func<Vector2, Vector2, bool> clearSegment;
        private float stuckFor;
        private Vector2 lastPosition;
        public bool IsWalkingToPlot => route != null;
        public void WalkToPlot(List<Vector2> path, Func<Vector2,Vector2,bool> canWalk, Action<bool> finished)
        {
            CancelWalk(); route=path; waypoint=0; clearSegment=canWalk; walkFinished=finished;
            stuckFor=0; lastPosition=body.position;
        }
        public void CancelWalk() => FinishWalk(false);
        private void FinishWalk(bool success)
        {
            if(route == null) return;
            route=null; input=Vector2.zero; body.linearVelocity=Vector2.zero;
            animator.SetBool(Moving,false);
            var callback=walkFinished; walkFinished=null; callback?.Invoke(success);
        }
        private static readonly int Working = Animator.StringToHash("Working");
        private static readonly string[] Directions = { "Down", "Left", "Right", "Up" };

        public bool BeginWork(string action, Vector3 target, Action<bool> finished)
        {
            if (IsWorking || !isActiveAndEnabled) return false;
            Vector2 delta = target - transform.position;
            facing = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? (delta.x < 0 ? 1 : 2) : (delta.y < 0 ? 0 : 3);
            int state = Animator.StringToHash("Base Layer." + action + Directions[facing]);
            if (!animator.HasState(0, state)) return false;
            IsWorking = true; workFinished = finished;
            input = Vector2.zero; body.linearVelocity = Vector2.zero;
            animator.SetInteger(Direction, facing); animator.SetBool(Moving, false); animator.SetBool(Working, true);
            animator.Play(state, 0, 0); animator.Update(0);
            StartCoroutine(Work());
            return true;
        }
        private IEnumerator Work()
        {
            // Completion follows the actual non-looping clip, including any animator speed changes.
            do { yield return null; } while (animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f);
            EndWork(true);
        }
        private void EndWork(bool completed)
        {
            if (!IsWorking) return;
            IsWorking = false;
            animator.SetBool(Working, false);
            animator.Play("Idle" + Directions[facing], 0, 0);
            var callback = workFinished; workFinished = null; callback?.Invoke(completed);
        }
        public void CancelWork() { StopAllCoroutines(); EndWork(false); }

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
            if (IsWorking) { input = Vector2.zero; return; }
            input = Vector2.ClampMagnitude(movement.ReadValue<Vector2>(), 1f);
            if(route != null)
            {
                if(input.sqrMagnitude>.001f || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)) CancelWalk();
                else if(waypoint>=route.Count) FinishWalk(true);
                else input=(route[waypoint]-body.position).normalized;
            }
            if(IsWorking) return;
            bool moving = input.sqrMagnitude > 0.001f;
            if (moving)
                facing = Mathf.Abs(input.x) >= Mathf.Abs(input.y)
                    ? (input.x < 0 ? 1 : 2) : (input.y < 0 ? 0 : 3);
            animator.SetInteger(Direction, facing);
            animator.SetBool(Moving, moving);
        }

        private void FixedUpdate()
        {
            if(route != null)
            {
                if(waypoint>=route.Count) { FinishWalk(true); return; }
                Vector2 delta=route[waypoint]-body.position;
                if(delta.magnitude<.04f) { waypoint++; body.linearVelocity=Vector2.zero; return; }
                if(!clearSegment(body.position,route[waypoint])) { FinishWalk(false); return; }
                stuckFor=Vector2.Distance(lastPosition,body.position)<.002f ? stuckFor+Time.fixedDeltaTime : 0;
                lastPosition=body.position;
                if(stuckFor>1f) { FinishWalk(false); return; }
                body.linearVelocity=delta.normalized*Mathf.Min(moveSpeed,delta.magnitude/Time.fixedDeltaTime);
                return;
            }
            body.linearVelocity = IsWorking ? Vector2.zero : input * moveSpeed;
        }

        private void OnDisable()
        {
            CancelWalk();
            CancelWork();
            movement?.Disable();
            input = Vector2.zero;
            if (body != null) body.linearVelocity = Vector2.zero;
        }

        private void OnDestroy() => movement?.Dispose();
    }
}

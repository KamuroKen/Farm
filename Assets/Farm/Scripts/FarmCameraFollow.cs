using UnityEngine;

namespace Farm
{
    [RequireComponent(typeof(Camera))]
    public sealed class FarmCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 0.5f, -10f);

        public Transform Target { get => target; set => target = value; }

        private void OnEnable()
        {
            Camera cameraComponent = GetComponent<Camera>();
            cameraComponent.orthographic = true;
            SnapToTarget();
        }

        private void LateUpdate() => SnapToTarget();

        public void SnapToTarget()
        {
            if (target != null) transform.position = target.position + offset;
        }
    }
}

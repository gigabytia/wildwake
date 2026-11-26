using UnityEngine;
using UnityEngine.InputSystem;

namespace Wildwake
{
    [RequireComponent(typeof(Camera))]
    public sealed class WildwakeOrbitCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private LayerMask obstacleLayers = ~0;
        [SerializeField, Min(0.5f)] private float distance = 4.8f;
        [SerializeField] private float followHeight = 1.45f;
        [SerializeField] private float shoulderOffset = 0.42f;
        [SerializeField, Min(0f)] private float mouseSensitivity = 0.17f;
        [SerializeField] private float initialYaw;
        [SerializeField, Range(-20f, 60f)] private float initialPitch = 15f;

        private readonly RaycastHit[] obstacleHits = new RaycastHit[12];
        private Vector3 pivot;
        private Vector3 pivotVelocity;
        private float yaw;
        private float pitch;
        private float actualDistance;
        private float distanceVelocity;

        private void Awake()
        {
            yaw = initialYaw;
            pitch = initialPitch;
            if (target != null)
            {
                pivot = target.position + Vector3.up * followHeight;
                MoveCamera(true);
            }
        }

        private void OnEnable()
        {
            SetCursorCapture(Application.isFocused);
        }

        private void OnDisable()
        {
            SetCursorCapture(false);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                SetCursorCapture(false);
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            UpdateOrbitInput();
            pivot = Vector3.SmoothDamp(pivot, target.position + Vector3.up * followHeight, ref pivotVelocity, 0.075f);
            MoveCamera(false);
        }

        private void UpdateOrbitInput()
        {
            Mouse mouse = Mouse.current;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SetCursorCapture(false);
                return;
            }

            if (mouse == null)
            {
                return;
            }

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                if (mouse.leftButton.wasPressedThisFrame && Application.isFocused)
                {
                    SetCursorCapture(true);
                }

                return;
            }

            Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity;
            yaw = Mathf.Repeat(yaw + delta.x, 360f);
            pitch = Mathf.Clamp(pitch - delta.y, -20f, 60f);
        }

        private void MoveCamera(bool instantly)
        {
            Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 offset = orbit * new Vector3(shoulderOffset, 0f, -distance);
            Vector3 direction = offset.normalized;
            float clearDistance = offset.magnitude;
            int count = Physics.SphereCastNonAlloc(pivot, 0.2f, direction, obstacleHits, clearDistance,
                obstacleLayers, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = obstacleHits[index];
                if (!hit.transform.IsChildOf(target))
                {
                    clearDistance = Mathf.Min(clearDistance, Mathf.Max(0.3f, hit.distance - 0.07f));
                }
            }

            if (instantly || clearDistance < actualDistance)
            {
                actualDistance = clearDistance;
                distanceVelocity = 0f;
            }
            else
            {
                actualDistance = Mathf.SmoothDamp(actualDistance, clearDistance, ref distanceVelocity, 0.18f);
            }

            transform.SetPositionAndRotation(pivot + direction * actualDistance, orbit);
        }

        private static void SetCursorCapture(bool capture)
        {
            Cursor.lockState = capture ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !capture;
        }
    }
}

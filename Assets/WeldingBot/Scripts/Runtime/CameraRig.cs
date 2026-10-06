using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WeldingBot
{
    /// <summary>Orbit camera: left-drag rotate, right/middle-drag pan, wheel zoom. Modes: overview, follow torch.</summary>
    public class CameraRig : MonoBehaviour
    {
        public enum Mode { Overview, FollowTorch }
        public Mode mode = Mode.Overview;
        public Transform torch;
        public Vector3 pivot = new Vector3(0f, 1f, 0f);
        public float yaw = -35f, pitch = 32f, distance = 22f;
        public float followDistance = 4.5f;
        public System.Func<Vector2, bool> isOverUI;      // set by the UI
        public FactoryCutaway cutaway;
        [Range(0f, 0.5f)] public float viewportLeft;     // fraction of the screen covered by the side panel
        Camera cam;
        Vector3 followPivot;
        bool dragging;

        public void FrameBounds(Bounds b)
        {
            pivot = b.center + Vector3.up * 1.5f;
            float size = Mathf.Max(b.size.x, b.size.z * 1.6f, 6f);
            distance = Mathf.Clamp(size * 1.35f + 7f, 12f, 60f);
            yaw = -32f; pitch = 30f;
            mode = Mode.Overview;
            Place();
        }

        public void SetMode(Mode m)
        {
            mode = m;
            if (m == Mode.FollowTorch && torch != null) followPivot = torch.position;
        }

        void LateUpdate()
        {
            HandleInput();
            Place();
        }

        void HandleInput()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 pos = mouse.position.ReadValue();
            bool over = isOverUI != null && isOverUI(pos);
            bool anyPress = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame;
            if (anyPress) dragging = !over;
            if (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed && !mouse.middleButton.isPressed) dragging = false;
            Vector2 d = mouse.delta.ReadValue();
            if (dragging && mouse.leftButton.isPressed)
            {
                yaw += d.x * 0.25f;
                pitch = Mathf.Clamp(pitch - d.y * 0.25f, 5f, 85f);
            }
            else if (dragging && (mouse.rightButton.isPressed || mouse.middleButton.isPressed))
            {
                float k = (mode == Mode.FollowTorch ? followDistance : distance) * 0.0015f;
                Vector3 right = transform.right, fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                Vector3 move = (-right * d.x - fwd * d.y) * k;
                if (mode == Mode.FollowTorch) mode = Mode.Overview;
                pivot += move;
            }
            float wheel = mouse.scroll.ReadValue().y;
            if (!over && Mathf.Abs(wheel) > 0.01f)
            {
                float f = Mathf.Pow(0.9f, Mathf.Sign(wheel) * Mathf.Min(3f, Mathf.Abs(wheel) / 120f + 0.5f));
                if (mode == Mode.FollowTorch) followDistance = Mathf.Clamp(followDistance * f, 1f, 20f);
                else distance = Mathf.Clamp(distance * f, 2f, 80f);
            }
#endif
        }

        public void Place()
        {
            Vector3 p = pivot; float dist = distance;
            if (mode == Mode.FollowTorch && torch != null)
            {
                followPivot = Vector3.Lerp(followPivot, torch.position, Application.isPlaying ? 1f - Mathf.Exp(-Time.unscaledDeltaTime * 4f) : 1f);
                p = followPivot; dist = followDistance;
            }
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = p - rot * Vector3.forward * dist;
            transform.rotation = rot;
            if (cutaway != null) cutaway.UpdateFor(transform.position);
            // shift the image centre into the area right of the UI panel
            if (cam == null) cam = GetComponent<Camera>();
            if (cam != null)
            {
                cam.ResetProjectionMatrix();
                if (viewportLeft > 0.001f)
                {
                    var m = cam.projectionMatrix;
                    m[0, 2] = -viewportLeft;
                    cam.projectionMatrix = m;
                }
            }
        }
    }
}

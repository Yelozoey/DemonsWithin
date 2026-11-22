using Unity.Cinemachine;
using UnityEngine;

namespace DemonsWithin
{
    [DisallowMultipleComponent]
    public sealed class DuelCameraRig : MonoBehaviour
    {
        [SerializeField] private LayerMask obstructions = 1 << 0;
        [SerializeField, Range(0.03f, 1f)] private float sensitivity = 0.15f;
        [SerializeField, Min(1f)] private float freeDistance = 4.4f;
        [SerializeField, Min(1f)] private float duelDistance = 4.0f;
        [SerializeField, Min(0f)] private float blendTime = 0.35f;
        [SerializeField, Range(0.25f, 0.5f)] private float playerScreenX = 0.42f;
        [SerializeField, Range(25f, 85f)] private float freeVerticalFov = 48f;
        [SerializeField, Range(25f, 85f)] private float runVerticalFov = 54f;
        [SerializeField, Range(25f, 85f)] private float duelVerticalFov = 51f;

        private CinemachineBrain brain;
        private CinemachineCamera freeCamera;
        private CinemachineCamera duelCamera;
        private DuelFighter player;
        private DuelFighter opponent;
        private Camera output;
        private Vector3 duelLine;
        private Vector3 freeFocus;
        private Vector3 duelFocus;
        private float yaw;
        private float pitch;
        private float side = 1f;
        private bool locked;

        public Vector3 MovementForward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        public bool Locked => locked;

        public void Bind(DuelFighter player, DuelFighter opponent)
        {
            this.player = player;
            this.opponent = opponent;
            output = GetComponent<Camera>();
            if (!TryGetComponent(out brain))
                brain = gameObject.AddComponent<CinemachineBrain>();
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, blendTime);
            freeCamera = CreateCamera("Free Orbit", freeVerticalFov);
            duelCamera = CreateCamera("Duel Framing", duelVerticalFov);
            ResetRig();
        }

        public void ResetRig()
        {
            yaw = player.transform.eulerAngles.y;
            pitch = 17f;
            locked = false;
            Vector3 first = player.transform.position;
            Vector3 second = opponent.transform.position;
            freeFocus = first + Vector3.up * 1.25f;
            duelFocus = (first + second) * 0.5f + Vector3.up * 1.10f;
            duelLine = FlatDirection(second - first, MovementForward);
            freeCamera.Lens.FieldOfView = freeVerticalFov;
            UpdatePose(1f);
        }

        public void SetLocked(bool value)
        {
            if (value == locked)
                return;
            if (value)
            {
                Vector3 first = player.transform.position;
                Vector3 second = opponent.transform.position;
                duelLine = FlatDirection(second - first, MovementForward);
                Vector3 right = Vector3.Cross(Vector3.up, duelLine);
                side = Vector3.Dot(transform.position - first, right) < -0.1f ? -1f : 1f;
                duelFocus = (first + second) * 0.5f + Vector3.up * 1.10f;
            }
            else
            {
                Vector3 sight = FlatDirection(transform.forward, MovementForward);
                yaw = Quaternion.LookRotation(sight).eulerAngles.y;
            }

            locked = value;
        }

        public void Look(Vector2 mouseDelta)
        {
            if (locked)
                return;
            yaw += mouseDelta.x * sensitivity;
            pitch = Mathf.Clamp(pitch - mouseDelta.y * sensitivity, -5f, 50f);
        }

        private void LateUpdate()
        {
            if (brain == null)
                return;
            UpdatePose(1f - Mathf.Exp(-9f * Time.deltaTime));
            brain.ManualUpdate();
        }

        private void UpdatePose(float blend)
        {
            Vector3 first = player.GetRenderPose().position;
            Vector3 second = opponent.GetRenderPose().position;
            Vector3 playerChest = first + Vector3.up * 1.25f;
            freeFocus = Vector3.Lerp(freeFocus, playerChest, blend);
            Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 shoulder = Quaternion.Euler(0f, yaw, 0f) * Vector3.right * 0.70f;
            freeCamera.Lens.FieldOfView = Mathf.Lerp(freeCamera.Lens.FieldOfView,
                player.Sprinting ? runVerticalFov : freeVerticalFov, blend * 0.65f);
            duelCamera.Lens.FieldOfView = duelVerticalFov;
            Pose(freeCamera, freeFocus, freeFocus - orbit * Vector3.forward * freeDistance + shoulder,
                freeFocus, playerScreenX);

            Vector3 separation = second - first;
            duelLine = Vector3.Slerp(duelLine, FlatDirection(separation, duelLine), blend);
            Vector3 lateral = Vector3.Cross(Vector3.up, duelLine) * side;
            Vector3 center = Vector3.Lerp(first, second, 0.46f) + Vector3.up * 1.10f;
            duelFocus = Vector3.Lerp(duelFocus, center, blend);
            float distance = duelDistance + Mathf.Clamp(separation.magnitude - 2f, 0f, 8f) * 0.75f;
            Vector3 desired = duelFocus + lateral * distance - duelLine * (distance * 0.61f) + Vector3.up * 1.65f;
            Pose(duelCamera, duelFocus, desired, duelFocus);

            freeCamera.Priority = locked ? 10 : 20;
            duelCamera.Priority = locked ? 20 : 10;
        }

        private void Pose(CinemachineCamera camera, Vector3 pivot, Vector3 desired, Vector3 focus, float screenX = 0.5f)
        {
            Vector3 arm = desired - pivot;
            if (Physics.SphereCast(pivot, 0.18f, arm.normalized, out RaycastHit hit, arm.magnitude,
                    obstructions, QueryTriggerInteraction.Ignore))
                desired = pivot + arm.normalized * Mathf.Max(0.35f, hit.distance - 0.12f);
            float halfHorizontal = Mathf.Tan(camera.Lens.FieldOfView * Mathf.Deg2Rad * 0.5f) * output.aspect;
            float yawBias = Mathf.Atan((1f - 2f * screenX) * halfHorizontal) * Mathf.Rad2Deg;
            Quaternion rotation = Quaternion.LookRotation(focus - desired) * Quaternion.Euler(0f, yawBias, 0f);
            camera.transform.SetPositionAndRotation(desired, rotation);
        }

        private CinemachineCamera CreateCamera(string name, float fieldOfView)
        {
            var rig = new GameObject(name);
            // The Brain updates its own Transform, so the shots live beside the output camera.
            rig.transform.SetParent(transform.parent, false);
            var camera = rig.AddComponent<CinemachineCamera>();
            camera.Lens.FieldOfView = fieldOfView;
            camera.Lens.NearClipPlane = 0.08f;
            camera.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Always;
            return camera;
        }

        private void OnDestroy()
        {
            if (freeCamera != null)
                Destroy(freeCamera.gameObject);
            if (duelCamera != null)
                Destroy(duelCamera.gameObject);
        }

        private static Vector3 FlatDirection(Vector3 direction, Vector3 fallback)
        {
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            return flat.sqrMagnitude > 0.01f ? flat.normalized : fallback;
        }
    }
}

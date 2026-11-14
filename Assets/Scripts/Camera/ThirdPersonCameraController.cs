using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Maze;
using Caffeine.Gameplay.Player;
using Cinemachine;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Caffeine.Gameplay.CameraSystem
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class ThirdPersonCameraController : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void ArchiveSetPointerLockDesired(int locked);
#endif
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private Transform cameraTarget;
        [SerializeField] private CinemachineFreeLook freeLook;
        [SerializeField] private CinemachineCollider cameraCollider;
        [SerializeField] private MazeSystem mazeSystem;

        private GameplayConfig.CameraSettings cameraConfig;
        private CinemachineBrain brain;
        private float yaw;
        private float verticalAxis = 0.5f;
        private bool tutorialCursorReleased;
        private bool shopCursorReleased;
        private PlayerTargetLock targetLock;
        private Transform lockLookAt;
        private Transform lockPlayerAnchor;
        private Transform lockEnemyAnchor;
        private CinemachineTargetGroup lockGroup;
        private Vector3 playerAnchorVelocity, enemyAnchorVelocity;
        private float yawVelocity, verticalVelocity, lensVelocity;
        private float lastTargetYaw;
        private bool wasLocked;
        private bool restoringLookAt;
        private bool CursorReleased => tutorialCursorReleased || shopCursorReleased;

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            SetCursorLocked(!CursorReleased);
        }

        private void Start()
        {

            cameraConfig = ConfigSystem.Instance.Config.Camera;
            targetLock = inputReader.GetComponent<PlayerTargetLock>();
            EnsureLockLookAt();
            lockLookAt.position = cameraTarget.position;
            brain = UnityEngine.Camera.main.GetComponent<CinemachineBrain>();
            brain.m_UpdateMethod = CinemachineBrain.UpdateMethod.LateUpdate;
            brain.m_BlendUpdateMethod = CinemachineBrain.BrainUpdateMethod.LateUpdate;
            brain.m_IgnoreTimeScale = true;

            yaw = playerRoot.eulerAngles.y;
            ApplyConfiguration();
            ApplyOrientation();
        }

        private void Update()
        {
            if (cameraConfig == null || CursorReleased || !Application.isFocused
                || inputReader.IsGameplayBlocked)
            {
                return;
            }

            if (targetLock != null && targetLock.IsLocked)
            {
                if (targetLock.IsTrackingPaused)
                {
                    return;
                }

                ApplyLockedFacing(targetLock.TargetPoint);
                return;
            }
            if (mazeSystem.IsSessionOpen)
            {
                return;
            }

            Vector2 lookInput = inputReader.PointerDelta;
            yaw = Mathf.Repeat(yaw + lookInput.x * cameraConfig.HorizontalSensitivity, 360f);
            verticalAxis = Mathf.Clamp01(verticalAxis - lookInput.y * cameraConfig.VerticalSensitivity);
            ApplyOrientation();
        }

        private void ApplyLockedFacing(Vector3 point)
        {
            Vector3 direction = point - playerRoot.position;
            direction.y = 0f;
            float minimum = cameraConfig.LockDirectionMinimumDistance;
            if (direction.sqrMagnitude >= minimum * minimum)
            {
                playerRoot.rotation = Quaternion.LookRotation(direction, Vector3.up);
                lastTargetYaw = playerRoot.eulerAngles.y;
            }
        }

        private void LateUpdate()
        {
            if (cameraConfig == null || CursorReleased || !Application.isFocused
                || inputReader.IsGameplayBlocked || (targetLock != null && targetLock.IsTrackingPaused))
            {
                return;
            }

            bool locked = targetLock != null && targetLock.IsLocked;
            EnsureLockLookAt();
            float deltaTime = Time.unscaledDeltaTime;
            if (locked && !wasLocked)
            {
                // Start from the current view, even when the character is already facing the enemy.
                yaw = freeLook.m_XAxis.Value;
                lastTargetYaw = playerRoot.eulerAngles.y;
                lockPlayerAnchor.position = lockEnemyAnchor.position = cameraTarget.position;
                playerAnchorVelocity = enemyAnchorVelocity = Vector3.zero;
                yawVelocity = verticalVelocity = 0f;
            }
            if (!locked && wasLocked)
            {
                // Seed the free-view lens from the rendered lens to avoid a one-frame FOV jump.
                freeLook.m_Lens.FieldOfView = freeLook.State.Lens.FieldOfView;
                yawVelocity = verticalVelocity = lensVelocity = 0f;
            }
            wasLocked = locked;
            if (locked)
            {
                restoringLookAt = true;
                Vector3 direction = targetLock.TargetPoint - playerRoot.position;
                direction.y = 0f;
                float minimum = cameraConfig.LockDirectionMinimumDistance;
                if (direction.sqrMagnitude >= minimum * minimum)
                {
                    lastTargetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                }

                yaw = Mathf.SmoothDampAngle(yaw, lastTargetYaw, ref yawVelocity,
                                    cameraConfig.LockSmoothTime, cameraConfig.LockMaxTurnSpeed, deltaTime);
                verticalAxis = Mathf.SmoothDamp(verticalAxis, 0.5f, ref verticalVelocity,
                    cameraConfig.LockSmoothTime, Mathf.Infinity, deltaTime);
                freeLook.m_XAxis.Value = yaw;
                freeLook.m_YAxis.Value = verticalAxis;
            }
            if (!locked && !restoringLookAt)
            {
                freeLook.m_LookAt = cameraTarget;
                return;
            }
            Vector3 playerPoint = locked ? PlayerTargetLock.GetTargetPoint(playerRoot) : cameraTarget.position;
            Vector3 enemyPoint = locked ? targetLock.TargetPoint : cameraTarget.position;
            lockPlayerAnchor.position = Vector3.SmoothDamp(lockPlayerAnchor.position, playerPoint,
                ref playerAnchorVelocity, cameraConfig.LockSmoothTime, Mathf.Infinity, deltaTime);
            lockEnemyAnchor.position = Vector3.SmoothDamp(lockEnemyAnchor.position, enemyPoint,
                ref enemyAnchorVelocity, cameraConfig.LockSmoothTime, Mathf.Infinity, deltaTime);
            lockGroup.m_Targets[0].radius = locked ? BodyRadius(playerRoot) : 0f;
            lockGroup.m_Targets[1].radius = locked ? BodyRadius(targetLock.LockedTransform) : 0f;
            lockGroup.DoUpdate();
            if (!locked)
            {
                freeLook.m_Lens.FieldOfView = Mathf.SmoothDamp(freeLook.m_Lens.FieldOfView,
                                    cameraConfig.FieldOfView, ref lensVelocity, cameraConfig.LockSmoothTime, Mathf.Infinity, deltaTime);
            }

            if (!locked && (lockPlayerAnchor.position - playerPoint).sqrMagnitude <= 0.0001f
                            && (lockEnemyAnchor.position - enemyPoint).sqrMagnitude <= 0.0001f
                            && Mathf.Abs(freeLook.State.Lens.FieldOfView - cameraConfig.FieldOfView) < 0.05f)
            {
                restoringLookAt = false;
                playerAnchorVelocity = enemyAnchorVelocity = Vector3.zero;
                freeLook.m_Lens.FieldOfView = cameraConfig.FieldOfView;
                freeLook.m_LookAt = cameraTarget;
            }
            else
            {
                freeLook.m_LookAt = lockLookAt;
            }
        }

        private static float BodyRadius(Transform target)
        {
            Collider body = target.GetComponent<CharacterController>();
            if (body == null)
            {
                body = target.GetComponent<Collider>();
            }

            return body != null ? body.bounds.extents.magnitude : 1f;
        }

        private void EnsureLockLookAt()
        {
            if (lockLookAt != null)
            {
                // Detach anchors created before a script reload as well.
                if (lockLookAt.parent != null)
                {
                    lockLookAt.SetParent(null, true);
                }
            }
            else
            {
                lockLookAt = CreateAnchor("Lock look-at");
            }

            if (lockPlayerAnchor == null)
            {
                lockPlayerAnchor = CreateAnchor("Lock player bounds");
            }

            if (lockEnemyAnchor == null)
            {
                lockEnemyAnchor = CreateAnchor("Lock enemy bounds");
            }

            if (lockGroup != null)
            {
                return;
            }

            lockGroup = lockLookAt.gameObject.AddComponent<CinemachineTargetGroup>();
            lockGroup.m_PositionMode = CinemachineTargetGroup.PositionMode.GroupAverage;
            lockGroup.m_RotationMode = CinemachineTargetGroup.RotationMode.Manual;
            lockGroup.m_Targets = new[]
            {
                new CinemachineTargetGroup.Target { target = lockPlayerAnchor, weight = 1f },
                new CinemachineTargetGroup.Target { target = lockEnemyAnchor, weight = 1f }
            };
        }

        private Transform CreateAnchor(string name)
        {
            var anchor = new GameObject(name).transform;
            // All three are independent roots: neither Camera nor TargetGroup movement can move members.
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(anchor.gameObject, gameObject.scene);
            anchor.position = cameraTarget.position;
            return anchor;
        }

        private void OnDestroy()
        {
            DestroyAnchor(lockLookAt);
            DestroyAnchor(lockPlayerAnchor);
            DestroyAnchor(lockEnemyAnchor);
        }

        private static void DestroyAnchor(Transform anchor)
        {
            if (anchor == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(anchor.gameObject);
            }
            else
            {
                DestroyImmediate(anchor.gameObject);
            }
        }

        private void OnDisable()
        {
            targetLock?.ClearLock();
            if (freeLook != null && cameraTarget != null)
            {
                freeLook.m_LookAt = cameraTarget;
            }

            SetCursorLocked(false);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (Application.isPlaying && isActiveAndEnabled)
            {
                SetCursorLocked(hasFocus && !CursorReleased);
            }
        }

        public void SetTutorialCursorReleased(bool released)
        {
            tutorialCursorReleased = released;
            SetCursorLocked(isActiveAndEnabled && Application.isFocused && !CursorReleased);
        }

        public void SetShopCursorReleased(bool released)
        {
            shopCursorReleased = released;
            SetCursorLocked(isActiveAndEnabled && Application.isFocused && !CursorReleased);
        }

        private void ApplyConfiguration()
        {
            cameraTarget.localPosition = new Vector3(
                cameraTarget.localPosition.x,
                cameraConfig.FollowTargetHeight,
                cameraTarget.localPosition.z);

            freeLook.m_Follow = cameraTarget;
            freeLook.m_LookAt = cameraTarget;
            freeLook.m_CommonLens = true;
            freeLook.m_Lens.FieldOfView = cameraConfig.FieldOfView;
            freeLook.m_BindingMode = CinemachineTransposer.BindingMode.WorldSpace;
            freeLook.m_XAxis.m_InputAxisName = string.Empty;
            freeLook.m_YAxis.m_InputAxisName = string.Empty;
            // These axes receive absolute values; don't run Cinemachine's second integrator.
            freeLook.m_XAxis.m_MaxSpeed = 0f;
            freeLook.m_YAxis.m_MaxSpeed = 0f;
            freeLook.m_XAxis.m_InputAxisValue = 0f;
            freeLook.m_YAxis.m_InputAxisValue = 0f;
            freeLook.m_RecenterToTargetHeading.m_enabled = false;
            freeLook.m_YAxisRecentering.m_enabled = false;
            freeLook.m_Orbits = new[]
            {
                ToOrbit(cameraConfig.TopOrbit),
                ToOrbit(cameraConfig.MiddleOrbit),
                ToOrbit(cameraConfig.BottomOrbit)
            };
            ConfigureRigs();

            cameraCollider.m_IgnoreTag = "Player";
            cameraCollider.m_AvoidObstacles = true;
            cameraCollider.m_Strategy = CinemachineCollider.ResolutionStrategy.PullCameraForward;
            cameraCollider.m_Damping = cameraConfig.OcclusionDamping;
            cameraCollider.m_DampingWhenOccluded = 0f;
            cameraCollider.m_CameraRadius = 0.25f;
        }

        private void ConfigureRigs()
        {
            for (var index = 0; index < 3; index++)
            {
                CinemachineVirtualCamera rig = freeLook.GetRig(index);
                if (rig == null)
                {
                    continue;
                }

                CinemachineOrbitalTransposer body = rig.GetCinemachineComponent<CinemachineOrbitalTransposer>();
                if (body != null)
                {
                    body.m_XDamping = 0f;
                    body.m_YDamping = 0f;
                    body.m_ZDamping = 0f;
                }

                CinemachineComposer aim = rig.GetCinemachineComponent<CinemachineComposer>();
                if (aim != null)
                {
                    aim.m_HorizontalDamping = 0f;
                    aim.m_VerticalDamping = 0f;
                    var groupAim = aim as CinemachineGroupComposer;
                    if (groupAim != null)
                    {
                        groupAim.m_AdjustmentMode = CinemachineGroupComposer.AdjustmentMode.ZoomOnly;
                        groupAim.m_FramingMode = CinemachineGroupComposer.FramingMode.HorizontalAndVertical;
                        groupAim.m_GroupFramingSize = cameraConfig.LockGroupFramingSize;
                        groupAim.m_FrameDamping = cameraConfig.LockSmoothTime;
                        groupAim.m_MinimumFOV = cameraConfig.FieldOfView;
                        groupAim.m_MaximumFOV = cameraConfig.LockMaximumFieldOfView;
                    }
                }
            }
        }

        private void ApplyOrientation()
        {
            playerRoot.rotation = Quaternion.Euler(0f, yaw, 0f);
            freeLook.m_XAxis.Value = yaw;
            freeLook.m_YAxis.Value = verticalAxis;
        }

        public void ResetAfterTeleport(Quaternion playerRotation)
        {
            targetLock?.ClearLock();
            playerAnchorVelocity = enemyAnchorVelocity = Vector3.zero;
            yawVelocity = verticalVelocity = lensVelocity = 0f;
            wasLocked = false;
            restoringLookAt = false;
            if (lockLookAt != null)
            {
                lockLookAt.position = cameraTarget.position;
            }

            if (lockPlayerAnchor != null)
            {
                lockPlayerAnchor.position = cameraTarget.position;
            }

            if (lockEnemyAnchor != null)
            {
                lockEnemyAnchor.position = cameraTarget.position;
            }

            freeLook.m_Lens.FieldOfView = cameraConfig.FieldOfView;
            freeLook.m_LookAt = cameraTarget;
            yaw = playerRotation.eulerAngles.y;
            verticalAxis = 0.5f;
            ApplyOrientation();
            foreach (var camera in FindObjectsByType<CinemachineVirtualCameraBase>(FindObjectsSortMode.None))
            {
                camera.PreviousStateIsValid = false;
            }
        }

        private static CinemachineFreeLook.Orbit ToOrbit(Vector2 values)
        {
            return new CinemachineFreeLook.Orbit(values.x, values.y);
        }

        private static void SetCursorLocked(bool locked)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Browser focus/activation may lag behind closing a Unity menu.
            ArchiveSetPointerLockDesired(locked ? 1 : 0);
#endif
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
        [System.Serializable] public sealed class SaveState { public float yaw; public float verticalAxis; }
        public SaveState CaptureState() => new SaveState { yaw = yaw, verticalAxis = verticalAxis };
        public void RestoreState(SaveState s)
        {
            if (s == null) return;
            ResetAfterTeleport(playerRoot.rotation);
            yaw = s.yaw; verticalAxis = s.verticalAxis;
            freeLook.m_XAxis.Value = yaw; freeLook.m_YAxis.Value = verticalAxis; freeLook.PreviousStateIsValid = false;
        }

    }
}

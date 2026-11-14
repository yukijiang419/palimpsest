using Cinemachine;
using UnityEngine;

namespace Caffeine.Gameplay.CameraSystem
{
    // Collision follows the player; looking at an enemy must not pull the camera toward that enemy.
    public sealed class PlayerCameraCollider : CinemachineCollider
    {
        protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
            CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            Vector3 lookAt = state.ReferenceLookAt;
            if (stage == CinemachineCore.Stage.Body && vcam.Follow != null && state.HasLookAt)
                state.ReferenceLookAt = vcam.Follow.position;
            base.PostPipelineStageCallback(vcam, stage, ref state, deltaTime);
            state.ReferenceLookAt = lookAt;
        }
    }
}

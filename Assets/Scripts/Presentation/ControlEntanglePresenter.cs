using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Enemy;
using Caffeine.Gameplay.Maze;
using UnityEngine;

namespace Caffeine.Gameplay.Presentation
{
    [DisallowMultipleComponent]
    public sealed class ControlEntanglePresenter : MonoBehaviour
    {
        private const float AppliedSpeed = 1.8f;
        private const float PreviewSpeed = 3.2f;
        private const float PreviewAlpha = 0.55f;

        [SerializeField] private EnemyController enemy;
        [SerializeField] private LineRenderer entangleLine;

        private GameplayConfig.UiSettings ui;

        private void Start()
        {
            ui = ConfigSystem.Instance.Config.UI;

            entangleLine.useWorldSpace = false;
            entangleLine.loop = false;
            entangleLine.startWidth = ui.EntangleWidth;
            entangleLine.endWidth = ui.EntangleWidth;
            entangleLine.numCapVertices = 2;
            entangleLine.numCornerVertices = 2;
            BuildSpiral();
            entangleLine.enabled = false;
        }

        private void LateUpdate()
        {
            MazeSystem maze = enemy.MazeSystem;
            bool preview = maze != null && maze.IsSessionOpen
                && maze.CurrentTarget == enemy
                && maze.CurrentSession.ControlStage > 0;
            bool applied = enemy.IsControlled;
            bool visible = preview || applied;
            entangleLine.enabled = visible;
            if (!visible)
            {
                return;
            }

            Color color = ui.EntangleColor;
            color.a *= applied ? 1f : PreviewAlpha;
            entangleLine.startColor = color;
            entangleLine.endColor = color;

            float speed = applied ? AppliedSpeed : PreviewSpeed;
            float angle = -Time.unscaledTime * speed * Mathf.Rad2Deg;
            entangleLine.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
        }

        private void OnDisable()
        {
            entangleLine.enabled = false;
        }

        private void BuildSpiral()
        {
            int count = ui.EntangleSegments;
            entangleLine.positionCount = count;
            for (int index = 0; index < count; index++)
            {
                float t = index / (float)(count - 1);
                float angle = t * ui.EntangleTurns * Mathf.PI * 2f;
                float radius = ui.EntangleRadius * (0.88f + 0.12f * Mathf.Sin(t * Mathf.PI));
                float y = Mathf.Lerp(-ui.EntangleHeight * 0.5f, ui.EntangleHeight * 0.5f, t);
                entangleLine.SetPosition(index, new Vector3(
                    Mathf.Cos(angle) * radius,
                    y,
                    Mathf.Sin(angle) * radius));
            }
        }
    }
}

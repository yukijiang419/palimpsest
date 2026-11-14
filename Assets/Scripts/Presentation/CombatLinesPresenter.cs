using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Maze;
using Caffeine.Gameplay.Player;
using UnityEngine;

namespace Caffeine.Gameplay.Presentation
{
    [DisallowMultipleComponent]
    public sealed class CombatLinesPresenter : MonoBehaviour
    {
        [SerializeField] private PlayerAim playerAim;
        [SerializeField] private MazeSystem mazeSystem;
        [SerializeField] private Transform lineOrigin;
        [SerializeField] private LineRenderer aimLine;
        [SerializeField] private LineRenderer mazeLink;

        private GameplayConfig.UiSettings uiSettings;

        private void Start()
        {
            uiSettings = ConfigSystem.Instance.Config.UI;
            ConfigureLine(aimLine, uiSettings.AimLineWidth, uiSettings.AimLineColor);
            ConfigureLine(mazeLink, uiSettings.MazeLinkWidth, uiSettings.MazeLinkColor);
        }

        private void LateUpdate()
        {
            // draw the player aim line
            Vector3 origin = lineOrigin.position + Vector3.up * uiSettings.WorldLineHeight;
            aimLine.SetPosition(0, origin);
            aimLine.SetPosition(1, origin + playerAim.AimDirection * uiSettings.AimLineLength);
            aimLine.enabled = true;

            bool showLink = mazeSystem.IsSessionOpen
                && mazeSystem.CurrentTarget.IsAlive;
            // draw the maze target line
            mazeLink.enabled = showLink;
            if (!showLink)
            {
                return;
            }

            Vector3 target = PlayerTargetLock.GetTargetPoint(mazeSystem.CurrentTarget.transform);
            mazeLink.SetPosition(0, origin);
            mazeLink.SetPosition(1, target);
        }

        private void OnDisable()
        {
            aimLine.enabled = false;
            mazeLink.enabled = false;
        }

        private static void ConfigureLine(LineRenderer line, float width, Color color)
        {
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.loop = false;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.numCapVertices = 2;
            line.enabled = false;
        }

    }
}

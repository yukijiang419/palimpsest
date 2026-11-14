using System.Collections;
using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Player;
using UnityEngine;

namespace Caffeine.Gameplay.Puzzles
{
    [DefaultExecutionOrder(200)]
    public sealed class PuzzleRunRestorer : MonoBehaviour
    {
        private string message;
        private float messageUntil;

        private IEnumerator Start()
        {
            var player = GetComponent<PlayerInputReader>();
            var health = GetComponent<HealthShield>();
            while (health != null && !health.IsInitialized) yield return null;
            message = PuzzleRunState.RestorePlayer(player);
            messageUntil = Time.unscaledTime + 5f;
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(message) || Time.unscaledTime > messageUntil || Time.timeScale == 0f) return;
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            style.normal.textColor = Color.white;
            GUI.Box(new Rect(Screen.width * .5f - 280, Screen.height - 120, 560, 60), message, style);
        }
    }
}

using Caffeine.Gameplay.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Caffeine.Gameplay.UI
{
    // The encounter trigger calls BeginEncounter / EndEncounter. Ordinary enemies never register as bosses.
    [DisallowMultipleComponent]
    public sealed class BossEncounterPresenter : MonoBehaviour
    {
        [SerializeField] private HealthShield boss;
        [SerializeField] private string bossName = "Central corrective system";
        [SerializeField] private bool beginOnEnable;
        [SerializeField, Tooltip("Seconds between the Boss dying and the Victory screen, so its death can be seen.")]
        private float victoryDelay = 4f;
        private HealthShield subscribedBoss;
        private RectTransform panel;
        private Image healthFill, shieldFill;
        private bool encounterActive;

        public HealthShield BossHealth => boss;

        public bool TryGetLockAnchorScreenPoint(out Vector2 point)
        {
            point = default;
            if (!encounterActive || panel == null || !panel.gameObject.activeInHierarchy
                || boss == null || boss.IsDead) return false;
            Canvas canvas = panel.GetComponentInParent<Canvas>();
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            // The panel is the stable background of both Boss bars and their title.
            point = RectTransformUtility.WorldToScreenPoint(camera,
                panel.TransformPoint(new Vector3(panel.rect.center.x, panel.rect.yMax, 0f)));
            return true;
        }

        private void OnEnable()
        {
            if (beginOnEnable) BeginEncounter();
        }

        public void BeginEncounter()
        {
            if (encounterActive || boss == null || boss.IsDead) return;
            if (panel == null)
            {
                var hud = FindFirstObjectByType<PlayerHudPresenter>();
                if (hud == null) return;
                panel = ArchiveUi.Rect("Boss health and shield", hud.transform.parent,
                    new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -28), new Vector2(580, 110));
                ArchiveUi.Frame(panel, ArchiveUi.Copper);
                ArchiveUi.Text(panel, bossName, new Vector2(16, -8), new Vector2(548, 36), 24, TextAlignmentOptions.Center,
                    ArchiveUi.FontRole.Heading);
                shieldFill = ArchiveUi.Bar(panel, "Boss shield", new Vector2(20, -54), 540, ArchiveUi.Shield);
                healthFill = ArchiveUi.Bar(panel, "Boss health", new Vector2(20, -80), 540, ArchiveUi.Red);
            }
            encounterActive = true;
            subscribedBoss = boss;
            subscribedBoss.VitalsChanged += Refresh;
            subscribedBoss.Died += HandleVictory;
            panel.gameObject.SetActive(true);
            Refresh(subscribedBoss);
        }

        public void EndEncounter()
        {
            if (subscribedBoss != null)
            {
                subscribedBoss.VitalsChanged -= Refresh;
                subscribedBoss.Died -= HandleVictory;
            }
            subscribedBoss = null;
            encounterActive = false;
            if (panel != null) panel.gameObject.SetActive(false);
        }

        private void Refresh(HealthShield source)
        {
            ArchiveUi.SetBar(healthFill, source.CurrentHealth, source.MaxHealth);
            ArchiveUi.SetBar(shieldFill, source.CurrentShield, source.MaxShield);
        }

        private void HandleVictory(HealthShield source)
        {
            EndEncounter();
            if (victoryDelay > 0f) StartCoroutine(ShowVictoryLater());
            else ShowVictory();
        }

        private System.Collections.IEnumerator ShowVictoryLater()
        {
            yield return new WaitForSeconds(victoryDelay);
            ShowVictory();
        }

        private void ShowVictory()
        {
            var screens = FindFirstObjectByType<TutorialPresenter>();
            if (screens != null) screens.ShowVictory();
        }

        private void OnDisable() => EndEncounter();
        private void OnDestroy()
        {
            if (panel != null) Destroy(panel.gameObject);
        }
    }
}

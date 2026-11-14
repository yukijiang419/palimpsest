using System;
using System.Collections.Generic;
using System.Linq;
using Caffeine.Gameplay;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.CameraSystem;
using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Player;
using Caffeine.Gameplay.Quests;
using Caffeine.Gameplay.Puzzles;
using Caffeine.Gameplay.UI;
using Caffeine.Gameplay.Maze;
using Caffeine.Gameplay.Presentation;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class Merchant : MonoBehaviour
{
    [SerializeField] private GameObject shopPanel;
    [SerializeField] private string questId;
    [SerializeField] private GameObject questMarker;
    [SerializeField] private Transform puzzleReturnPoint;
    public Transform PuzzleReturnPoint => puzzleReturnPoint;

    private MerchantQuestDefinition quest;
    private MerchantQuestDefinition[] puzzleQuests;
    private readonly HashSet<Collider> playersInRange = new();
    // Looked up each time: New Game clears run progress after Start, which would orphan a cached instance.
    private QuestProgress progress => QuestProgress.ForRun(quest.QuestId);
    private static Merchant activeMerchant;
    private GameTimeController gameTime;
    private PlayerInputReader input;
    private ThirdPersonCameraController cameraController;
    private TutorialPresenter screens;
    private MazeSystem maze;
    private MerchantQuestPresenter presenter;
    private Transform destination;
    private PuzzleEntrance[] puzzleEntrances;
    private Transform bossDestination;
    private bool destinationsValid;
    private bool sessionOpen;
    private QuestOverviewController overview;
    public bool IsSessionOpen => sessionOpen;
    private bool InRange => playersInRange.Count > 0;
    public Vector3? CurrentTaskTarget { get; private set; }
    public bool IsTaskGuidanceVisible => isActiveAndEnabled && CurrentTaskTarget.HasValue && !sessionOpen && (overview == null || !overview.IsPlaying) && (screens == null || !screens.IsShowing || screens.IsMapShowing) && presenter != null && !presenter.IsShowing;

    GameplayConfig.UpgradeSettings weaponDamage => ConfigSystem.Instance.Config.Shop.WeaponDamage;

    GameplayConfig.UpgradeSettings maxHealth => ConfigSystem.Instance.Config.Shop.MaxHealth;

    GameplayConfig.UpgradeSettings maxStamina => ConfigSystem.Instance.Config.Shop.MaxStamina;

    GameplayConfig.UpgradeSettings mazeRecovery => ConfigSystem.Instance.Config.Shop.MazeRecovery;

    GameplayConfig.UpgradeSettings mazeControl => ConfigSystem.Instance.Config.Shop.MazeControl;

    [Header("Icons")] // Weapon icons come from WeaponData.icon
    [SerializeField] Sprite healthIcon;
    [SerializeField] Sprite staminaIcon;
    [SerializeField] Color lockedIconTint = new(0.25f, 0.25f, 0.25f, 1f);
    [Header("Layout")]
    [SerializeField] int columns = 3;
    [SerializeField] Vector2 cardSize = new(280f, 270f);
    [SerializeField] Vector2 cardSpacing = new(30f, 24f);
    [SerializeField] float iconSize = 150f;
    [SerializeField]
    Vector2 listCenter = new(0f, -10f); // Relative to the shop panel centre
    [SerializeField] Color cardColor = new(0.18f, 0.18f, 0.18f, 1f);
    PlayerWallet wallet;
    HealthShield health;
    PlayerStamina stamina;
    WeaponManager weapons;
    // UI references for one shop item.
    class Card
    {
        public Button Button;
        public Image Icon;
        public TextMeshProUGUI Title;
        public TextMeshProUGUI Info;
    }

    TextMeshProUGUI coinsLabel;
    Card[] weaponCards;
    Card healthCard;
    Card staminaCard;
    Card recoveryCard;
    Card controlCard;
    private void Start()
    {
        gameTime = FindFirstObjectByType<GameTimeController>();
        input = FindFirstObjectByType<PlayerInputReader>();
        cameraController = FindFirstObjectByType<ThirdPersonCameraController>();
        screens = FindFirstObjectByType<TutorialPresenter>();
        maze = FindFirstObjectByType<MazeSystem>();
        if (shopPanel != null)
        {
            shopPanel.SetActive(false);
        }

        var config = ConfigSystem.Instance != null ? ConfigSystem.Instance.Config : null;
        if (config == null || config.Quests == null || !config.Quests.TryGetDefinition(questId, out quest))
        {
            Debug.LogError($"Merchant quest '{questId}' is missing, duplicated or invalid in GameplayConfig.Quests.", this);
            return;
        }

        if (quest.HasPuzzle)
        {
            puzzleQuests = config.Quests.Definitions.Where(q => q != null && q.HasPuzzle).OrderBy(q => q.Puzzle.puzzleIndex).ToArray();
            if (puzzleQuests.Length != PuzzleRunState.BossIndex - 1 || puzzleQuests[0] != quest || puzzleQuests.Where((q, i) => q.Puzzle.puzzleIndex != i + 1).Any())
            {
                Debug.LogError("Merchant needs three separate puzzle quests in order.", this);
                return;
            }

            SelectPuzzleQuest();
        }

        destinationsValid = ResolveDestinations();
        presenter = gameObject.AddComponent<MerchantQuestPresenter>();
        presenter.Initialize(config.Quests, questMarker);
        if (quest.HasPuzzle)
        {
            overview = gameObject.AddComponent<QuestOverviewController>();
            overview.Initialize(this, input, gameTime, screens);
        }
    }

    private bool ResolveDestinations()
    {
        if (quest.HasPuzzle)
        {
            var entrances = FindObjectsByType<PuzzleEntrance>(FindObjectsSortMode.None);
            foreach (var entrance in entrances)
            {
                if (entrance.puzzleIndex == PuzzleRunState.BossIndex)
                {
                    bossDestination = entrance.InteractionTarget;
                }
            }

            puzzleEntrances = new PuzzleEntrance[PuzzleRunState.BossIndex - 1];
            for (int i = 0; i < puzzleEntrances.Length; i++)
            {
                int matches = 0;
                foreach (var entrance in entrances)
                {
                    if (entrance.puzzleIndex != i + 1)
                    {
                        continue;
                    }

                    puzzleEntrances[i] = entrance;
                    matches++;
                }

                if (matches != 1)
                {
                    Debug.LogError($"Quest '{questId}' needs one active entrance for puzzle {i + 1}; found {matches}.", this);
                    return false;
                }
            }

            return true;
        }

        foreach (var point in FindObjectsByType<QuestDestination>(FindObjectsSortMode.None))
        {
            if (point.destinationId != quest.destinationId)
            {
                continue;
            }

            if (destination != null)
            {
                Debug.LogError($"Duplicate quest destination: {quest.destinationId}", this);
                destination = null;
                break;
            }

            destination = point.transform;
        }

        if (destination == null)
        {
            Debug.LogError($"Missing unique quest destination: {quest.destinationId}", this);
        }

        return destination != null;
    }

    private int CurrentPuzzleStep() => quest.HasPuzzle ? quest.Puzzle.puzzleIndex - 1 : -1;
    private void SelectPuzzleQuest()
    {
        var selected = puzzleQuests.FirstOrDefault(candidate => QuestProgress.ForRun(candidate.QuestId).Stage == QuestStage.ReturnToMerchant) ?? puzzleQuests.FirstOrDefault(candidate => QuestProgress.ForRun(candidate.QuestId).Stage != QuestStage.Completed) ?? puzzleQuests[puzzleQuests.Length - 1];
        quest = selected;
    }

    private void Update()
    {
        playersInRange.RemoveWhere(c => c == null || !c.gameObject.activeInHierarchy);
        bool playing = (screens == null || !screens.IsShowing) && (overview == null || !overview.IsPlaying);
        if ((!playing || !InRange) && sessionOpen)
        {
            CloseShop();
        }

        if (quest != null && presenter != null && input != null)
        {
            int puzzleStep = quest.HasPuzzle ? CurrentPuzzleStep() : -1;
            if (quest.HasPuzzle)
            {
                destination = destinationsValid && progress.Stage == QuestStage.Travelling && puzzleStep >= 0 && puzzleStep < puzzleEntrances.Length && puzzleEntrances[puzzleStep] != null ? puzzleEntrances[puzzleStep].InteractionTarget : null;
            }
            else if (playing && !sessionOpen && destination != null && progress.Stage == QuestStage.Travelling && Vector3.Distance(input.transform.position, destination.position) <= quest.arrivalRadius)
            {
                progress.Arrive();
            }

            Vector3? target = progress.Stage == QuestStage.Travelling && destination != null ? destination.position : progress.Stage == QuestStage.ReturnToMerchant || progress.Stage == QuestStage.Available ? transform.position : (Vector3? )null;
            if (progress.Stage == QuestStage.Completed && PuzzleRunState.BossUnlocked && !PuzzleRunState.BossDefeated)
            {
                if (bossDestination != null)
                {
                    target = bossDestination.position;
                }
            }

            string heading = progress.Stage == QuestStage.ReturnToMerchant ? "REWARD READY" : "CURRENT TASK";
            if (progress.Stage == QuestStage.Travelling && quest.HasPuzzle && puzzleStep >= 0)
            {
                heading = $"TASK {puzzleStep + 1}";
            }

            if (target.HasValue)
            {
                heading += $"  ·  {Vector3.Distance(input.transform.position, target.Value):0} m";
            }

            string status = progress.Stage switch
            {
                QuestStage.Travelling when quest.HasPuzzle && puzzleStep >= 0 => quest.Puzzle.title,
                QuestStage.Travelling => quest.title,
                QuestStage.ReturnToMerchant when quest.HasPuzzle => "Return for " + PuzzleRunState.RewardName(puzzleStep + 1),
                QuestStage.ReturnToMerchant => "Return to the merchant",
                QuestStage.Available => "Meet the local contact",
                QuestStage.Completed when PuzzleRunState.BossUnlocked && !PuzzleRunState.BossDefeated => "Reach Old Arts · Stop the reconstruction cycle",
                _ => ""
            };
            CurrentTaskTarget = target;
            presenter.SetGuidance(heading, status, target, playing && !sessionOpen, InRange && activeMerchant == null);
        }

        var kb = Keyboard.current;
        if (!playing || kb == null)
        {
            return;
        }

        if (sessionOpen && kb.escapeKey.wasPressedThisFrame)
        {
            if (presenter != null && presenter.IsRewardShowing)
            {
                return;
            }

            CloseShop();
            return;
        }

        if (!InRange || (activeMerchant != null && activeMerchant != this))
        {
            return;
        }

        if (sessionOpen)
        {
            if (kb.eKey.wasPressedThisFrame && shopPanel != null && shopPanel.activeSelf)
            {
                CloseShop();
            }

            return;
        }

        if (presenter != null && input != null && input.TryConsumeInteract())
        {
            OpenDialogue();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playersInRange.Add(other);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        playersInRange.Remove(other);
        if (!InRange)
        {
            CloseShop();
        }
    }

    private void OnDisable()
    {
        if (overview != null)
        {
            overview.Cancel();
        }

        CurrentTaskTarget = null;
        EndSession(false);
        playersInRange.Clear();
        if (presenter != null)
        {
            presenter.SetGuidance("", "", null, false, false);
        }
    }

    private void OnDestroy()
    {
        if (wallet != null)
        {
            wallet.CoinsChanged -= OnCoinsChanged;
        }
    }

    private bool BeginSession()
    {
        if ((screens != null && screens.IsShowing) || (overview != null && overview.IsPlaying) || (activeMerchant != null && activeMerchant != this))
        {
            return false;
        }

        if (!sessionOpen)
        {
            FeedbackAudioController.Play(FeedbackSound.Open);
        }

        activeMerchant = this;
        sessionOpen = true;
        if (maze != null)
        {
            maze.CancelSession();
        }

        if (gameTime != null)
        {
            gameTime.SetShopPaused(true);
        }

        if (input != null)
        {
            input.SetShopInputBlocked(true);
        }

        if (cameraController != null)
        {
            cameraController.SetShopCursorReleased(true);
        }

        return true;
    }

    private void OpenDialogue()
    {
        if (!BeginSession())
        {
            return;
        }

        if (shopPanel != null)
        {
            shopPanel.SetActive(false);
        }

        presenter.Show(quest, progress.Stage, destinationsValid, AcceptQuest, TurnInQuest, OpenShop, CloseShop);
    }

    private void AcceptQuest()
    {
        if (!sessionOpen || !InRange || !destinationsValid || !progress.Accept())
        {
            return;
        }

        FindFirstObjectByType<Caffeine.Gameplay.Enemy.CampusEncounterDirector>()?.ActivateAvailableRound();
        QueueQuestOverview();
        CloseShop();
        if (overview == null)
        {
            screens?.TryShowCombatTutorialAfterQuestAccepted();
        }
    }

    private void QueueQuestOverview()
    {
        int step = CurrentPuzzleStep();
        if (overview != null && destinationsValid && step >= 0 && step < puzzleEntrances.Length)
        {
            overview.Queue(quest, puzzleEntrances[step].InteractionTarget);
        }
    }

    private void TurnInQuest()
    {
        if (!sessionOpen || !InRange || input == null)
        {
            return;
        }

        WeaponData acquiredWeapon = null;
        if (quest.HasPuzzle)
        {
            int index = CurrentPuzzleStep() + 1;
            if (!PuzzleRunState.ClaimReward(progress, index))
            {
                return;
            }

            input.GetComponentInChildren<WeaponManager>()?.CollectQuestReward(quest.Puzzle.rewardWeapon);
            acquiredWeapon = quest.Puzzle.rewardWeapon;
            if (index == puzzleQuests.Length)
            {
                input.GetComponent<PlayerWallet>().AddCoins(quest.coinReward);
            }

            PuzzleRunState.AuthorizeBoss();
            SelectPuzzleQuest();
            if (CurrentPuzzleStep() == 0 || PuzzleRunState.IsUnlocked(CurrentPuzzleStep()))
            {
                if (progress.Accept())
                {
                    QueueQuestOverview();
                }
            }
        }
        else
        {
            if (!progress.Complete())
            {
                return;
            }

            input.GetComponent<PlayerWallet>().AddCoins(quest.coinReward);
        }

        FindFirstObjectByType<Caffeine.Gameplay.Enemy.CampusEncounterDirector>()?.ActivateAvailableRound();
        if (acquiredWeapon != null)
        {
            presenter.ShowWeaponReward(acquiredWeapon, OpenDialogue);
        }
        else
        {
            OpenDialogue();
        }
    }

    public void OpenShop()
    {
        if (shopPanel == null || presenter == null || !presenter.IsShowing || !sessionOpen)
        {
            return;
        }

        presenter.Close();
        shopPanel.SetActive(true);
        FeedbackAudioController.Play(FeedbackSound.Open);
        ShowShopItems();
    }

    public void CloseShop()
    {
        EndSession(true);
    }

    private void EndSession(bool playSound)
    {
        if (!sessionOpen)
        {
            return;
        }

        if (playSound)
        {
            FeedbackAudioController.Play(FeedbackSound.Close);
        }

        sessionOpen = false;
        if (activeMerchant == this)
        {
            activeMerchant = null;
        }

        if (shopPanel != null)
        {
            shopPanel.SetActive(false);
        }

        if (presenter != null)
        {
            presenter.Close();
        }

        if (gameTime != null)
        {
            gameTime.SetShopPaused(false);
        }

        if (input != null)
        {
            input.SetShopInputBlocked(false);
        }

        if (cameraController != null)
        {
            cameraController.SetShopCursorReleased(false);
        }
    }

    // ---------- Purchases ----------
    // Builds the item cards on first open, then refreshes prices and levels.
    void ShowShopItems()
    {
        if (!BindPlayer())
        {
            return;
        }

        if (weaponCards == null)
        {
            BuildList();
        }

        Refresh();
    }

    bool BindPlayer()
    {
        if (wallet != null)
        {
            return true;
        }

        if (input == null)
        {
            return false;
        }

        health = input.GetComponentInParent<HealthShield>();
        stamina = input.GetComponentInParent<PlayerStamina>();
        weapons = input.GetComponentInChildren<WeaponManager>();
        if (health == null || stamina == null || maze == null || !health.IsInitialized || !stamina.IsInitialized)
        {
            Debug.LogError("Merchant needs initialized player health, stamina and maze components.", this);
            return false;
        }

        wallet = input.GetComponent<PlayerWallet>();
        wallet.CoinsChanged += OnCoinsChanged;
        // Levels are derived from the bonuses on the player, which PuzzleRunState carries across scene loads,
        // so the shop stays in step after a puzzle trip without saving its own copy.
        return true;
    }

    void OnCoinsChanged(int coins)
    {
        if (shopPanel != null && shopPanel.activeInHierarchy && healthCard != null)
        {
            Refresh();
        }
    }

    void BuyWeaponDamage(int index)
    {
        int slot = weapons.GetWeapon(index).slot;
        if (weapons.GetEquippedIndex(slot) != index || !TryBuy(weaponDamage, LevelOf(weaponDamage, weapons.GetDamageBonus(index))))
        {
            return;
        }

        weapons.AddDamageBonus(index, weaponDamage.amountPerLevel);
        Refresh();
    }

    void BuyMaxHealth()
    {
        if (!TryBuy(maxHealth, LevelOf(maxHealth, health.BonusMaxHealth)))
        {
            return;
        }

        health.IncreaseMaxHealth(maxHealth.amountPerLevel);
        Refresh();
    }

    void BuyMaxStamina()
    {
        if (!TryBuy(maxStamina, LevelOf(maxStamina, stamina.BonusMaxStamina)))
        {
            return;
        }

        stamina.IncreaseMaxStamina(maxStamina.amountPerLevel);
        Refresh();
    }

    void BuyMazeRecovery()
    {
        if (!TryBuy(mazeRecovery, LevelOf(mazeRecovery, maze.BonusRecovery)))
        {
            return;
        }

        maze.AddRecoveryBonus(mazeRecovery.amountPerLevel);
        Refresh();
    }

    void BuyMazeControl()
    {
        if (!TryBuy(mazeControl, LevelOf(mazeControl, maze.BonusControlDuration)))
        {
            return;
        }

        maze.AddControlDurationBonus(mazeControl.amountPerLevel);
        Refresh();
    }

    static int LevelOf(GameplayConfig.UpgradeSettings upgrade, float bonus)
    {
        return upgrade.amountPerLevel > 0f ? Mathf.RoundToInt(bonus / upgrade.amountPerLevel) : 0;
    }

    bool TryBuy(GameplayConfig.UpgradeSettings upgrade, int level)
    {
        bool bought = sessionOpen && shopPanel != null && shopPanel.activeInHierarchy && health.IsInitialized && !health.IsDead && level < upgrade.maxLevel && wallet.TrySpend(upgrade.PriceAt(level));
        FeedbackAudioController.Play(bought ? FeedbackSound.Purchase : FeedbackSound.Reject);
        return bought;
    }

    // ---------- UI ----------
    // Cards are generated from code so adding weapons to WeaponManager needs no UI work.
    void BuildList()
    {
        ArchiveUi.Frame((RectTransform)shopPanel.transform, ArchiveUi.Copper);
        foreach (var label in shopPanel.GetComponentsInChildren<TMP_Text>(true))
        {
            ArchiveUi.ApplyFont(label, ArchiveUi.FontRole.Heading);
        }

        foreach (var button in shopPanel.GetComponentsInChildren<Button>(true))
        {
            ArchiveUi.StyleButton(button);
        }

        var list = new GameObject("ItemList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        var listRect = (RectTransform)list.transform;
        listRect.SetParent(shopPanel.transform, false);
        listRect.anchoredPosition = listCenter;
        listRect.sizeDelta = new Vector2(columns * cardSize.x + (columns - 1) * cardSpacing.x, 0f);
        var layout = list.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        list.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var walletRow = new GameObject("Wallet", typeof(RectTransform), typeof(LayoutElement));
        walletRow.transform.SetParent(listRect, false);
        walletRow.GetComponent<LayoutElement>().preferredHeight = 40f;
        var walletRect = (RectTransform)walletRow.transform;
        ArchiveUi.Surface(ArchiveUi.Rect("Wallet icon", walletRect, new Vector2(.5f, .5f), new Vector2(1, .5f), new Vector2(-80, 0), new Vector2(48, 48)), "wallet");
        coinsLabel = CreateLabel(walletRect, "Coins", 28f, ArchiveUi.FontRole.Technical);
        ArchiveUi.Place(coinsLabel.rectTransform, new Vector2(.5f, .5f), new Vector2(0, .5f), new Vector2(-64, 0), new Vector2(240, 40));
        coinsLabel.alignment = TextAlignmentOptions.MidlineLeft;
        var grid = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup));
        grid.transform.SetParent(listRect, false);
        var gridLayout = grid.GetComponent<GridLayoutGroup>();
        gridLayout.cellSize = cardSize;
        gridLayout.spacing = cardSpacing;
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = columns;
        gridLayout.childAlignment = TextAnchor.UpperCenter;
        int count = weapons != null ? weapons.WeaponCount : 0;
        weaponCards = new Card[count];
        for (int i = 0; i < count; i++)
        {
            int index = i;
            weaponCards[i] = CreateCard(grid.transform, "Weapon" + i, weapons.GetWeapon(i).icon, () => BuyWeaponDamage(index));
        }

        healthCard = CreateCard(grid.transform, "MaxHealth", healthIcon, BuyMaxHealth);
        staminaCard = CreateCard(grid.transform, "MaxStamina", staminaIcon, BuyMaxStamina);
        recoveryCard = CreateCard(grid.transform, "MazeRecovery", null, BuyMazeRecovery);
        controlCard = CreateCard(grid.transform, "MazeControl", null, BuyMazeControl);
        if (healthIcon == null)
        {
            CreateIconBar(healthCard, ArchiveUi.Red);
        }

        if (staminaIcon == null)
        {
            CreateIconBar(staminaCard, ArchiveUi.Cyan);
        }

        var settings = ConfigSystem.Instance.Config;
        CreateEffectIcon(recoveryCard, 3, settings.UI.RecoveryColor);
        CreateEffectIcon(controlCard, 5, settings.UI.ControlColor);
    }

    void CreateEffectIcon(Card card, int sides, Color color)
    {
        var rect = ArchiveUi.Rect("EffectIcon", card.Icon.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 90f);
        var icon = rect.gameObject.AddComponent<PolygonTokenGraphic>();
        icon.ConfigureStyle(sides, 3, 5, color, color, 3f);
        icon.SetProgress(5);
    }

    // A full bar in the HUD's colours, drawn in the card's icon area when no sprite is assigned.
    void CreateIconBar(Card card, Color color)
    {
        const float height = 20f;
        float width = cardSize.x - 60f;
        Image fill = ArchiveUi.Bar(card.Button.transform, "Bar", new Vector2(30f, -(12f + (iconSize - height) * 0.5f)), width, color);
        ArchiveUi.BarTrack(fill).sizeDelta = new Vector2(width, height);
        fill.raycastTarget = false;
        ArchiveUi.BarTrack(fill).GetComponent<Image>().raycastTarget = false;
    }

    void Refresh()
    {
        coinsLabel.text = wallet.Coins.ToString("N0");
        for (int i = 0; i < weaponCards.Length; i++)
        {
            Card card = weaponCards[i];
            bool visible = IsShownInShop(i);
            card.Button.gameObject.SetActive(visible);
            if (!visible)
            {
                continue;
            }

            card.Title.text = weapons.GetWeapon(i).weaponName;
            bool unlocked = weapons.IsUnlocked(i);
            card.Icon.color = unlocked ? Color.white : lockedIconTint;
            if (!unlocked)
            {
                card.Info.text = "Locked";
                card.Button.interactable = false;
                continue;
            }

            SetUpgradeInfo(card, weaponDamage, LevelOf(weaponDamage, weapons.GetDamageBonus(i)), "ATK", weapons.GetDamage(i));
        }

        healthCard.Title.text = "Max HP";
        SetUpgradeInfo(healthCard, maxHealth, LevelOf(maxHealth, health.BonusMaxHealth), "HP", health.MaxHealth);
        staminaCard.Title.text = "Max Stamina";
        SetUpgradeInfo(staminaCard, maxStamina, LevelOf(maxStamina, stamina.BonusMaxStamina), "Stamina", stamina.MaxStamina);
        recoveryCard.Title.text = "Hack Healing";
        SetUpgradeInfo(recoveryCard, mazeRecovery, LevelOf(mazeRecovery, maze.BonusRecovery), "Heal", 1f + maze.BonusRecovery, true);
        controlCard.Title.text = "Hack Control";
        SetUpgradeInfo(controlCard, mazeControl, LevelOf(mazeControl, maze.BonusControlDuration), "Duration", 1f + maze.BonusControlDuration, true);
        if (EventSystem.current != null)
        {
            var selected = EventSystem.current.currentSelectedGameObject;
            var selectedButton = selected != null ? selected.GetComponent<Button>() : null;
            if (selectedButton == null || !selected.transform.IsChildOf(shopPanel.transform) || !selectedButton.interactable)
            {
                ArchiveUi.Focus(shopPanel.GetComponentsInChildren<Button>().FirstOrDefault(b => b.interactable));
            }
        }
    }

    // One card per slot: the weapon currently in it, or the slot's first weapon (shown locked) while it is empty.
    // Weapons replaced by a stronger one in the same slot can no longer be equipped, so upgrading them is pointless.
    bool IsShownInShop(int index)
    {
        int slot = weapons.GetWeapon(index).slot;
        int equippedIndex = weapons.GetEquippedIndex(slot);
        if (equippedIndex >= 0)
        {
            return equippedIndex == index;
        }

        for (int i = 0; i < index; i++)
        {
            if (weapons.GetWeapon(i).slot == slot)
            {
                return false;
            }
        }

        return true;
    }

    void SetUpgradeInfo(Card card, GameplayConfig.UpgradeSettings upgrade, int level, string stat, float value, bool percentage = false)
    {
        string format = percentage ? "0%" : "0";
        string current = value.ToString(format);
        if (level >= upgrade.maxLevel)
        {
            card.Info.text = $"{stat} {current}\nLv{level}/{upgrade.maxLevel} · MAX";
            card.Button.interactable = false;
            return;
        }

        int price = upgrade.PriceAt(level);
        string next = (value + upgrade.amountPerLevel).ToString(format);
        card.Info.text = $"{stat} {current} > {next}\n{price} coins · Lv{level}/{upgrade.maxLevel}";
        if (wallet.Coins < price)
        {
            card.Info.text += "\nNot enough coins";
        }

        card.Button.interactable = wallet.Coins >= price;
    }

    // Card layout, top to bottom: icon, title, info (stat change and price).
    Card CreateCard(Transform parent, string name, Sprite iconSprite, Action onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = cardColor;
        var card = new Card
        {
            Button = go.GetComponent<Button>()
        };
        ArchiveUi.StyleButton(card.Button, true);
        card.Button.onClick.AddListener(() => onClick());
        var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        if (iconSprite != null)
        {
            var liner = ArchiveUi.Rect("Weapon icon liner", go.transform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -18), new Vector2(iconSize + 22, iconSize - 16));
            ArchiveUi.Surface(liner, "weapon-frame");
        }

        var iconRect = (RectTransform)icon.transform;
        iconRect.SetParent(go.transform, false);
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 1f);
        iconRect.pivot = new Vector2(0.5f, 1f);
        iconRect.anchoredPosition = new Vector2(0f, -12f);
        iconRect.sizeDelta = new Vector2(iconSize, iconSize);
        card.Icon = icon.GetComponent<Image>();
        card.Icon.sprite = iconSprite;
        card.Icon.preserveAspect = true;
        card.Icon.raycastTarget = false;
        card.Icon.enabled = iconSprite != null;
        card.Title = CreateLabel(go.transform, "Title", 24f, ArchiveUi.FontRole.Button);
        SetTopStrip(card.Title.rectTransform, 12f + iconSize + 4f, 32f);
        card.Info = CreateLabel(go.transform, "Info", 18f);
        SetTopStrip(card.Info.rectTransform, 12f + iconSize + 30f, 72f);
        return card;
    }

    // Stretches a rect across the card width at a fixed distance from the top.
    static void SetTopStrip(RectTransform rect, float top, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -top);
        rect.sizeDelta = new Vector2(0f, height);
    }

    static TextMeshProUGUI CreateLabel(Transform parent, string name, float fontSize, ArchiveUi.FontRole fontRole = ArchiveUi.FontRole.Body)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        ArchiveUi.ApplyFont(label, fontRole);
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = ArchiveUi.Paper;
        label.raycastTarget = false;
        return label;
    }
}

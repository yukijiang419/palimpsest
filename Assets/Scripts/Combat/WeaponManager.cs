using UnityEngine;
using Caffeine.Gameplay.Player;

public class WeaponManager : MonoBehaviour
{
    static readonly int AttackSpeedId = Animator.StringToHash("AttackSpeed");
    [SerializeField] PlayerInputReader inputReader;
    [SerializeField] Animator animator;
    [SerializeField]
    RuntimeAnimatorController baseController; // PlayerAnimator
    [SerializeField]
    RuntimeAnimatorController twoHandController; // TwohandedAnimator
    PlayerAttackController attackController;
    PlayerMoveController motor;
    int pendingSlot;
    [SerializeField]
    WeaponData[] allWeapons; // Four weapons' data
    [SerializeField]
    GameObject[] allModels; // Four weapons' models, order must match the above
    readonly int[] equippedIndex =
    {
        -1,
        -1
    };
    bool[] unlocked; // Weapons the player has obtained, indexed like allWeapons
    float[] damageBonus; // Damage bought in the shop, indexed like allWeapons
    int currentIndex = -1;
    bool modelHidden;
    public WeaponData Current => currentIndex >= 0 ? allWeapons[currentIndex] : null;
    public int WeaponCount => allWeapons.Length;
    public float CurrentDamageBonus => currentIndex >= 0 ? damageBonus[currentIndex] : 0f;

    void Awake()
    {
        if (inputReader == null)
        {
            inputReader = GetComponentInParent<PlayerInputReader>();
        }

        attackController = GetComponentInParent<PlayerAttackController>();
        motor = GetComponentInParent<PlayerMoveController>();
        unlocked = new bool[allWeapons.Length];
        damageBonus = new float[allWeapons.Length];
    }

    public WeaponData GetWeapon(int index) => allWeapons[index];
    public bool IsUnlocked(int index) => unlocked[index];
    // Index in allWeapons of the weapon held in a slot (1-based), or -1 if the slot is empty
    public int GetEquippedIndex(int slot) => slot >= 1 && slot <= equippedIndex.Length ? equippedIndex[slot - 1] : -1;
    public float GetDamage(int index) => allWeapons[index].damage + damageBonus[index];
    public float GetDamageBonus(int index) => damageBonus[index];
    // Puts the held weapon away, e.g. while both hands push a puzzle block.
    public void SetModelHidden(bool hidden)
    {
        modelHidden = hidden;
        if (currentIndex >= 0 && currentIndex < allModels.Length)
        {
            allModels[currentIndex].SetActive(!hidden);
        }
    }

    // Upgrades are stored here instead of on WeaponData, because editing the asset at runtime would persist in the editor.
    public void AddDamageBonus(int index, float amount)
    {
        damageBonus[index] += amount;
    }

    void Start()
    {
        Unlock(0); // Unlock the first weapon by default
        var quests = Caffeine.Gameplay.Config.ConfigSystem.Instance.Config.Quests;
        foreach (var definition in quests.Definitions)
        {
            if (definition.HasPuzzle && Caffeine.Gameplay.Puzzles.PuzzleRunState.HasWeaponReward(definition.Puzzle.puzzleIndex))
            {
                Unlock(System.Array.IndexOf(allWeapons, definition.Puzzle.rewardWeapon));
            }
        }

        int preferred = Caffeine.Gameplay.Puzzles.PuzzleRunState.PreferredSlot;
        Equip(preferred >= 1 && preferred <= equippedIndex.Length && equippedIndex[preferred - 1] >= 0 ? preferred : 1);
    }

    void Update()
    {
        if (inputReader == null)
        {
            return;
        }

        if (inputReader.IsGameplayBlocked)
        {
            pendingSlot = 0;
            return;
        }

        if (inputReader.WeaponSlot1Pressed && equippedIndex[0] >= 0)
        {
            pendingSlot = 1;
        }

        if (inputReader.WeaponSlot2Pressed && equippedIndex[1] >= 0)
        {
            pendingSlot = 2;
        }

        if (pendingSlot != 0 && !IsBusy())
        {
            int slot = pendingSlot;
            pendingSlot = 0;
            Equip(slot);
        }
    }

    public void CollectQuestReward(WeaponData reward)
    {
        int index = System.Array.IndexOf(allWeapons, reward);
        if (index < 0 || index >= allWeapons.Length || allWeapons[index] == null)
        {
            return;
        }

        UnlockData(index);
        pendingSlot = 0;
        Equip(allWeapons[index].slot);
    }

    // Unlocks a collected reward or restores it after scene travel.
    public void Unlock(int index)
    {
        int slot = UnlockData(index);
        if (slot == 0 || Current == null || Current.slot != slot)
        {
            return;
        }

        if (IsBusy())
        {
            pendingSlot = slot;
        }
        else
        {
            Equip(slot);
        }
    }

    private int UnlockData(int index)
    {
        if (index < 0 || index >= allWeapons.Length || allWeapons[index] == null)
        {
            return 0;
        }

        WeaponData weapon = allWeapons[index];
        unlocked[index] = true;
        if (weapon.slot < 1 || weapon.slot > equippedIndex.Length || equippedIndex[weapon.slot - 1] > index)
        {
            return 0;
        }

        equippedIndex[weapon.slot - 1] = index;
        return weapon.slot;
    }

    bool IsBusy() => (attackController != null && attackController.HasPendingAttack) || (motor != null && motor.IsSliding);
    void Equip(int slot)
    {
        int index = GetEquippedIndex(slot);
        if (index < 0)
        {
            return;
        }

        WeaponData w = allWeapons[index];
        attackController?.CancelAttack();
        currentIndex = index;
        // Only show the current weapon's model
        int show = currentIndex;
        for (int i = 0; i < allModels.Length; i++)
        {
            allModels[i].SetActive(i == show && !modelHidden);
        }

        // Switch the animation controller
        if (w.isTwoHanded && twoHandController != null)
        {
            animator.runtimeAnimatorController = twoHandController;
        }
        else
        {
            animator.runtimeAnimatorController = baseController;
        }

        // Must be set after swapping controllers, since swapping resets all parameters
        animator.SetFloat(AttackSpeedId, w.attackSpeed > 0f ? w.attackSpeed : 1f);
    }
    [System.Serializable] public sealed class WeaponState { public string id; public bool unlocked; public float bonus; }
    [System.Serializable] public sealed class SaveState { public WeaponState[] weapons; public string[] equipped; public int slot; }
    public SaveState CaptureState()
    {
        var s = new SaveState { weapons = new WeaponState[allWeapons.Length], equipped = new string[2], slot = Current != null ? Current.slot : 1 };
        for (int i=0; i<allWeapons.Length; i++) s.weapons[i] = new WeaponState { id = allWeapons[i].saveId, unlocked = unlocked[i], bonus = damageBonus[i] };
        for (int i=0; i<2; i++) if (equippedIndex[i]>=0) s.equipped[i] = allWeapons[equippedIndex[i]].saveId;
        return s;
    }
    public void RestoreState(SaveState s)
    {
        System.Array.Clear(unlocked,0,unlocked.Length); System.Array.Clear(damageBonus,0,damageBonus.Length);
        equippedIndex[0] = equippedIndex[1] = -1;
        foreach (var w in s.weapons) for(int i=0;i<allWeapons.Length;i++) if(allWeapons[i].saveId == w.id) { unlocked[i]=w.unlocked; damageBonus[i]=w.bonus; }
        for(int slot=0;slot<2;slot++) for(int i=0;i<allWeapons.Length;i++) if(allWeapons[i].saveId==s.equipped[slot] && unlocked[i]) equippedIndex[slot]=i;
        pendingSlot = 0; modelHidden = false; Equip(s.slot);
    }

}

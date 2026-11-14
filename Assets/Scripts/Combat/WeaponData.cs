using UnityEngine;

[CreateAssetMenu(menuName = "Game/Weapon")]
public class WeaponData : ScriptableObject
{
    public string saveId;
    public string weaponName;
    public Sprite icon;           // Shown in the shop
    public int slot;              // 1 = one-handed, 2 = two-handed
    public bool isTwoHanded;
    public float damage;
    public float range;           // Attack distance
    public float arcDegrees = 90f; // Attack arc width, centered on the aim direction
    public float attackSpeed;
    [Tooltip("Impact point of each combo swing (0-1); empty uses the gameplay config.")]
    public float[] comboImpactTimes;
}

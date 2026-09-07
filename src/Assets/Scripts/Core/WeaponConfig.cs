// WeaponConfig.cs —— 阵营武器配置数据（对位 src3d/core/weaponConfig.js）
// F2 修正：必须含 baseDamage（对基地伤害），数值照抄 Web 版
//   天庭能量炮 blue: dmg24/range25/cd1.0/aoe12/baseDamage150/speed40/falloff0.5/color ffd700
//   玄朝能量机枪 red: dmg8/range20/cd0.45/aoe0/baseDamage15/speed80/falloff0/color ff4444
using UnityEngine;

namespace Liuhuo.Core
{
    // Web 版 weaponConfig.js 的 C# 等价物（ScriptableObject 数据驱动，改数值免重编译）
    [CreateAssetMenu(fileName = "WeaponConfig", menuName = "Liuhuo/WeaponConfig")]
    public class WeaponConfig : ScriptableObject
    {
        [Header("基础")] public string weaponName = "能量炮";
        public float damage = 24f;           // 对兵单发伤害——T7 对齐 Web weaponConfig.js 天庭 damage=24（原 50 致战斗数值与 Web 失衡）
        public float attackRange = 25f;      // 攻击范围 m
        public float attackCooldown = 1.0f;  // 攻击间隔 s —— T7 对齐 src3d 天庭 cd1.0（原3.0）
        public float projectileSpeed = 40f;  // 弹体速度 m/s
        public float aoeRadius = 12f;        // AOE 半径（玄朝机枪=0 单体）—— T7 对齐 src3d 天庭 aoe12（原5）
        public float aoeDamageFalloff = 0.5f; // 范围伤害衰减系数

        [Header("F2 对基地伤害")] public float baseDamage = 150f; // 对基地伤害——T7 对齐 Web weaponConfig.js 天庭能量炮 baseDamage=150（原 100 致基地血量失衡）

        [Header("视觉/特效")] public Color color = new Color(1f, 0.84f, 0f); // ffd700
        public string muzzleFlash = "cannon_gold";
        public string hitEffect = "explosion_gold";

        // 阵营枚举（对位 Web team: blue=天庭/red=玄朝）
        public enum Team { Blue, Red }
        public Team team = Team.Blue;

        // 对兵伤害结算
        public float GetSoldierDamage() { return damage; }
        // 对基地伤害结算（F2：与对兵伤害分道，基地血推动用 baseDamage）
        public float GetBaseDamage() { return baseDamage; }

        // 玄朝机枪变体（F2 baseDamage=15 印证同源差异）
        public static WeaponConfig CreateMachineGun()
        {
            var cfg = ScriptableObject.CreateInstance<WeaponConfig>();
            cfg.weaponName = "能量机枪"; cfg.damage = 8f; cfg.attackRange = 20f;
            cfg.attackCooldown = 0.45f; cfg.projectileSpeed = 80f; cfg.aoeRadius = 0f;  // T7 对齐 src3d 玄朝 cd0.45（原0.2）
            cfg.baseDamage = 15f; // F2
            cfg.color = new Color(1f, 0.27f, 0.27f); // ff4444
            cfg.team = Team.Red;
            return cfg;
        }
    }
}

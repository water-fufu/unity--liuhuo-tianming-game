// SoldierTag.cs —— 士兵标记组件（对位 Web 士兵对象）
// 供 HolyJudgment/TongsiUltimate/战斗层统一识别士兵 + 提供即死/扣血接口。
// 对应 Web 版"instanceIndex 恒 -1 陷阱 → 用 sol.id 作稳定 ID"；Unity 侧天然稳定 = 每士兵一个 GameObject + 本组件。
using UnityEngine;

namespace Liuhuo.Core
{
    public class SoldierTag : MonoBehaviour
    {
        [Header("士兵身份")] public int soldierId = -1;        // 稳定 ID（对位 Web sol.id，绕过 instanceIndex）
        public int team = 0;                                   // 0=天庭(蓝)/1=玄朝(红)
        public float maxHp = 100f;
        public float hp = 100f;

        [Header("F3/F4：即死通路")] public bool isAlive = true;

        public void TakeDamage(float dmg)
        {
            if (!isAlive) return;
            hp -= dmg;
            if (hp <= 0) KillInstantly();
        }

        // 圣裁/同死 波内即死（F3：非数值伤害，直接判定死亡）
        public void KillInstantly()
        {
            if (!isAlive) return;
            isAlive = false;
            gameObject.SetActive(false); // 死亡回收/隐藏（对位 Web 阵亡处理）
            // TODO: 触发死亡特效/尸体（阶段2 接 VFXManager）
        }

        public float GetHp() { return hp; }
        public float GetHpNormalized() => maxHp <= 0 ? 0 : hp / maxHp;
    }
}

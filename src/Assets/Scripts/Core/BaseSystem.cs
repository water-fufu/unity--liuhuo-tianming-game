// BaseSystem.cs —— 基地血量（对位 src3d/core/BaseSystem.js / battleStage.js 基地逻辑）
// F2 关联：基地 10000 血靠 baseDamage 推动；对兵用 damage，对基地用 baseDamage，两条结算路径分离。
using UnityEngine;
using UnityEngine.Events;
using Liuhuo.Audio;   // P1-4 基地受击 -> PlayAlarm(警报) 接线

namespace Liuhuo.Core
{
    public class BaseSystem : MonoBehaviour
    {
        [Header("F2 基地血量（对位 BASE_HP_MAX=10000）")]
        public float maxHp = 10000f;
        public float hp = 10000f;

        [SerializeField] private int team = 0; // 0=天庭(蓝)/1=玄朝(红)

        [Header("事件回调")]
        public UnityEvent<float> onDamaged;     // param=当前剩余血量
        public UnityEvent onDestroyed;

        public void ResetBase()
        {
            hp = maxHp;
            onDamaged?.Invoke(hp);
        }

        // F2 关键：接受"一次攻击"的 (对兵伤害, 对基地伤害)，基地结算只用 baseDamage
        public void ApplyHit(float soldierDamage, float baseDamage)
        {
            // 基地被弹体命中：只扣 baseDamage（对兵 damage 不适用到基地）
            hp = Mathf.Max(0, hp - baseDamage);
            onDamaged?.Invoke(hp);
            // P1-4 接线：基地受击 -> 警报 alarm_siren.mp3（PlayAlarm 内置 100ms 节流，50v50 高频命中不炸音）
            if (AudioController.Instance != null) AudioController.Instance.PlayAlarm();
            // TODO: 基地受击反馈（阶段2 粒子/震动）
            if (hp <= 0 && !IsDestroyed)
            {
                IsDestroyed = true;
                onDestroyed?.Invoke();
            }
        }

        public bool IsDestroyed { get; private set; }
        public int Team => team;
        public float GetHpNormalized() => maxHp <= 0 ? 0 : hp / maxHp;
        public void SetTeam(int t) { team = t; }
    }
}

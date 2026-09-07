// HolyJudgment.cs —— 圣裁大招（对位 src3d/fx/HolyJudgment.js）
// F3 修正：圣裁核心 = "波内士兵即死"（OverlapSphere），非 200 数值伤害；
//   weaponConfig 的 damage200 只是基础命中兜底，不是主机制。
//   LIFESPAN=6.0，波前半径从≈69 经 elapsed/LIFESPAN p² 缓动扩到满图 DOME_MAX_SCALE=500。
using System.Collections.Generic;
using Liuhuo.Core;   // SoldierTag（F3 波内即死）
using UnityEngine;

namespace Liuhuo.FX
{
    public class HolyJudgment : MonoBehaviour
    {
        [Header("F3 关键参数（照抄 HolyJudgment.js）")]
        public float lifespan = 6.0f;      // LIFESPAN=6.0（同死是 7.0，勿统一）
        public float domeMaxScale = 500f;  // DOME_MAX_SCALE=500
        public float domeRing = 0.5f;      // DOME_RING=0.5
        public float startRadius = 0f;     // B3 修正：从 0 起（Web 修复前 bug 值 69），波前从中心扩满而非瞬间 69

        [Header("圣裁时间轴")]
        public float elapsed = 0f;          // 用于 elapsed/LIFESPAN 的 p² 缓动
        public float checkInterval = 1.0f;  // 每秒 1 轮扫杀
        private float _nextCheck = 0f;

        // 波内士兵集合（由调用方注入，对应 Web 侧场景 soldiers）
        private List<GameObject> _soldiers = new List<GameObject>();

        public void Init(List<GameObject> soldiers) { _soldiers = soldiers; }

        // F3: OverlapSphere 波内即死——非数值伤害
        void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / lifespan); // p² 缓动进度
            // 当前波半径（从 startRadius 扩到满图）
            float waveRadius = Mathf.Lerp(startRadius, domeMaxScale, t * t);

            // 每 1s 扫杀一轮：半径内士兵直接即死
            if (elapsed >= _nextCheck)
            {
                _nextCheck = elapsed + checkInterval;
                KillWave(waveRadius);
            }

            if (elapsed >= lifespan) { Destroy(gameObject); } // 波次结束自毁
        }

        // F3 核心：OverlapSphere 检测 + 即死
        void KillWave(float radius)
        {
            Vector3 center = transform.position;
            Collider[] hit = Physics.OverlapSphere(center, radius);
            foreach (Collider c in hit)
            {
                var soldier = c.GetComponentInParent<SoldierTag>();
                if (soldier != null) soldier.KillInstantly(); // 波内即死（非扣血）
            }
        }

        // F3 兜底：当此处未附带 SoldierTag 时，也允许托管方直接喂士兵列表
        void KillSoldiersInList()
        {
            foreach (var s in _soldiers)
            {
                if (s == null) continue;
                float t = Mathf.Clamp01(elapsed / lifespan);
                float waveRadius = Mathf.Lerp(startRadius, domeMaxScale, t * t);
                if (Vector3.Distance(s.transform.position, transform.position) < waveRadius)
                {
                    s.GetComponent<SoldierTag>()?.KillInstantly();
                }
            }
        }

        // 供反射/调试：GetWaveRadius 回读当前波半径
        public float GetWaveRadius()
        {
            float t = Mathf.Clamp01(elapsed / lifespan);
            return Mathf.Lerp(startRadius, domeMaxScale, t * t);
        }
    }
}

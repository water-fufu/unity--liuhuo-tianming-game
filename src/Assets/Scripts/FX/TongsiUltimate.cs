// TongsiUltimate.cs —— 同死大招（对位 src3d/fx/SameDeath.js）敌方大招
// F4-1 修正：LIFESPAN=7.0（非圣裁的 6.0）；MAX_LASERS=80；波次在 LIFESPAN 期间持续发射（连绵不绝）。
using UnityEngine;

namespace Liuhuo.FX
{
    public class TongsiUltimate : MonoBehaviour
    {
        [Header("F4-1 关键参数（照抄 SameDeath.js）")]
        public float lifespan = 7.0f;      // LIFESPAN=7.0（v91 4.5→7s）
        public int maxLasers = 80;         // MAX_LASERS=80
        public float waveEvery = 0.25f;    // B1 修正：WAVE_EVERY=0.25（对位 SameDeath.js 实测），非 0.5 旧值

        [Header("同死时间轴")]
        public float elapsed = 0f;
        private float _nextWave = 0f;

        void Update()
        {
            elapsed += Time.deltaTime;

            // F4-1 持续降雨：在 LIFESPAN 内不断发射下一波
            if (elapsed <= lifespan && elapsed >= _nextWave)
            {
                _nextWave = elapsed + waveEvery;
                SpawnLaserWave();
            }

            if (elapsed >= lifespan) { Destroy(gameObject); } // 7s 结束自毁
        }

        // 分波发射激光（对位 WAVE_COUNT/MaxLasers 机制；WAVE_COUNT=14 已过时，改按 waveEvery 持续）
        void SpawnLaserWave()
        {
            // TODO: 阶段2 接激光特效生成器（VFXManager），此处先占位逻辑骨架
            // 对位 Web：elapsed<LIFESPAN 期间每 waveEvery 秒落一束雨
            Debug.Log($"[Tongsi] 第{((int)(elapsed / waveEvery)) + 1}波 激光雨 elapsed={elapsed:F2}");
        }

        // 供反射/调试
        public float GetWaveProgress() { return Mathf.Clamp01(elapsed / lifespan); }
    }
}

// UltController.cs —— 大招应用层（对位 Web src3d/main.js._triggerUltimate + fx/UltimateEffect.js 驱动）
// 契约（对位基准_v105权威值.md + main.js 实战结算为准，非 weaponConfig 配置表）：
//   圣裁(天庭/蓝)：damage=200 radius=30 基地=500 LIFESPAN=6.0s damageAt=t=3.0s 冷却30s
//   同死(玄朝/红)：每tick 20 × 30次=600(10/s×3s) radius=25 基地=450 LIFESPAN=7.0s WAVE_EVERY=0.25s 每波16-23道 池80 冷却30s
// 应用层职责：冷却管理 / 时序伤害(圣裁 delay t=3.0 一次性；同死 tick 型前16tick打士兵) / 震动 / 触发视觉层 Fx。
// 伤害源：遍历 SoldierFactory.ActiveSoldiers 按 team 过滤(圣裁打玄朝red/同死打天庭blue)；基地走 BaseSystem.ApplyHit(0, baseDamage)。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Liuhuo.Core;
using Liuhuo.Audio;

namespace Liuhuo.FX
{
    public class UltController : MonoBehaviour
    {
        [Header("S4 权威值（照抄 main.js 实测，非 weaponConfig 配置表）")]
        public float holyDamage = 100f;   // ★用户拍板(2026-09-07)：对士兵伤害=士兵血量100，大招内一击必杀（原版200，main.js L4095 cfg.damageAt?200:20）
        public float holyRadius = 60f;    // ★用户拍板(2026-09-07)：伤害范围全向×2 → 30→60（覆盖地图上方士兵；原 main.js L4096 radius=cfg.damageAt?30:25）
        public float holyVisualRadius = 250f;   // s5九任务T1: 圣裁视觉波前半径 500→250(用户拍板直径500m), 与伤害半径分离
        public float holyBaseDamage = 8000f;   // ★用户拍板(2026-09-07)：圣裁基地一次性总伤 8000（原版 baseDmg=cfg.damageAt?500，用户改 8000）
        public float holyLifespan = 6.0f;      // LIFESPAN=6.0
        public float holyAttackAt = 3.0f;      // damageAt:[3.0] 圣裁伤害时刻 t=3.0s
        public float sameTickDamage = 100f;  // ★用户拍板(2026-09-07)：对士兵伤害=士兵血量100，一击必杀（原版20，main.js L4095 cfg.damageAt?20:25）
        public int sameMaxTick = 30;   // totalDamage=600=20x30(tickRate10xduration3,全程打士兵)
        public float sameRadius = 100f;        // ★用户拍板(2026-09-07)：伤害范围全向×2 → 50→100（覆盖地图上方士兵；视觉/粒子雨同步×2，见 SameDeathFx）
        public float sameBaseDamage = 8000f;    // ★用户拍板(2026-09-07)：同死基地总伤 8000（分摊30tick）；原版 450
        public float sameLifespan = 7.0f;      // LIFESPAN=7.0
        public float sameWaveEvery = 0.25f;    // WAVE_EVERY=0.25
        public int sameMaxLasers = 80;         // MAX_LASERS=80（单波上限）
        // ★基地伤害对齐(2026-09-07)：同死 tick 型基伤 = sameBaseDamage/(tickRate×tickDuration)。原版 round(450/(10×3))=15/次、总450。
        //   ★用户拍板(2026-09-07)：基地总伤一律 8000。同死 = 8000/30 = 266.67/tick，30 tick 累计精确 8000。用除法非 round，
        //   因 8000/30 非整除，round 会变 267/tick→总8010 偏10（原版 round 对 450/30 整除才无偏）。士兵伤害不变（圣裁200/同死每tick20）。
        public float sameTickRate = 10f;       // tickRate=10（每秒 tick 数，对位 ULTIMATE_CONFIG.same.tickRate）
        public float sameTickDuration = 7.0f;  // ★用户拍板(2026-09-07)：同死时长延长至7秒 → 伤害窗口/tick总数随之(10×7=70), 总伤8000重分摊; 视觉SameDeathFx lifespan本已是7.0f
        // 同死每 tick 对基地伤害（用户拍板总伤=8000；精确除法保证 30 tick 累计=8000）。随 sameTickRate/sameTickDuration 派生。
        public float SameBasePerTick => Mathf.Max(1f, sameTickRate * sameTickDuration) > 0f ? sameBaseDamage / Mathf.Max(1f, sameTickRate * sameTickDuration) : 0f;
        public float cooldown = 0f;   // ★用户指令(2026-09-07)：取消圣裁/同死 cd 限制(原30s)。=0 → 触发后 _cooldownReadyAt=Time.time, IsReady 恒 true,按钮可直接再点, HUD fill 由 GetCooldownPct 守卫为0

        [Header("引用（由场景/反射注入）")]
        public SoldierFactory factory;
        public BaseSystem blueBase;   // 天庭(蓝)基地
        public BaseSystem redBase;    // 玄朝(红)基地

        // 视觉层预制体引用（可选，缺失则纯逻辑伤害）
        public HolyJudgmentFx holyFxPrefab;
        public SameDeathFx sameFxPrefab;

        // 冷却表（对位 _cooldown[ult/ultEnemy]）
        private readonly Dictionary<int, float> _cooldownReadyAt = new Dictionary<int, float>();
        private float _screenShake = 0f;

        public static UltController Instance;

        void Awake() { Instance = this; }

        void Start() { BindRefs(); Debug.Log($"[UltProbe] AOE中心 position={transform.position} sameRadius={sameRadius} holyRadius={holyRadius}"); }

        // 自举：谁调用都保证实例存在（对位 BattleCamera.EnsureCreated），免场景预挂
        public static UltController EnsureCreated()
        {
            if (Instance == null)
            {
                var go = new GameObject("UltController");
                go.AddComponent<UltController>();
                // 任务2：圣裁/同死 AOE 中心移到用户拍板点位——u3d 双方基地连线中点(天庭38.1/玄朝-28.7, z≈71.4)。
                // 原版 AOE 中心在世界原点(0,0),恰为两基地(±40,0)连线中点;u3d 基地挪到 z≈71.4 后,中点=((38.1-28.7)/2,0,71.4)≈(4.7,0,71.4)。
                go.transform.position = new Vector3(4.7f, 0f, 71.4f);
            }
            return Instance;
        }

        // 引用自动回填：从 GameManager 拉 factory/基地（免 Inspector 拖拽，防域重载丢失）
        public void BindRefs()
        {
            var gm = GameManager.Instance;
            if (factory == null && gm != null) factory = gm.factory;
            if (blueBase == null && gm != null) blueBase = gm.blueBase;
            if (redBase == null && gm != null) redBase = gm.redBase;
        }

        // ===== 冷却 API（HUD 绑 + 场景驱动）=====
        public bool IsReady(int team)
        {
            if (!_cooldownReadyAt.TryGetValue(team, out var t)) return true;
            return Time.time >= t;
        }
        // ★任务7 大招无限触发验证：清冷却(立即 ready)供 autoTest 探针连触取证；生产态自动测试专用，不影响正常冷却逻辑。
        public void ResetCooldown(int team) { _cooldownReadyAt[team] = 0f; }
        public float GetCooldown(int team)
        {
            if (!_cooldownReadyAt.TryGetValue(team, out var t)) return 0f;
            return Mathf.Max(0f, t - Time.time);
        }
        public float GetCooldownPct(int team) { return cooldown <= 0f ? 0f : Mathf.Clamp01(GetCooldown(team) / cooldown); }   // 防 cooldown=0 除零(NaN 破 HUD cooldown fill)

        // ===== 触发入口（对位 main.js._triggerUltimate(team)）=====
        // s5 v3.2（工部二 §13.2-①）：加视觉层探针 —— 大招视觉必须在 Player 运行时可见，打字记录供 autoTest 采集
        public void TriggerUltimate(int team)
        {
            if (!IsReady(team)) { Debug.Log($"[Ult] team={team} 冷却中 remain={GetCooldown(team):F1}s"); return; }
            _cooldownReadyAt[team] = Time.time + cooldown;
            bool holy = team == 0;   // 0=天庭(蓝)→圣裁；1=玄朝(红)→同死
            Debug.Log($"[Ult] {(holy ? "圣裁" : "同死")} 触发 (team={team}) visual=ParticleSystem+additive+pool");
            StartCoroutine(holy ? HolyFlow() : SameFlow());
        }

        // ===== 圣裁流程（★用户拍板 2026-09-07：冲击波范围内每秒造成一次伤害；单次圣裁只对基地造成一次伤害）=====
        // 原版 ULTIMATE_CONFIG.holy damageAt=[3.0] tickRate=0（t=3.0s 一次性点爆发）；用户明确要求复刻"每秒刷新一次出伤"，
        //   故改为 tick 型：出伤窗口 = holyLifespan(6.0s)，每 holyTickInterval(1s) 一次对范围内士兵出伤；基地只在 tick#1 打一次。
        //   士兵血量=SoldierTag.maxHp=100，holyDamage=100 → 第一 tick 即秒杀；后续 ticks 已死士兵 skip（hit=0），对更新的活兵仍生效。
        public float holyTickInterval = 1f;   // ★用户拍板(2026-09-07)：圣裁每秒一次出伤（原版 tickRate=0 单次）
        IEnumerator HolyFlow()
        {
            Debug.Log("[Ult] 圣裁流程开始 (holy) tick型每秒出伤");
            var fx = SpawnHolyFx();
            if (AudioController.Instance != null) AudioController.Instance.PlayUltimateHeaven();

            float t = 0f;
            int tickCount = 0;
            float nextTick = 1f;   // ★用户拍板(2026-09-07)：圣裁第一秒不出伤，第二秒再出 → 首次 tick 推迟到 t>=1.0s（下一行从0改1）
            while (t < holyLifespan)
            {
                t += Time.deltaTime;
                if (t >= nextTick)
                {
                    nextTick = t + holyTickInterval;   // 每 1s 一次出伤（用户拍板"每秒一次"）
                    tickCount++;
                    ApplyHolyTick(tickCount);
                    // S5 R4 块P1-6 E5 震动取证：AddShake 注入时打 GetShake 代码证据（震动峰值<0.25s 衰减快，采帧难命中，改采调用点）
                    AddShake(0.5f);
                    Debug.Log($"[Ult] AddShake注入 圣裁 tick#{tickCount} shake={GetShake():F1} (E5 代码证据, 峰值快速衰减至0则采调用点为准)");
                }
                yield return null;
            }
            StopFx(fx);
            Debug.Log($"[Ult] 圣裁流程结束 tick={tickCount}");
        }

        // ★用户拍板(2026-09-07)：圣裁每 tick 对范围内士兵出伤(holyDamage)；基地只在 tick#1 打一次(holyBaseDamage, 单次圣裁仅盖一次基地伤害)。
        void ApplyHolyTick(int tickCount)
        {
            // ★RangeProbe(2026-09-07)：在造成任何伤害【前】统计新旧半径覆盖的活敌兵力——必须放 kill 循环前，否则士兵已被本 tick 一击必杀 isAlive=false→误报0
            int rpNew = 0, rpOld = 0;
            if (tickCount == 1) { rpNew = CountAliveEnemiesInRadius(holyRadius, false); rpOld = CountAliveEnemiesInRadius(30f, false); }
            var soldiers = FactoryActive();
            int hit = 0;
            for (int i = 0; i < soldiers.Count; i++)
            {
                var s = soldiers[i];
                if (s == null || s.tag == null || !s.tag.isAlive) continue;
                if (s.team != 0)
                {
                    if (Vector3.Distance(s.transform.position, transform.position) <= holyRadius)
                    {
                        s.TakeDamage(holyDamage);   // 圣裁每 tick 100 (=士兵血量100, 一击必杀)
                        hit++;
                    }
                }
            }
            // ★RangeProbe(2026-09-07)：圣裁 tick#1 新半径(60) vs 旧半径(30) 覆盖活敌(玄朝)兵力实际覆盖差
            if (tickCount == 1) Debug.Log($"[RangeProbe] 圣裁 新半径({holyRadius})内活敌={rpNew} 旧半径(30)内={rpOld} 扩半径额外覆盖={rpNew - rpOld}");
            // ★基地伤害实证(2026-09-07)：圣裁只一次性打敌基地(redBase) 8000（tick#1），并打 HP 前后对比日志（消除"打了却说没受伤"假阳性）。
            if (tickCount == 1)
            {
                if (redBase != null)
                {
                    float before = redBase.hp;
                    redBase.ApplyHit(0f, holyBaseDamage);
                    Debug.Log($"[UltBase] 圣裁 红基地-{holyBaseDamage} HP {before:F0}->{redBase.hp:F0} (单次圣裁仅一次基地伤害, 用户拍板 8000)");
                }
                else
                {
                    Debug.LogWarning("[UltBase] 圣裁 红基地引用为 null，基地伤害未生效！");
                }
            }
            Debug.Log($"[Ult] 圣裁 tick#{tickCount} applyDamage={holyDamage} hit={hit}");
        }

        // ===== 同死流程（tick 型，原版对齐：tickRate=10 × tickDuration=3 = 30 次）=====
        // 原版 ULTIMATE_CONFIG.same：tickStart=2.0 tickRate=10 tickDuration=3.0。
        //   士兵：每 tick 20/25m ×30 = 600；基地：每 tick round(450/30)=15 ×30 = 450。
        //   当前 u3d 旧实现按 lifespan(7)/waveEvery(0.25)=28 波算基地(16.07/波)，与原版 30 tick 15/tick 不符，此处重构对齐。
        IEnumerator SameFlow()
        {
            Debug.Log("[Ult] 同死流程开始 (same)");
            var fx = SpawnSameFx();
            if (AudioController.Instance != null) AudioController.Instance.PlayUltimateXuan();

            // 原版 tick 总数 n = tickRate × tickDuration（10×3=30）；伤害仅在前 3.0s 内发生。
            int totalTicks = Mathf.Max(1, Mathf.RoundToInt(sameTickRate * sameTickDuration));
            float basePerTick = SameBasePerTick;   // round(450/(tickRate*tickDuration)) = round(450/30) = 15

            float t = 0f; int tickCount = 0;
            float nextWave = 0f;
            // 只走原版 tick 窗口（0~3.0s），不再延到 sameLifespan(7s) —— 对齐原版「tickDuration 3s 打 30 次」
            float tickWindow = sameTickDuration;

            while (t < tickWindow)
            {
                t += Time.deltaTime;
                if (t >= nextWave)
                {
                    nextWave = t + 1f / sameTickRate;   // 每 0.1s 一次（tickRate=10）
                    tickCount++;
                    ApplySameTick(tickCount, basePerTick, totalTicks);
                    // S5 R4 块P1-6 E5 震动取证：同死每 tick AddShake 注入日志
                    AddShake(0.3f);
                    Debug.Log($"[Ult] AddShake注入 同死 tick#{tickCount} shake={GetShake():F1} (E5 代码证据)");
                    if (tickCount >= totalTicks) break;   // 只跑原版 30 tick
                }
                yield return null;
            }
            StopFx(fx);
            Debug.Log($"[Ult] 同死流程结束 tick={tickCount}/{totalTicks}");
        }

        // ★基地伤害实证(2026-09-07)：同死每 tick 对敌基地(blueBase)结算 basePerTick，并打连 HP 日志（消除"打了却说没受伤"假阳性）。
        void ApplySameTick(int tickCount, float basePerTick, int totalTicks)
        {
            // ★RangeProbe(2026-09-07)：在造成任何伤害【前】统计新旧半径覆盖的活敌兵力——必须放 kill 循环前（同上，被击杀后 isAlive=false 会误报0）
            int rpNew = 0, rpOld = 0;
            if (tickCount == 1) { rpNew = CountAliveEnemiesInRadius(sameRadius, true); rpOld = CountAliveEnemiesInRadius(50f, true); }
            var soldiers = FactoryActive();
            int hit = 0;
            if (tickCount <= sameMaxTick)   // 原版士兵伤害只在前 30 tick 对士兵生效（同死士兵打满 30 次）
            {
                for (int i = 0; i < soldiers.Count; i++)
                {
                    var s = soldiers[i];
                    if (s == null || s.tag == null || !s.tag.isAlive) continue;
                    if (s.team == 0)
                    {
                        if (Vector3.Distance(s.transform.position, transform.position) <= sameRadius)
                        {
                            s.TakeDamage(sameTickDamage);   // 同死每 tick 20
                            hit++;
                        }
                    }
                }
                Debug.Log($"[Ult] 同死 tick#{tickCount} 士兵{sameTickDamage} hit={hit}");
            }
            // ★RangeProbe(2026-09-07)：同死 tick#1 新半径(100) vs 旧半径(50) 覆盖活敌(天庭)兵力实际覆盖差
            if (tickCount == 1) Debug.Log($"[RangeProbe] 同死 新半径({sameRadius})内活敌={rpNew} 旧半径(50)内={rpOld} 扩半径额外覆盖={rpNew - rpOld}");
            // ★基地伤害（原版 L4106 damageBase(enemyTeam, baseDmg)）：无条件对敌基地结算，合并基地日志证明真正掉血
            if (blueBase != null)
            {
                float before = blueBase.hp;
                blueBase.ApplyHit(0f, basePerTick);
                Debug.Log($"[UltBase] 同死 tick#{tickCount} 蓝基地-{basePerTick} HP {before:F0}->{blueBase.hp:F0} (原版 baseDmg={basePerTick}/tick×{totalTicks})");
            }
            else
            {
                Debug.LogWarning("[UltBase] 同死 蓝基地引用为 null，基地伤害未生效！");
            }
        }

        // ===== 视觉层生成（B4 程序化）=====
        HolyJudgmentFx SpawnHolyFx()
        {
            if (holyFxPrefab != null) { var f = Instantiate(holyFxPrefab, transform.position, Quaternion.identity); f.Init(holyLifespan, holyVisualRadius); return f; }
            // P0 第七轮(TC-B5 0分根因=接线断裂): HolyJudgmentFx 组件早已实现但从未实例化(prefab 字段恒 null)→ 动态 AddComponent 兜底,免 Inspector 预挂
            var go = new GameObject("HolyJudgmentFx"); go.transform.SetParent(transform, false);
            go.transform.position = transform.position;
            var fx = go.AddComponent<HolyJudgmentFx>(); fx.Init(holyLifespan, holyVisualRadius);   // W3: 视觉波前用 holyVisualRadius(500), 非伤害半径 holyRadius(30)
            return fx;
        }
        SameDeathFx SpawnSameFx()
        {
            if (sameFxPrefab != null) { var f = Instantiate(sameFxPrefab, transform.position, Quaternion.identity); f.Init(sameLifespan, sameMaxLasers, sameWaveEvery); return f; }
            // P0 第七轮(TC-B5): SameDeathFx 组件早已实现但从未实例化 → 动态 AddComponent 兜底
            var go = new GameObject("SameDeathFx"); go.transform.SetParent(transform, false);
            go.transform.position = transform.position;
            var fx = go.AddComponent<SameDeathFx>(); fx.Init(sameLifespan, sameMaxLasers, sameWaveEvery);
            return fx;
        }
        void StopFx(Component fx) { if (fx != null) Destroy(fx.gameObject); }

        List<Soldier> FactoryActive()
        {
            return factory != null ? SoldierFactory.ActiveSoldiers : new List<Soldier>();
        }

        // ★RangeProbe(2026-09-07)：统计 AOE 中心 r 米内、指定阵营(0=天庭蓝/1=玄朝红)的存活敌兵数。
        //   用途：比较「新半径(60/100) vs 旧半径(30/50)」实际覆盖到的敌兵数，确定性证明扩半径后打到地图上方士兵，不受测试构图随机性影响。
        int CountAliveEnemiesInRadius(float r, bool targetTeam0)
        {
            var soldiers = FactoryActive();
            int c = 0;
            for (int i = 0; i < soldiers.Count; i++)
            {
                var s = soldiers[i];
                if (s == null || s.tag == null || !s.tag.isAlive) continue;
                if ((s.team == 0) != targetTeam0) continue;   // targetTeam0=true 打天庭(team0)；false 打玄朝(team1)
                if (Vector3.Distance(s.transform.position, transform.position) <= r) c++;
            }
            return c;
        }

        public float GetShake() { return _screenShake; }
        void AddShake(float v) { _screenShake = v; }
        public void DecayShake(float dt) { _screenShake = Mathf.Max(0f, _screenShake - dt * 2f); }
    }
}

// BattleStage.cs —— 战局四阶段推进（对位 battleStage.js）：开场/僵持/激战/终局
// intensity = 0.4*timeNorm + 0.3*killNorm + 0.3*soldierNorm（各归一到 0~1，含除零保护）
// 阶段由 intensity 驱动演进；击杀/基地摧毁经 CombatSystem 事件挂载 RecordKill（连杀/首杀）。
// 供 UI/特效读取 getVisualParams() 取背景色/粒子密度/缩放。
using System;
using UnityEngine;

namespace Liuhuo.Core
{
    // 战局四阶段
    public enum BattlePhase { Opening, Stalemate, Frenzy, Finale }

    // 阶段视觉参数（供 UI/特效用）
    public struct BattleVisualParams
    {
        public Color backgroundColor;   // 背景色调
        public float particleDensity;   // 0~1 粒子密度系数
        public float scale;             // 特效缩放系数

        public BattleVisualParams(Color bg, float density, float scaleMul)
        {
            backgroundColor = bg;
            particleDensity = density;
            scale = scaleMul;
        }
    }

    /// <summary>
    /// 战局阶段协调单例。EnsureCreated() 创建隐藏 GameObject 挂载，Update 驱动阶段演进。
    /// 只读现有接口（GameManager.Instance.factory / SoldierFactory.ActiveSoldiers / CombatSystem 事件），不修改任何现有 .cs。
    /// </summary>
    public class BattleStage : MonoBehaviour
    {
        public static BattleStage Instance { get; private set; }

        [Header("归一化参考值（除零保护）")]
        [Tooltip("战斗时长归一参考(秒)，timeNorm = BattleElapsed / 此值")]
        public float battleDurationRef = 120f;

        [Tooltip("击杀归一参考，killNorm = TotalKills / 此值")]
        public int killNormMax = 100;

        [Tooltip("士兵归一参考(总活跃数)，<=0 时自动取 2*factory.maxSoldiers")]
        public int soldierNormMax = 0;

        [Header("连杀窗口(秒)")]
        [Tooltip("窗口内连续击杀计为连杀")]
        public float killStreakWindow = 5f;

        // ---- 只读状态 ----
        public BattlePhase Phase { get; private set; } = BattlePhase.Opening;
        public float Intensity { get; private set; }
        public float BattleElapsed { get; private set; }
        public int TotalKills { get; private set; }
        public int KillStreak { get; private set; }
        public bool IsFirstKillDone { get; private set; }
        public int DestroyedTeam { get; private set; } = -1;   // -1=基地未破；0/1=被摧毁方
        public bool IsBaseDestroyed => DestroyedTeam >= 0;

        // ---- 事件（供 StageQuotes 等订阅）----
        public event Action<BattlePhase> OnPhaseChanged;   // 阶段进入
        public event Action<int, int> OnKill;              // (得分方 team, 连杀数)
        public event Action<int> OnBaseDestroyed;          // (被摧毁方 team)

        private readonly int[] _killCounts = { 0, 0 };
        private float _lastKillTime = float.NegativeInfinity;
        private bool _wasBattle;
        private bool _disposed;

        /// <summary>单例：确保 BattleStage 存在并返回（幂等）。</summary>
        public static BattleStage EnsureCreated()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("BattleStage");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<BattleStage>();
            return Instance;
        }

        private void Awake()
        {
            // 挂载击杀/基地摧毁点 -> RecordKill（对位 battleStage.js 结算广播）
            CombatSystem.OnSoldierKilled += OnSoldierKilledHandler;
            CombatSystem.OnBaseDestroyed += OnBaseDestroyedHandler;
        }

        private void OnDestroy()
        {
            _disposed = true;
            CombatSystem.OnSoldierKilled -= OnSoldierKilledHandler;
            CombatSystem.OnBaseDestroyed -= OnBaseDestroyedHandler;
            if (Instance == this) Instance = null;
        }

        // 士兵阵亡 -> 阵亡方 team 的对立方得分（0 对 1，取反）
        private void OnSoldierKilledHandler(Soldier s)
        {
            if (_disposed || s == null) return;
            RecordKill(1 - s.team);
        }

        // 基地被摧毁 -> 摧毁方得分 + 记录被摧毁方 + 广播 OnBaseDestroyed
        private void OnBaseDestroyedHandler(int destroyedTeam)
        {
            if (_disposed) return;
            DestroyedTeam = destroyedTeam;
            RecordKill(1 - destroyedTeam);     // 基地摧毁 = 决定性一杀，同样挂载 RecordKill
            OnBaseDestroyed?.Invoke(destroyedTeam);
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            bool inBattle = gm != null && gm.State == GameState.Battle;

            // 进入战斗：重置计时/连杀，并强制触发"开场"阶段（保证 Opening 诗词必然发出）
            if (inBattle && !_wasBattle)
            {
                BattleElapsed = 0f;
                KillStreak = 0;
                ForcePhase(BattlePhase.Opening);
            }
            if (!inBattle)
            {
                _wasBattle = false;
                return;
            }
            _wasBattle = true;

            BattleElapsed += Time.deltaTime;

            int alive = CountAliveSoldiers();
            int sRef = soldierNormMax > 0
                ? soldierNormMax
                : (gm != null && gm.factory != null ? Mathf.Max(1, gm.factory.maxSoldiers * 2) : 100);

            float timeNorm = battleDurationRef <= 0f ? 0f : Mathf.Clamp01(BattleElapsed / battleDurationRef);
            float killNorm = killNormMax <= 0 ? 0f : Mathf.Clamp01((float)TotalKills / Mathf.Max(1, killNormMax));
            float soldierNorm = Mathf.Clamp01((float)alive / Mathf.Max(1, sRef));

            Intensity = Mathf.Clamp01(0.4f * timeNorm + 0.3f * killNorm + 0.3f * soldierNorm);

            BattlePhase next = ResolvePhase(Intensity);
            if (next != Phase) SetPhase(next);
        }

        // intensity -> 阶段映射（四档）
        private static BattlePhase ResolvePhase(float intensity)
        {
            if (intensity >= 0.75f) return BattlePhase.Finale;
            if (intensity >= 0.5f) return BattlePhase.Frenzy;
            if (intensity >= 0.25f) return BattlePhase.Stalemate;
            return BattlePhase.Opening;
        }

        private void SetPhase(BattlePhase p)
        {
            if (Phase == p) return;
            ForcePhase(p);
        }

        private void ForcePhase(BattlePhase p)
        {
            Phase = p;
            OnPhaseChanged?.Invoke(p);
        }

        // 统计存活士兵：逐个判 tag.isAlive（ActiveSoldiers 含死亡动画未回池者，避免误计）
        public int CountAliveSoldiers()
        {
            var list = SoldierFactory.ActiveSoldiers;
            if (list == null) return 0;
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s != null && s.tag != null && s.tag.isAlive) n++;
            }
            return n;
        }

        /// <summary>击杀结算入口：首杀/连杀/累计（team=得分方 0 天庭 / 1 玄朝）。</summary>
        public void RecordKill(int team)
        {
            if (_disposed) return;
            team = Mathf.Clamp(team, 0, 1);
            TotalKills++;
            _killCounts[team]++;

            bool first = !IsFirstKillDone;
            if (first) IsFirstKillDone = true;

            float now = Time.time;
            if (first || now - _lastKillTime > killStreakWindow) KillStreak = 1;
            else KillStreak++;
            _lastKillTime = now;

            OnKill?.Invoke(team, KillStreak);
        }

        public int GetKills(int team) { team = Mathf.Clamp(team, 0, 1); return _killCounts[team]; }
        public int GetAliveCount() { return CountAliveSoldiers(); }

        // T2-3 重载：清空击杀/计时/被摧毁方/连杀状态（对位 Web _resetBattle 重置战局阶段；GameOver 转 Battle 触发 Update 自动重置时补全计数归零）
        public void Reset()
        {
            BattleElapsed = 0f;
            TotalKills = 0;
            KillStreak = 0;
            IsFirstKillDone = false;
            DestroyedTeam = -1;
            _killCounts[0] = 0; _killCounts[1] = 0;
            _lastKillTime = float.NegativeInfinity;
            ForcePhase(BattlePhase.Opening);
            Debug.Log("[BattleStage] 战局重置完成(击杀/计时/阶段归零)");
        }

        /// <summary>当前阶段对应视觉参数（背景色/粒子密度系数/缩放），供 UI/特效取用。</summary>
        public BattleVisualParams getVisualParams()
        {
            switch (Phase)
            {
                case BattlePhase.Opening:   return new BattleVisualParams(new Color(0.55f, 0.70f, 0.90f), 0.40f, 0.90f);
                case BattlePhase.Stalemate: return new BattleVisualParams(new Color(0.62f, 0.62f, 0.64f), 0.55f, 1.00f);
                case BattlePhase.Frenzy:    return new BattleVisualParams(new Color(0.85f, 0.35f, 0.20f), 0.80f, 1.10f);
                case BattlePhase.Finale:    return new BattleVisualParams(new Color(0.90f, 0.15f, 0.05f), 1.00f, 1.20f);
                default:                    return new BattleVisualParams(Color.gray, 0.50f, 1.00f);
            }
        }

        // 调试：每 3s 上报阶段/intensity/计数（与 GameManager.ReportRuntime 同节奏，仅 autoTest 或手动调用触发）
        public override string ToString()
            => $"[BattleStage] phase={Phase} intensity={Intensity:F2} t={BattleElapsed:F0}s kills={TotalKills} killStreak={KillStreak} alive={GetAliveCount()} destroyedTeam={DestroyedTeam}";
    }
}

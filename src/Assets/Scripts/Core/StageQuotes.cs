// StageQuotes.cs —— 战局诗词弹幕（对位 Web src3d/core/stageQuotes.js + main.js._fireQuote）
// ★任务8 全量重写（2026-09-06）：源码级复刻原版 7 分类 STAGE_QUOTES + QuoteCooldown 分类冷却 + 三句式协程字体递进 + reset。
// 原版 STAGE_QUOTES 7 分类（opening/wipe/frenzy/comeback/baseLow/firstBaseHit/victory），QUOTE_COOLDOWN_MS=10000，
// QuoteCooldown 按 category 独立冷却（canFire/fire/reset）。multi=三句式(按序显现+字体递进变大)、single=单句式。
// 显示：Debug.Log + 尝试写中央信息面板 Text（GameObject.Find 候选名防御性探测；无则仅 Log）。
// 触发接线（对位 main.js 7 时机）：opening=进战斗延迟5s；frenzy=阶段切白热化；wipe=一方清场；
//   comeback=基地受击5s内击杀；baseLow=基地hp<30%每方每次；firstBaseHit=首击敌方基地每局一次；victory=基地摧毁。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;   // ★任务8: UnityEvent<float>(BaseSystem.onDamaged) 的命名空间，BindBases 里 new UnityEvent 需要它
using UnityEngine.UI;

namespace Liuhuo.Core
{
    /// <summary>
    /// 战局诗词单例（MonoBehaviour，跑三句式/延迟协程）。EnsureCreated() 幂等创建并订阅 CombatSystem + BattleStage 事件；
    /// 基地受击用 BindBases() 订阅 BaseSystem.onDamaged（GameManager CreateBasesPhaseB 后注入，重开须重绑）。
    /// </summary>
    public class StageQuotes : MonoBehaviour
    {
        // 对位 src3d/core/stageQuotes.js QUOTE_COOLDOWN_MS = 10000（同分类最短间隔）
        public const int QUOTE_COOLDOWN_MS = 10000;
        private const float QUOTE_COOLDOWN = QUOTE_COOLDOWN_MS / 1000f;

        /// <summary>7 触发分类（对位 STAGE_QUOTES 字典 key）。</summary>
        public enum Category { Opening, Wipe, Frenzy, Comeback, BaseLow, FirstBaseHit, Victory }

        // ---- 单条 Quote 定义（对位 STAGE_QUOTES：type('multi'/'single')/lines[]/text/duration）----
        private sealed class QuoteDef
        {
            public bool multi;
            public string[] lines;       // multi 三句式
            public string text;          // single 单句式
            public float duration;       // 展示总时长(秒)
            public QuoteDef(bool m, string[] l, string t, float d) { multi = m; lines = l; text = t; duration = d; }
        }

        // ---- STAGE_QUOTES 字典（文案逐字原版）----
        private static readonly Dictionary<Category, QuoteDef> STAGE_QUOTES = new Dictionary<Category, QuoteDef>
        {
            { Category.Opening,     new QuoteDef(true,  new[]{"沸诸天","碎尘寰","焚尽八荒谁争王"},                          null,  4.0f) },
            { Category.Wipe,        new QuoteDef(false, null,   "一身转战三千里，一剑曾当百万师",                        3.0f) },
            { Category.Frenzy,      new QuoteDef(true,  new[]{"苍洱毁","群星坠","三界崩末尤未悔"},                          null,  4.0f) },
            { Category.Comeback,    new QuoteDef(false, null,   "熟人自夸常胜，何方妄称无双",                            3.0f) },
            { Category.BaseLow,     new QuoteDef(false, null,   "兵临城下危在旦夕",                                      3.0f) },
            { Category.FirstBaseHit,new QuoteDef(false, null,   "斩将夺旗先登首功",                                      3.0f) },
            { Category.Victory,     new QuoteDef(false, null,   "君临天下，新王登基",                                    5.0f) },
        };

        public static StageQuotes Instance { get; private set; }

        // ---- QuoteCooldown 分类冷却（对位 stageQuotes.js QuoteCooldown 类：_lastFire 按 category 独立+reset）----
        private readonly Dictionary<Category, float> _lastFire = new Dictionary<Category, float>();

        // ---- 只触发一次 / 状态标记（对位 main.js _wipeFired/_baseWarned/_firstBaseHitDone/_lastBaseHitAt）----
        private readonly Dictionary<int, bool> _baseWarned = new Dictionary<int, bool>();   // team+hp<30% 每方每次
        private readonly Dictionary<int, bool> _wipeFired = new Dictionary<int, bool>();    // 被灭方清场每局一次
        private readonly Dictionary<int, float> _lastBaseHitAt = new Dictionary<int, float>(); // team -> 最近受击 Time.time
        private bool _firstBaseHitDone;   // 首击敌方基地每局一次

        private BaseSystem _blue;         // 天庭(蓝)基地
        private BaseSystem _red;          // 玄朝(红)基地
        private Text _infoText;           // 缓存中央信息面板 Text（命中后不再查找）
        private Coroutine _multiCoroutine;
        private Coroutine _openingTimer;

        /// <summary>单例：确保 StageQuotes 存在并订阅事件（幂等）。</summary>
        public static StageQuotes EnsureCreated()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("StageQuotes");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<StageQuotes>();
            return Instance;
        }

        private void Awake()
        {
            // 对位 main.js _fireQuote 订阅点：击杀(wipe/comeback) + 基地摧毁(victory)
            CombatSystem.OnSoldierKilled += OnSoldierKilled;
            CombatSystem.OnBaseDestroyed += OnBaseDestroyed;
            // 阶段(对位 main.js 阶段) ：进战斗 Opening 延迟5s / Frenzy 白热化
            BattleStage.EnsureCreated().OnPhaseChanged += OnPhaseChanged;
        }

        private void OnDestroy()
        {
            CombatSystem.OnSoldierKilled -= OnSoldierKilled;
            CombatSystem.OnBaseDestroyed -= OnBaseDestroyed;
            if (BattleStage.Instance != null) BattleStage.Instance.OnPhaseChanged -= OnPhaseChanged;
            if (Instance == this) Instance = null;
        }

        // ---- 基地受击订阅（GameManager CreateBasesPhaseB 后调用；重开基地重建须重绑）----
        public void BindBases(BaseSystem blue, BaseSystem red)
        {
            // ★任务8修复：BaseSystem.onDamaged(UnityEvent<float>) 无默认=null——运行时动态基地(BaseModelFactory)未序列化事件，
            //   直接 AddListener/RemoveListener 会 NRE(实测 GameStart BindBases 崩)。须先 new UnityEvent 确保非空(ApplyHit 里 onDamaged?.Invoke 会推送)。
            if (blue != null && blue != _blue)
            {
                if (_blue != null && _blue.onDamaged != null) _blue.onDamaged.RemoveListener(OnBlueDamaged);
                _blue = blue;
                if (_blue.onDamaged == null) _blue.onDamaged = new UnityEvent<float>();
                _blue.onDamaged.AddListener(OnBlueDamaged);
            }
            if (red != null && red != _red)
            {
                if (_red != null && _red.onDamaged != null) _red.onDamaged.RemoveListener(OnRedDamaged);
                _red = red;
                if (_red.onDamaged == null) _red.onDamaged = new UnityEvent<float>();
                _red.onDamaged.AddListener(OnRedDamaged);
            }
        }

        private void OnBlueDamaged(float hp) { HandleBaseDamaged(_blue, hp); }
        private void OnRedDamaged(float hp) { HandleBaseDamaged(_red, hp); }

        // ---- 阶段触发（对位 main.js 阶段切白热化 / 进战斗开局）----
        private void OnPhaseChanged(BattlePhase p)
        {
            switch (p)
            {
                case BattlePhase.Opening:
                    // 重开/进战斗回 Opening：清只触发一次标记 + 重启 5s 开局延迟（对位 _resetBattle 手动 _openingTimer 重置）
                    ResetOneShotFlags();
                    RestartOpeningTimer();
                    break;
                case BattlePhase.Frenzy:
                    FireQuote(Category.Frenzy);
                    break;
            }
        }

        // ---- 击杀触发（对位 main.js _fireQuote 的 wipe/comeback 分支）----
        private void OnSoldierKilled(Soldier s)
        {
            if (s == null) return;
            int killedTeam = s.team;            // 被杀方（对位 killerTeam 的被灭方）
            int killerTeam = 1 - s.team;        // 击杀方（被灭方对立）

            // wipe：被杀方清场 → 每局该方一次（对位 enemyAlive===0 && !_wipeFired[killerTeam]）
            if (CountAlive(killedTeam) == 0 && !_wipeFired.ContainsKey(killedTeam))
            {
                _wipeFired[killedTeam] = true;
                Debug.Log($"[StageQuotes] wipe条件满足: team={killedTeam} 全军覆没");
                FireQuote(Category.Wipe);
            }

            // comeback：击杀方基地 5s 内受过击（对位 _lastBaseHitAt[killerTeam] 5000ms 窗口）
            if (_lastBaseHitAt.TryGetValue(killerTeam, out var hitAt) && (Time.time - hitAt) <= 5f)
            {
                Debug.Log($"[StageQuotes] comeback条件满足: killer={killerTeam} 基地{Time.time - hitAt:F1}s内受击");
                FireQuote(Category.Comeback);
            }
        }

        // ---- 基地摧毁触发（对位 main.js 基地摧毁 → victory）----
        private void OnBaseDestroyed(int destroyedTeam)
        {
            FireQuote(Category.Victory);
        }

        // ---- 基地受击（对位 main.js onHit 的 baseLow/comeback窗口/firstBaseHit）----
        private void HandleBaseDamaged(BaseSystem b, float hp)
        {
            if (b == null) return;
            int team = b.Team;
            _lastBaseHitAt[team] = Time.time;   // comeback 窗口

            float ratio = b.GetHpNormalized();
            if (ratio <= 0.3f && !_baseWarned.ContainsKey(team))
            {
                _baseWarned[team] = true;
                Debug.Log($"[StageQuotes] baseLow条件满足: team={team} hpRatio={ratio:F2} (每方每次)");
                FireQuote(Category.BaseLow);
            }

            if (!_firstBaseHitDone)
            {
                _firstBaseHitDone = true;
                Debug.Log("[StageQuotes] firstBaseHit条件满足: 首击敌方基地 (每局一次)");
                FireQuote(Category.FirstBaseHit);
            }
        }

        // ---- 开局延迟 5s 触发 opening（对位 main.js setTimeout 5000）----
        private void RestartOpeningTimer()
        {
            if (_openingTimer != null) StopCoroutine(_openingTimer);
            _openingTimer = StartCoroutine(DelayOpening(5f));
        }

        private IEnumerator DelayOpening(float delay)
        {
            yield return new WaitForSeconds(delay);
            Debug.Log("[StageQuotes] opening 开局延迟完成, 触发开场三句式");
            FireQuote(Category.Opening);
        }

        // ---- 只触发一次标记清空（对位 _resetBattle：_quoteCd.reset('opening') + _firstBaseHitDone=false + _baseWarned={}）----
        private void ResetOneShotFlags()
        {
            ResetCooldown(Category.Opening);
            _baseWarned.Clear();
            _wipeFired.Clear();
            _lastBaseHitAt.Clear();
            _firstBaseHitDone = false;
        }

        // ===== QuoteCooldown 分类冷却（对位 stageQuotes.js canFire/fire/reset）=====
        public bool CanFire(Category c)
        {
            if (!_lastFire.TryGetValue(c, out var t)) return true;
            return Time.time - t >= QUOTE_COOLDOWN;
        }
        public void Fire(Category c) { _lastFire[c] = Time.time; }
        public void ResetCooldown(Category c) { _lastFire.Remove(c); }

        private static QuoteDef GetQuote(Category c) => STAGE_QUOTES.TryGetValue(c, out var q) ? q : null;

        // ---- 触发出口（对位 main.js._fireQuote：canFire→getQuote→multi?showMultiQuote : showStageTip→fire）----
        // ★任务8 autoTest 探针入口：公开转发并走正常分类冷却门控（生产态不调用，仅无头取证用）
        public void PublicFire(Category c) { FireQuote(c); }
        private void FireQuote(Category c)
        {
            if (!CanFire(c)) { Debug.Log($"[StageQuotes][冷却] {c} 冷却中(<{QUOTE_COOLDOWN:F0}s), 本句略过"); return; }
            var q = GetQuote(c);
            if (q == null) return;
            if (q.multi)
            {
                if (_multiCoroutine != null) StopCoroutine(_multiCoroutine);
                _multiCoroutine = StartCoroutine(ShowMultiQuote(q.lines, q.duration));
            }
            else
            {
                ShowStageTip(q.text, q.duration);
            }
            Fire(c);
        }

        // ---- 三句式：逐句按序显现 + 字体递进变大（对位 showMultiQuote(lines, duration)）----
        private IEnumerator ShowMultiQuote(string[] lines, float duration)
        {
            if (lines == null || lines.Length == 0) yield break;
            float per = duration / lines.Length;
            for (int i = 0; i < lines.Length; i++)
            {
                string txt = lines[i];
                Debug.Log($"[StageQuotes] 三句式 第{i + 1}/{lines.Length}句: [{txt}]");
                var t = ResolveInfoText();
                if (t != null) { t.text = txt; t.fontSize = 24 + i * 8; }   // 字体递进变大(24→32→40)
                yield return new WaitForSeconds(per);
            }
        }

        // ---- 单句式提示（对位 showStageTip(text, duration)）----
        private void ShowStageTip(string text, float duration)
        {
            if (string.IsNullOrEmpty(text)) return;
            Debug.Log($"[StageQuotes] {text}");
            var t = ResolveInfoText();
            if (t != null) t.text = text;
        }

        // ---- 按阵营统计存活士兵（对位 GameManager.CountAlive；判 tag.isAlive 防死亡动画未回池误计）----
        private int CountAlive(int team)
        {
            var list = SoldierFactory.ActiveSoldiers;
            if (list == null) return 0;
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s != null && s.tag != null && s.tag.isAlive && s.tag.team == team) n++;
            }
            return n;
        }

        // 防御性探测"中央信息面板"文本组件（当前场景无专用面板，用候选名探测；命中即缓存）
        private Text ResolveInfoText()
        {
            if (_infoText != null) return _infoText;
            string[] candidates = { "HUDTitle", "InfoPanel", "CentralText", "QuoteText", "BattleInfo", "CenterText" };
            foreach (var name in candidates)
            {
                var go = GameObject.Find(name);
                if (go == null) continue;
                var t = go.GetComponent<Text>();
                if (t != null) { _infoText = t; return t; }
            }
            return null;
        }
    }
}

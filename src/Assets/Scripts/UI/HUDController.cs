// HUDController.cs —— 映射 Web src3d/ui/UIManager.js 逻辑层（F1 修正，非纯视觉）
// 对位 API：createHUD/updateHUD/setFaction/setCooldown/onAction
//   按钮 key：summon(天庭召唤) / summon-red(玄朝召唤) / ult(圣裁,本阵营) / ultEnemy(敌方大招,独立冷却)
//   LX_FACTION 文案表：tianting{天庭/天庭召唤/圣裁} / xuanchao{玄朝/玄朝召唤/同死}
//   双主题 CSS(tianting-ui.css 金白 / xuanchao-ui.css 红黑) → UGUI 配色 ThemeData
// C 轴接线（S5）：btnUlt→UltController.TriggerUltimate(0 圣裁)、btnUltEnemy→TriggerUltimate(1 同死)、冷却交互显示。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Liuhuo.FX;

namespace Liuhuo.UI
{
    // 阵营风格主题（对位 LX_FACTION 文案 + 双主题 CSS）
    [System.Serializable]
    public class FactionTheme
    {
        public string name = "天庭";
        public string summonLabel = "天庭召唤";
        public string ultLabel = "圣裁";
        public Color primary = new Color(1f, 0.84f, 0f);    // 金
        public Color accent = new Color(1f, 1f, 1f);        // 白
    }

    public class HUDController : MonoBehaviour
    {
        [Header("双阵营主题（F1：tianting-ui.css / xuanchao-ui.css）")]
        public FactionTheme tiantingTheme = new FactionTheme();
        public FactionTheme xuanchaoTheme = new FactionTheme { name = "玄朝", summonLabel = "玄朝召唤", ultLabel = "同死", primary = new Color(1f, 0.27f, 0.27f), accent = new Color(0.2f, 0.05f, 0.05f) };

        [Header("状态 UI 绑定")]
        public Slider baseHpTianting;
        public Slider baseHpXuanchao;
        public Text soldierCountTianting;
        public Text soldierCountXuanchao;
        public Text killCountTianting;
        public Text killCountXuanchao;
        [Tooltip("P1-6 缺口2：基地血量数值 Text（显示当前 HP 绝对值）")]
        public Text hpTextTianting;   // 天庭基地血量数字
        public Text hpTextXuanchao;   // 玄朝基地血量数字
        public Button btnSummon;      // key=summon 天庭召唤
        public Button btnSummonRed;   // key=summon-red 玄朝召唤
        public Button btnUlt;         // key=ult 圣裁
        public Button btnUltEnemy;    // key=ultEnemy 敌方大招独立冷却

        [Header("大招接线（C 轴 S5）")]
        public UltController ult;     // 场景/自举注入，决定 btnUlt/btnUltEnemy 触发目标

        // 回调表（对位 _actions）+ 冷却状态（对位 _cooldown）
        private Dictionary<string, Action> _actions = new Dictionary<string, Action>();
        private Dictionary<string, float> _cooldown = new Dictionary<string, float>();
        private string _faction = "tianting";

        // ---- 对位 UIManager.prototype 方法 ----
        public void OnAction(string key, Action fn) { _actions[key] = fn; }

        public void SetCooldown(string key, float remain)
        {
            _cooldown[key] = remain;
            // C 轴：把冷却映射到对应按钮的可交互状态
            Button b = key == "ult" ? btnUlt : (key == "ultEnemy" ? btnUltEnemy : null);
            if (b != null) b.interactable = remain <= 0.01f;
        }

        public void SetFaction(string faction)
        {
            _faction = faction;
            // TODO: 切主题色 + 按钮文案（summon/ult 文案随阵营切换）
        }

        // 对位 updateHUD(state)：刷新血条/数量/击杀
        // P1-6 缺口2：新增 tiantingHp/xuanchaoHp（基地血量绝对值），驱动 HP 数字 Text；pct 仍驱动 Slider。
        public void UpdateHud(float tiantingHpPct, float xuanchaoHpPct, float tiantingHp, float xuanchaoHp, int tSoldier, int xSoldier, int tKill, int xKill)
        {
            if (baseHpTianting) baseHpTianting.value = tiantingHpPct;
            if (baseHpXuanchao) baseHpXuanchao.value = xuanchaoHpPct;
            // F1 士兵数（工部二 S4 ◎2）：格式"XX/50"（对位 Web 显示当前/上限；上限=SoldierFactory.maxSoldiers=50 约定）
            if (soldierCountTianting) soldierCountTianting.text = $"{tSoldier}/{50}";
            if (soldierCountXuanchao) soldierCountXuanchao.text = $"{xSoldier}/{50}";
            if (killCountTianting) killCountTianting.text = tKill.ToString();
            if (killCountXuanchao) killCountXuanchao.text = xKill.ToString();
            // P1-6 缺口2：HP 数值 Text（F0=取整显示当前血量绝对值，随基地受击实时下降）
            if (hpTextTianting) hpTextTianting.text = tiantingHp.ToString("F0");
            if (hpTextXuanchao) hpTextXuanchao.text = xuanchaoHp.ToString("F0");
        }

        // 绑定按钮点击（对位 bind(key, fn)）
        private void Bind(string key, Button b)
        {
            if (b == null) return;
            b.onClick.AddListener(() => { if (_actions.TryGetValue(key, out var fn)) fn(); });
        }

        void Start()
        {
            Bind("summon", btnSummon);
            Bind("summon-red", btnSummonRed);
            Bind("ult", btnUlt);
            Bind("ultEnemy", btnUltEnemy);

            // C 轴接线：大招按钮直连 UltController（免 Inspector，自举保证实例）
            if (ult == null) ult = UltController.EnsureCreated();
            if (ult != null)
            {
                if (btnUlt != null) btnUlt.onClick.AddListener(() => ult.TriggerUltimate(0));       // 圣裁(天庭/蓝)
                if (btnUltEnemy != null) btnUltEnemy.onClick.AddListener(() => ult.TriggerUltimate(1)); // 同死(玄朝/红)
                ApplyUltCooldown(0, btnUlt);
                ApplyUltCooldown(1, btnUltEnemy);
            }
        }

        // 工部二 §14.6-3（§14.2-③）: 大招冷却 fillAmount 探针，限频打印防刷屏（B轴判据 [HUD] ultFill）
        private readonly float[] _lastFil = new float[] { -1f, -1f };

        void Update()
        {
            if (ult == null) return;
            ApplyUltCooldown(0, btnUlt);
            ApplyUltCooldown(1, btnUltEnemy);
        }

        private void ApplyUltCooldown(int team, Button b)
        {
            if (b == null) return;
            b.interactable = ult.IsReady(team);
            // 工部二 §14.6-3: 复用现成 UltController.GetCooldownPct(team) @L79，勿新建协程（Update 天然每帧驱动）
            // fillAmount = 1 - pct：冷却中 1-pct<1 未满；冷却完 pct=0 → fillAmount=1 满
            float pct = ult.GetCooldownPct(team);
            var img = b.GetComponent<Image>();
            if (img != null)
            {
                img.fillAmount = 1f - pct;
                if (Mathf.Abs(img.fillAmount - _lastFil[team]) > 0.05f)
                {
                    _lastFil[team] = img.fillAmount;
                    Debug.Log($"[HUD] ultFill team={team} pct={pct:F2} fillAmount={img.fillAmount:F2}");
                }
            }
        }
    }
}

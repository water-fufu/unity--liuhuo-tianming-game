// SettlementManager.cs —— 结算画面 + 重载（对位 Web main.js _triggerGameOver / _resetBattle，任务2 T2）
// 职责：
//   T2-1 结算画面功能：一方基地 HP=0 -> 弹结算面板（胜负大字 + 重新开始按钮），胜负判定由 GameManager.winTeam 提供。
//   T2-2 基地爆炸：爆炸特效由 DeathFXManager 独立订阅 BaseSystem.onDestroyed 播放（勿重复）；本类补"爆炸后隐藏败方基地"（GameBootstrap.OnBaseDestroyed 协程延迟）。
//   T2-3 重载：点重新开始 -> GameManager.RestartBattle()（基地重建 + 士兵/弹清空 + 战局重置 + 面板关闭）。
//   T2-4 暂停/恢复：Show() 置 Time.timeScale=0（士兵/子弹静止），OnRestart 置 1（恢复）。
// UGUI 用 unscaled 时间，timeScale=0 下重开按钮仍可点击（对位原版 DOM z-index 9999 置顶）。
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Liuhuo.Core;

namespace Liuhuo.UI
{
    public class SettlementManager : MonoBehaviour
    {
        public static SettlementManager Instance;

        private GameObject _panel;      // 结算面板根（Canvas/遮罩/文字/按钮）
        private bool _active = false;

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        /// <summary>自举单例（免场景预挂，对位 UILoader/UltController 模式）。</summary>
        public static void EnsureCreated()
        {
            if (Instance != null) return;
            var go = new GameObject("SettlementManager");
            go.AddComponent<SettlementManager>();
        }

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        /// <summary>结算触发（GameBootstrap.OnBaseDestroyed 调用）。winnerTeam：0=天庭胜/1=玄朝胜。</summary>
        public void Show(int winnerTeam)
        {
            if (_active) return;
            _active = true;
            Time.timeScale = 0f;   // T2-4：结算暂停游戏（士兵/子弹静止）
            EnsureEventSystem();
            BuildPanel(winnerTeam);
            Debug.Log($"[Settlement] 结算面板弹出 winnerTeam={winnerTeam} timeScale=0");
        }

        private void BuildPanel(int winnerTeam)
        {
            var canvasGo = new GameObject("SettlementCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _panel = canvasGo;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;   // 置顶于 HUD（对位原版 z-index 9999）
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // 全屏半透明黑遮罩（统一底色 + 阻挡 HUD 点击）
            var mask = new GameObject("Mask", typeof(RectTransform), typeof(Image));
            mask.transform.SetParent(canvasGo.transform, false);
            var maskRt = mask.GetComponent<RectTransform>();
            maskRt.anchorMin = Vector2.zero; maskRt.anchorMax = Vector2.one;
            maskRt.offsetMin = Vector2.zero; maskRt.offsetMax = Vector2.zero;
            mask.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

            // 胜负大字（对位 victory-text "大胜"，金色 #ffd700）
            string winName = winnerTeam == 0 ? "天庭" : "玄朝";
            Text winText = MakeText(canvasGo.transform, "VictoryText", new Vector2(0f, 140f), 96,
                new Color(1f, 0.84f, 0f), $"{winName}   大胜");
            var shadow = winText.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            shadow.effectDistance = new Vector2(3f, -3f);

            // 败方提示
            string loseName = winnerTeam == 0 ? "玄朝" : "天庭";
            MakeText(canvasGo.transform, "LoseText", new Vector2(0f, 50f), 44,
                new Color(1f, 0.85f, 0.85f), $"{loseName}基地已被摧毁");

            // 重新开始按钮（对位 restart-btn "点击重新开始"）
            MakeButton(canvasGo.transform, "RestartBtn", new Vector2(0f, -120f), OnRestart);

            Debug.Log($"[Settlement] 结算面板构建完成 {winName}大胜+重开按钮");
        }

        private Text MakeText(Transform parent, string name, Vector2 pos, int size, Color color, string content)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(600f, 110f);
            var t = go.GetComponent<Text>();
            t.font = GetChineseFont(size);
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.text = content;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private Text MakeButton(Transform parent, string name, Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(420f, 110f);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.35f, 0.25f, 0.05f, 0.9f);
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            // 按钮文字：锚点撑满按钮（anchor 0..1 + offset 0 => rect=按钮尺寸，文字自动居中）
            Text label = MakeText(go.transform, "Label", Vector2.zero, 46, Color.white, "点击重新开始");
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            return label;
        }

        // 复用 UILoader 的 SimHei 中文字体（保证"大胜/重新开始"中文渲染）；UILoader 未建时兜底内置字体
        private Font GetChineseFont(int size)
        {
            if (UILoader.Instance != null)
            {
                try { return UILoader.Instance.GetChineseFont(size); }
                catch (System.Exception e) { Debug.LogWarning($"[Settlement] GetChineseFont 异常: {e.Message}"); }
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        /// <summary>重开按钮回调：关面板 + timeScale=1 + 重载。</summary>
        private void OnRestart()
        {
            HidePanel();
            Time.timeScale = 1f;   // T2-4：重开恢复
            Debug.Log("[Settlement] 重新开始点击 timeScale=1 -> RestartBattle");
            if (GameManager.Instance != null) GameManager.Instance.RestartBattle();
        }

        private void HidePanel()
        {
            if (_panel != null) Destroy(_panel);
            _panel = null;
            _active = false;
        }
    }
}

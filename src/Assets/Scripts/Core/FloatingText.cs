// FloatingText.cs —— 战斗飘字（死亡/受击数字，对位 Web kill/damage 飘字，任务块P1-3 + 工部二◎1）
// 落点（工部二 S4 ◎1 修正口径）：
//   伤害数字 -> Soldier.TakeDamage 扣血处调 Show(dmg, pos, teamColor)
//   击杀 "击杀!" -> GameManager 订阅 CombatSystem.OnSoldierKilled 事件回调调 Show
// 池化复用：防频繁 Instantiate/Destroy 的 GC 抖动 + DrawCall 波动（D2 禁回归）。
// 字体：SimHei 优先、兜底内置（伤害数字为纯数字 Latin 栅格化可靠；中文"击杀!"视运行时能否栅格化，
//       打包后以 [FloatText] hasChar 探针确认——若 SimHei 未能载入中文字形则中文部分降级为不入库判断）。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Liuhuo.Core
{
    /// <summary>飘字门面池：静态入口 + 唯一 manager（单例）承载生命周期。</summary>
    public static class FloatingText
    {
        private static FloatingTextManager _m;
        static FloatingTextManager M
        {
            get
            {
                if (_m == null) _m = FloatingTextManager.EnsureCreated();
                return _m;
            }
        }

        /// <summary>在世界坐标 worldPos 显示一行飘字。</summary>
        public static void Show(string text, Vector3 worldPos, Color color, int fontSize = 26)
        {
            var m = M;
            if (m != null) m.Show(text, worldPos, color, fontSize);
        }
    }

    /// <summary>飘字运行时 driver：池 + 上移淡出 + 回收。EnsureCreated 自举（免场景预挂）。</summary>
    public class FloatingTextManager : MonoBehaviour
    {
        private const int PoolSize = 40;
        private const float Life = 1.0f;
        private const float Rise = 2.2f;
        private static FloatingTextManager _inst;
        private readonly Queue<Text> _pool = new Queue<Text>();
        private readonly List<Text> _active = new List<Text>();
        private readonly List<float> _actLife = new List<float>();
        private readonly List<Color> _actColor = new List<Color>();
        private Transform _root;
        private Font _font;

        public static FloatingTextManager EnsureCreated()
        {
            if (_inst != null) return _inst;
            // 挂在一个独立 GameObject 上（DontDestroy 不设——战斗场景内即可，避免跨场景残留）
            var go = new GameObject("FloatingTextManager");
            _inst = go.AddComponent<FloatingTextManager>();
            return _inst;
        }

        private void Awake()
        {
            // WorldSpace Canvas：scale 调到世界可见（相机 (0,150,220) FOV45，战场 ±70 ~ 数百像素，飘字需足够大）
            var canvasGo = new GameObject("FloatingTextCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;
            _root = canvasGo.transform;
            // 主相机（BattleCamera/Camera.main）面向飘字，Canvas 世界缩放收敛约 1.5m 宽
            canvasGo.transform.localScale = Vector3.one;
            // 字体
            _font = Resources.Load<Font>("fonts/SimHei");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // 中文栅格化探针：SimHei 动态字体在打包运行时能否载入中文字形（击杀!"击/杀"）
            try
            {
                _font.RequestCharactersInTexture("击杀", 26, FontStyle.Normal);
                bool cjk = _font.HasCharacter('击') && _font.HasCharacter('杀');
                Debug.Log($"[FloatText] font={_font.name} dynamic={_font.dynamic} hasChar击={_font.HasCharacter('击')} 中文栅格化={(cjk ? "OK(中文可显)" : "FAIL(中文空白,请烘焙)")}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[FloatText] 字体探针异常: {e.Message}");
            }
        }

        public void Show(string text, Vector3 worldPos, Color color, int fontSize)
        {
            Text t = _pool.Count > 0 ? _pool.Dequeue() : CreateText();
            t.gameObject.SetActive(true);
            t.text = text;
            t.fontSize = fontSize;
            t.color = color;
            t.rectTransform.SetPositionAndRotation(worldPos, Quaternion.identity);
            _active.Add(t);
            _actLife.Add(0f);
            _actColor.Add(color);
        }

        private Text CreateText()
        {
            var go = new GameObject("FloatText", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_root, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(3.5f, 1.4f);
            var t = go.GetComponent<Text>();
            if (_font != null) t.font = _font;
            t.fontSize = 26;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private void Update()
        {
            if (_active.Count == 0) return;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                _actLife[i] += Time.deltaTime;
                float p = Mathf.Clamp01(_actLife[i] / Life);
                var t = _active[i];
                if (t == null) { _active.RemoveAt(i); _actLife.RemoveAt(i); _actColor.RemoveAt(i); continue; }
                // 上移 + 淡出
                Vector3 pos = t.rectTransform.position;
                t.rectTransform.position = pos + Vector3.up * Rise * Time.deltaTime;
                var c = _actColor[i];
                c.a = 1f - p;
                t.color = c;
                if (p >= 1f)
                {
                    t.gameObject.SetActive(false);
                    _active.RemoveAt(i); _actLife.RemoveAt(i); _actColor.RemoveAt(i);
                    _pool.Enqueue(t);
                }
            }
        }
    }
}

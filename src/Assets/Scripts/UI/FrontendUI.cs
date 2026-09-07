// FrontendUI.cs —— T10 开始页 + 关卡选择页 UGUI 复刻（对位 Web start.html / level-select.html）
//   运行时 Create()（镜像 UILoader/SettingsPanel 自举模式），挂在 UILoader 建的 UI Canvas 下 MenuRoot
//   （嵌套子 Canvas overrideSorting+sortingOrder=100 置顶，保证盖住战斗 HUD）。
//   SetActive 切换 StartPanel/LevelSelectPanel；选关确认 -> GameManager.Instance.SetState(GameState.Battle)。
//   ★关键：菜单必须运行时创建，绝不写 .unity（会被 S5_Builder.BuildScene 重建覆盖 + SceneAudit 只查 Renderer/Camera，不查 UI）。
//   ★背景用纯色 Image 遮光（避开 UILoader.LoadSprite 的 isReadable:0 退化纯色方块坑），文字用 SimHei 动态字体。
using System;
using UnityEngine;
using UnityEngine.UI;
using Liuhuo.Core;   // GameManager / GameState

namespace Liuhuo.UI
{
    public static class FrontendUI
    {
        static GameObject _root, _start, _level;
        static UILoader _u;

        // 静态自举入口（GameBootstrap.Start 交互分支调用）
        public static void Create()
        {
            if (_root != null) return;                      // 幂等
            if (UILoader.Instance == null) return;          // 防御：无 UI Canvas 静默跳过（与 SettingsPanel 一致）
            _u = UILoader.Instance;

            var root = new GameObject("MenuRoot", typeof(RectTransform));
            root.transform.SetParent(_u.transform, false);
            root.transform.SetAsLastSibling();
            // 嵌套子 Canvas 置顶：overrideSorting 隔离 + sortingOrder=100 盖住 HUD/面板(默认0)
            var cv = root.AddComponent<Canvas>();
            cv.overrideSorting = true;
            cv.sortingOrder = 100;
            root.AddComponent<GraphicRaycaster>();
            Stretch(root.GetComponent<RectTransform>());
            _root = root;

            _start = BuildStart(root.transform);
            _level = BuildLevelSelect(root.transform);
            ShowStart();
            Debug.Log("[FrontendUI] T10 开始页+关卡选择页已创建（MenuRoot 置顶，战斗被挡，待点开始）");
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // 全屏纯色遮光背景（挡住战斗 HUD + raycastTarget=true 拦截射线；勿设 alphaHitTestMinimumThreshold）
        static Image StretchPanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>());
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = true;
            return img;
        }

        // 通用文字（标题/副标题，sizeDelta 可大，带 Shadow 模拟辉光；SimHei 动态字体）
        static Text MakeText(Transform parent, Vector2 pos, Vector2 size, int fs, Color color, string text)
        {
            var go = new GameObject("Txt", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.GetComponent<Text>();
            t.font = _u.GetChineseFont(fs);
            t.fontSize = fs;
            t.color = color;
            t.text = text;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.6f);
            sh.effectDistance = new Vector2(2f, -2f);
            return t;
        }

        // 通用按钮（自建 Image+Button+文字；不依赖 MakeButtonImage 的固定 parent——它挂 UI Canvas 根会排在 MenuRoot 外）
        static void MakeButton(Transform parent, Vector2 pos, Vector2 size, string label, Color bg, Action onClick)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = bg;
            img.raycastTarget = true;
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;   // 主题色由我等手动控
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            _u.MakeButtonLabel(go.transform, Vector2.zero, 44, Color.white, label);
        }

        // ---- 开始页（start.html）----
        static GameObject BuildStart(Transform parent)
        {
            var p = new GameObject("StartPanel", typeof(RectTransform));
            p.transform.SetParent(parent, false);
            Stretch(p.GetComponent<RectTransform>());
            StretchPanel(p.transform, "Bg", new Color(0.04f, 0.03f, 0.06f, 0.94f));
            MakeText(p.transform, new Vector2(0f, 320f), new Vector2(700f, 40f), 30, new Color(1f, 0.84f, 0f), "天庭 VS 玄朝 · 决战天涯");
            MakeText(p.transform, new Vector2(0f, 190f), new Vector2(760f, 140f), 92, new Color(0.98f, 0.75f, 0.12f), "流  火");
            MakeButton(p.transform, new Vector2(0f, -40f), new Vector2(340f, 110f), "开始游戏", new Color(0.62f, 0.13f, 0.08f), ShowLevelSelect);
            MakeText(p.transform, new Vector2(0f, -480f), new Vector2(320f, 30f), 18, new Color(0.5f, 0.5f, 0.5f, 0.8f), "版本 v1.0 · T10 复刻");
            return p;
        }

        // ---- 关卡选择页（level-select.html）----
        static GameObject BuildLevelSelect(Transform parent)
        {
            var p = new GameObject("LevelSelectPanel", typeof(RectTransform));
            p.transform.SetParent(parent, false);
            Stretch(p.GetComponent<RectTransform>());
            StretchPanel(p.transform, "Bg", new Color(0.05f, 0.04f, 0.07f, 0.94f));
            MakeText(p.transform, new Vector2(0f, 320f), new Vector2(700f, 40f), 26, new Color(1f, 0.84f, 0f), "未来战场 · 荣耀对决");
            MakeText(p.transform, new Vector2(0f, 195f), new Vector2(700f, 120f), 82, new Color(0.98f, 0.75f, 0.12f), "选择关卡");
            MakeText(p.transform, new Vector2(0f, 115f), new Vector2(600f, 30f), 22, new Color(0.7f, 0.7f, 0.7f), "— 点击关卡 · 进入战场 —");
            BuildLevelCard(p.transform, new Vector2(0f, -80f));
            MakeButton(p.transform, new Vector2(0f, -440f), new Vector2(260f, 90f), "返回", new Color(0.25f, 0.25f, 0.3f), ShowStart);
            return p;
        }

        // 关卡卡片（普通关卡 · 难度·普通；点卡进战斗）
        static void BuildLevelCard(Transform parent, Vector2 pos)
        {
            var go = new GameObject("LevelCard", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(760f, 200f);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.13f, 0.11f, 0.10f, 0.9f);
            img.raycastTarget = true;
            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => EnterBattle());
            Debug.Log($"[FrontendUI] LevelCard 已建 active={go.activeInHierarchy} interactable={btn.IsInteractable()} rect={rt.sizeDelta} imgRaycast={img.raycastTarget}");
            MakeText(go.transform, new Vector2(-180f, 40f), new Vector2(380f, 60f), 46, Color.white, "普通关卡");
            MakeText(go.transform, new Vector2(-180f, -40f), new Vector2(380f, 40f), 26, new Color(1f, 0.84f, 0f), "难度 · 普通");
            MakeText(go.transform, new Vector2(280f, 40f), new Vector2(90f, 60f), 40, new Color(1f, 0.84f, 0f), "▶");
        }

        // 选关确认 -> 隐藏菜单 + 进入战斗
        static void EnterBattle()
        {
            Hide();
            if (GameManager.Instance != null) GameManager.Instance.SetState(GameState.Battle);
            Debug.Log("[FrontendUI] 选关确认 -> Battle（战斗 HUD 应可见可交互）");
        }

        public static void ShowStart()
        {
            if (_start != null) _start.SetActive(true);
            if (_level != null) _level.SetActive(false);
        }

        public static void ShowLevelSelect()
        {
            if (_start != null) _start.SetActive(false);
            if (_level != null) _level.SetActive(true);
            Debug.Log("[FrontendUI] ShowLevelSelect 已触发（点开始命中）");
        }

        public static void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }
    }
}

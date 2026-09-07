// SettingsPanel.cs —— A1（P0-8 HUD补全）：暂停面板 + 设置面板 + 抗锯齿/Bloom 双toggle 持久化
// 对位 Web src3d/ui/UIManager.js 的 openPauseMenu / openSettings（overlay.style.display 显隐控制）
// 职责：
//   - 暂停面板：ESC / 暂停按钮 -> Time.timeScale=0 + 显示（继续/设置/退出）
//   - 设置面板：抗锯齿 toggle（QualitySettings.antiAliasing，非即时，标记重建/重启）+
//               Bloom toggle（URP 全局 Volume.Bloom.weight，即时）
//   - 持久化：PlayerPrefs "liuhuo_config.display.antialias"/"liuhuo_config.display.bloom"，启动读取生效
// 关键坑（工部二 S4 §11.3）：
//   * 改 QualitySettings.antiAliasing 不即时效（需重建），改 URP Volume.weight 才即时。
//   * Bloom 在 Player 端易被 strip -> 用运行时自举全局 Volume 保 Bloom 资产被引用（A2 补 AlwaysIncluded）。
//   * 复用 UILoader 已建的 Canvas（FindFirstObjectByType<Canvas>）不新建，防双 Canvas。
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;          // Volume / VolumeProfile
using UnityEngine.Rendering.Universal; // Bloom / UniversalAdditionalCameraData

namespace Liuhuo.UI
{
    // A1 显示设置服务 + 暂停/设置面板（运行时自举，免场景预挂，对位 Web UIManager）
    public static class SettingsPanel
    {
        // PlayerPrefs 键（对位 liuhuo_config.display.antialias/bloom）
        public const string KeyAA = "liuhuo_config.display.antialias";
        public const string KeyBloom = "liuhuo_config.display.bloom";

        // Bloom 全局运行时引用（toggle 直接控 weight）
        private static Volume _postVolume;
        private static Bloom _bloom;

        // 当前面板显隐状态（防重复建/重复恢复 timeScale）
        private static bool _paused;
        private static GameObject _pauseRoot;
        private static GameObject _settingsRoot;

        // ===================== 入口：由 GameBootstrap.Start 调用 =====================
        public static void Create()
        {
            ApplySavedDisplay();          // 启动先读 PlayerPrefs 生效抗锯齿/Bloom
            var canvas = FindCanvas();    // 复用 UILoader 建的 Canvas
            if (canvas == null) return;   // Canvas 未就绪则跳过（保编译零错）
            BuildPauseMenu(canvas);
            BuildSettingsPanel(canvas);
            EnsureBloomVolume();
        }

        // 复用已存在的 UI Canvas（UILoader 建的"UI Canvas"），不新建防叠加
        private static Canvas FindCanvas()
        {
            return UnityEngine.Object.FindFirstObjectByType<Canvas>();
        }

        // ===================== 显示设置持久化 =====================
        // 读取 PlayerPrefs 应用抗锯齿/Bloom（启动 + 构建后各调一次）
        public static void ApplySavedDisplay()
        {
            bool aa = PlayerPrefs.GetInt(KeyAA, 1) == 1;      // 默认开
            bool bloom = PlayerPrefs.GetInt(KeyBloom, 1) == 1; // 默认开
            ApplyAntiAlias(aa);
            ApplyBloom(bloom);
        }

        // 抗锯齿：QualitySettings.antiAliasing（2/4/8；0=关）。非即时，需重建/重启生效，故仅记录 + 应用。
        public static void ApplyAntiAlias(bool on)
        {
            QualitySettings.antiAliasing = on ? 4 : 0;
            Debug.Log($"[SettingsPanel] 抗锯齿 -> {(on ? "ON(MSAA4)" : "OFF")}（QualitySettings.antiAliasing，重建生效）");
        }

        // Bloom：全局 URP Volume.Bloom.weight（即时生效）。0=仅关闭 weight，保留 Bloom 资产引用防 strip。
        public static void ApplyBloom(bool on)
        {
            if (_bloom != null)
            {
                _bloom.intensity.Override(on ? 1f : 0f);
                Debug.Log($"[SettingsPanel] Bloom -> {(on ? "ON(weight=1)" : "OFF(weight=0)")}（URP Volume.weight 即时）");
            }
            else
            {
                Debug.LogWarning("[SettingsPanel] Bloom 组件未就绪，仅记录开关状态，待 EnsureBloomVolume 后生效");
            }
        }

        // 持久化显式写（toggle 回调触发）
        public static void SaveDisplay(bool aa, bool bloom)
        {
            PlayerPrefs.SetInt(KeyAA, aa ? 1 : 0);
            PlayerPrefs.SetInt(KeyBloom, bloom ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log($"[SettingsPanel] 显示设置持久化 -> AA={aa} Bloom={bloom}");
        }

        // ===================== Bloom 运行时自举 =====================
        // 生成全局 Volume(Bloom) 挂主相机后处理，toggle 即时控 weight。
        // ★P1-5 问题2修复：build 端 Bloom 易被 strip（URP_Renderer.asset 的 postProcessData 为 null 且 m_AlwaysIncludedShaders
        //   不含 Bloom 后处理 shader），本项目本机也无 PostProcessData 资产。此处按"只有 C#、不坏现有逻辑"约束：
        //   ① try/catch 包裹运行时自举，任何后处理缺失/异常都只降级不崩启动；② 显式把全局 Volume 挂 Default 层(layer 0)，
        //   确保主相机默认 VolumeMask 能命中它（否则 Bloom 组件挂上了相机也不采样）；③ 对 Add<Bloom> 结果做空判兜底。
        //   注：真正的根治（挂 PostProcessData 资产 / 增补 m_AlwaysIncludedShaders）超出本任务"只改 C#"约束，见报告。
        private static void EnsureBloomVolume()
        {
            // 主相机
            var cam = Camera.main;
            if (cam == null) { Debug.LogWarning("[SettingsPanel] 无主相机，Bloom Volume 待机"); return; }

            try
            {
                // 给主相机开启 URP 后处理标记（缺 renderPostProcessing 则 Bloom 不生效）
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;

                // 全局 Volume（isGlobal 覆盖画面）
                if (_postVolume == null)
                {
                    var go = new GameObject("Global_PostFX_Volume", typeof(Volume));
                    // ★补强：显式把 Volume 挂 Default 层(layer 0)，确保主相机默认 VolumeMask(全层)能命中该全局 Volume，
                    //   否则 Bloom 组件即使挂在 Volume 上相机也不采样 → Bloom 不生效（build 端 strip 残留风险）。
                    go.layer = 0;
                    _postVolume = go.GetComponent<Volume>();
                    _postVolume.isGlobal = true;
                    _postVolume.priority = 1f;

                    var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                    _bloom = profile.Add<Bloom>(true);
                    if (_bloom == null)
                    {
                        Debug.LogWarning("[SettingsPanel] 运行时 Add<Bloom> 失败(Bloom类型未加载)，Bloom 降级为标志位，仅不崩");
                        _postVolume.profile = profile;
                        return;
                    }
                    _bloom.intensity.Override(1f);
                    _bloom.threshold.Override(1.1f);
                    _postVolume.profile = profile;
                }

                // 用持久化状态收敛当前weight
                bool saved = PlayerPrefs.GetInt(KeyBloom, 1) == 1;
                if (_bloom != null) _bloom.intensity.Override(saved ? 1f : 0f);
                Debug.Log($"[SettingsPanel] Bloom Volume 自举完成 isGlobal={_postVolume.isGlobal} weight={(saved ? 1f : 0f)}（A1 对位 Web Bloom 开关）");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[SettingsPanel] EnsureBloomVolume 运行时异常（防御捕获，不阻断启动）: " + e.Message);
            }
        }

        // ===================== 暂停面板 =====================
        private static void BuildPauseMenu(Canvas canvas)
        {
            var root = new GameObject("PausePanel", typeof(RectTransform), typeof(Image));  // 全屏半透明遮罩
            root.transform.SetParent(canvas.transform, false);
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = root.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.55f);
            img.raycastTarget = true;   // 拦截点击（暂停时禁止误触下层）
            root.SetActive(false);
            _pauseRoot = root;

            MakeLabel(root.transform, "暂停", new Vector2(0f, 150f), 46, Color.white);

            MakeButton(root.transform, "Continue", "继续", new Vector2(0f, 40f), on: () => { SetPaused(false); });
            MakeButton(root.transform, "Settings", "设置", new Vector2(0f, -60f), on: () => { ToggleSettings(true); });
            MakeButton(root.transform, "Quit", "退出", new Vector2(0f, -160f), on: () => { QuitGame(); });
            Debug.Log("[SettingsPanel] 暂停面板已建（继续/设置/退出，ESC 切换显隐）");
        }

        // ===================== 设置面板 =====================
        private static void BuildSettingsPanel(Canvas canvas)
        {
            var root = new GameObject("SettingsPanel", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(canvas.transform, false);
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(520f, 420f);
            rt.anchoredPosition = Vector2.zero;
            var img = root.GetComponent<Image>();
            img.color = new Color(0.12f, 0.12f, 0.15f, 0.96f);
            img.raycastTarget = true;
            root.SetActive(false);
            _settingsRoot = root;

            MakeLabel(root.transform, "设置", new Vector2(0f, 170f), 40, Color.white);

            // 抗锯齿 toggle
            bool aa = PlayerPrefs.GetInt(KeyAA, 1) == 1;
            MakeToggle(root.transform, "抗锯齿 (MSAA)", new Vector2(0f, 90f), aa,
                on: v => { ApplyAntiAlias(v); SaveDisplay(v, PlayerPrefs.GetInt(KeyBloom, 1) == 1); });

            // Bloom toggle
            bool bloom = PlayerPrefs.GetInt(KeyBloom, 1) == 1;
            MakeToggle(root.transform, "Bloom", new Vector2(0f, 20f), bloom,
                on: v => { ApplyBloom(v); SaveDisplay(PlayerPrefs.GetInt(KeyAA, 1) == 1, v); });

            MakeButton(root.transform, "Back", "返回", new Vector2(0f, -120f), on: () => { ToggleSettings(false); });
            Debug.Log("[SettingsPanel] 设置面板已建（抗锯齿/Bloom 双toggle 持久化）");
        }

        // ===================== 交互逻辑 =====================
        public static void SetPaused(bool paused)
        {
            if (_paused == paused) return;
            _paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            if (_pauseRoot != null) _pauseRoot.SetActive(paused);
            if (paused) { /* 打开暂停时默认收起设置 */ ToggleSettings(false); }
            Debug.Log($"[SettingsPanel] 暂停 -> {paused} timeScale={Time.timeScale}");
        }
        public static bool IsPaused => _paused;

        private static void ToggleSettings(bool show)
        {
            if (_settingsRoot != null) _settingsRoot.SetActive(show);
            if (_pauseRoot != null) _pauseRoot.SetActive(!show);   // 设置面板打开时隐藏暂停面板
        }

        private static void QuitGame()
        {
            Debug.Log("[SettingsPanel] 退出游戏");
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #else
            Application.Quit();
            #endif
        }

        // 说明：ESC 监听放 GameBootstrap.Update（见其 A1 接线），本静态类不新增 MonoBehaviour。

        // ===================== 装配工具（复用 UILoader 同级风格） =====================
        private static Text MakeLabel(Transform parent, string text, Vector2 pos, int size, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(320f, 50f);
            var t = go.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.text = text;
            return t;   // MakeLabel 返回 Text（L282 var text = MakeLabel(...) 需赋值，原 void 致 CS0815）
        }

        private static void MakeButton(Transform parent, string name, string label, Vector2 pos, Action on)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(180f, 54f);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.35f, 0.35f, 0.45f, 1f);
            img.raycastTarget = true;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.ColorTint;
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var lrt = labelGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var l = labelGo.AddComponent<Text>();
            l.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            l.fontSize = 26; l.alignment = TextAnchor.MiddleCenter; l.color = Color.white; l.text = label;
            btn.onClick.AddListener(() => on?.Invoke());
        }

        private static void MakeToggle(Transform parent, string label, Vector2 pos, bool value, Action<bool> on)
        {
            var go = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(300f, 44f);
            var tg = go.GetComponent<Toggle>();
            tg.isOn = value;
            // 左侧背景
            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(go.transform, false);
            var brt = bg.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            var bimg = bg.GetComponent<Image>();
            bimg.color = new Color(0.25f, 0.25f, 0.32f, 1f);
            tg.targetGraphic = bimg;
            // Checkmark
            var check = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            check.transform.SetParent(go.transform, false);
            var crt = check.GetComponent<RectTransform>();
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.zero;
            crt.pivot = new Vector2(0f, 0.5f);
            crt.anchoredPosition = new Vector2(20f, 0f);
            crt.sizeDelta = new Vector2(20f, 20f);
            var cimg = check.GetComponent<Image>();
            cimg.color = Color.green;
            cimg.raycastTarget = false;
            tg.graphic = cimg;
            // 右侧文本
            var text = MakeLabel(go.transform, label, new Vector2(70f, 0f), 26, Color.white);
            text.alignment = TextAnchor.MiddleLeft;
            text.rectTransform.sizeDelta = new Vector2(220f, 44f);
        }
    }
}

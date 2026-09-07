// UILoader.cs —— u3d-align Phase-B P0-4：四按钮三面板 UI 加载（对位 Web src3d/ui/UIManager.js 视觉层）
// 职责：运行时以代码建 UGUI Canvas + 加载真实 PNG 资产(Resources/ui) + 把 Slider/Text/Button 注入 HUDController。
// 对位 HUDController 字段（已核对其真实声明）：
//   baseHpTianting/baseHpXuanchao(Slider)、soldierCount*/killCount*(Text)、btnSummon/btnSummonRed/btnUlt/btnUltEnemy(Button)
//   tiantingTheme/xuanchaoTheme(FactionTheme.primary/accent)、ult(UltController)、OnAction/SetCooldown/SetFaction/UpdateHud
// 关键坑：
//   * 黑底 PNG -> 用 ui_new/cutout 透明贴图（已复制到 Resources/ui），并对 Image 设 alphaHitTestMinimumThreshold 使射线忽略透明像素。
//   * 材质用 UI/Default（UGUI Image 缺省即 UI/Default，无需显式赋值；若需注雪可覆盖 material）。
//   * setNativeSize + preserveAspect：Sprite.Create 用 pixelsPerUnit=1 令原生尺寸=像素尺寸，再统一 localScale 收敛到目标框（保持长宽比）。
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Liuhuo.FX;   // UltController.EnsureCreated()（C 轴大招自举）

namespace Liuhuo.UI
{
    // P0-4 UI 加载器：自举式静态 Create()（对位 UltController.EnsureCreated 免场景预挂），持有 HUDController 引用转发调用。
    public class UILoader : MonoBehaviour
    {
        // 全局入口（对外只走 Create，Instance 是给其他系统弱引用阅读的）
        public static UILoader Instance;

        [Header("注入的 HUDController（由本加载器寻找/创建并填充）")]
        public HUDController hud;

        [Header("运行时可见实例（供外部读）")]
        public Text timerText;   // 中央信息面板 "02:00:00" 计时文字（代码运行时覆盖）

        // ---- 视觉主题切换所需引用（SetFaction 视觉层在 UILoader 内实现，勿改 HUDController）----
        private Image _panelXuanchao, _panelTianting, _panelCenter;
        private Image _btnSummon, _btnSummonRed, _btnUlt, _btnUltEnemy;   // 底四按钮
        private Image _fillTianting, _fillXuanchao;                       // 两血量条 fill
        private bool _instantiatedHud;                                     // 是否本加载器新建了 HUDController

        // ===================== 入口 =====================
        // 建 Canvas(Screen Space Overlay) + EventSystem(如无) + CanvasScaler(1920x1080)，并装配全部 UI。
        public static UILoader Create()
        {
            var canvasGo = new GameObject("UI Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UILoader));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var loader = canvasGo.GetComponent<UILoader>();
            Instance = loader;
            loader.EnsureEventSystem();
            loader.BuildHud();        // 先找/建 HUDController（其 Start 在下一帧绑定按钮，故必须早于赋值）
            loader.BuildPanels();     // 顶部三面板 + 血条 Slider + 计数 Text + 中央计时
            loader.BuildButtons();    // 底部四按钮
            loader.RebindIfNeeded();  // 兜底接线（复用已有 HUDController 时）
            loader.SetFactionVisual("tianting");   // 默认天庭主题
            return loader;
        }

        // EventSystem：场景无则创建（否则 Button 点击无响应）
        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // 寻找已有 HUDController，否则新建挂到 Canvas 下（其 Start 下一帧自动绑定四按钮 + UltController 接线）。
        private void BuildHud()
        {
            hud = FindFirstObjectByType<HUDController>();
            if (hud == null)
            {
                var go = new GameObject("HUDController");
                go.transform.SetParent(transform, false);
                hud = go.AddComponent<HUDController>();
                _instantiatedHud = true;
            }
        }

        // ===================== 顶部三面板 =====================
        // 任务1（用户拍板）：天庭面板置左(-520)、玄朝面板置右(+520)——与原版 Web 天庭左/玄朝右一致（u3d 原为玄朝左/天庭右，已对调）。
        private void BuildPanels()
        {
            // 天庭 HP（左）—— native 尺寸=像素，localScale 收敛进 640x160 框（preserveAspect 保比例）
            _panelTianting = MakePanel("Panel_Tianting_Hp", "ui/panel_tianting_hp", new Vector2(-520f, 400f), new Vector2(640f, 160f));
            var barT = MakeSlider(_panelTianting.transform, new Vector2(75.5f, -5f), new Vector2(500f, 26f));   // ★用户手动改 图一 HPBar: pos(75.5,-5) size(500,26)
            _fillTianting = barT.fillRect.GetComponent<Image>();
            _fillTianting.color = new Color(1f, 1f, 52f / 255f);   // ★用户手动改 天庭血条 fill 金 #FFFF34 (R255 G255 B52)
            // ★用户手动改 图二 天庭血条 Fill RectTransform（stretch）：Left-38 / Top-6 / Right-5 / Bottom-15
            //   = offsetMin(-38,-15) / offsetMax(5,6)；anchors 保持 MakeSlider 默认 stretch(0/1)，
            //   Slider 仅驱动 anchorMax.x（水平随血量），垂直+水平 offset 静态固化。
            var frT = barT.fillRect;
            frT.offsetMin = new Vector2(-38f, -15f);
            frT.offsetMax = new Vector2(5f, 6f);
            hud.baseHpTianting = barT;
            hud.soldierCountTianting = MakeLabel(_panelTianting.transform, new Vector2(-300f, -48f), 32, Color.white);
            hud.killCountTianting = MakeLabel(_panelTianting.transform, new Vector2(300f, -48f), 32, Color.white);
            // P1-6 缺口2：天庭基地血量数值 Text（叠在血条中央，随基地受击实时下降）
            hud.hpTextTianting = MakeLabel(_panelTianting.transform, new Vector2(-5f, -2.5f), 24, Color.black);   // ★用户手动改 图三 HpLabel: pos(-5,-2.5) + 天庭血条数值字体改黑
            // ★块C（文部 I5 失分：面板无阵营名）：补天庭阵营名，对位 Web UIManager.js .lx-faction-name（天庭白金）
            MakeButtonLabel(_panelTianting.transform, new Vector2(0f, 46f), 36, Color.white, "天庭");   // ★块C（文部 I5 失分：面板无阵营名）：补天庭阵营名，对位 Web UIManager.js .lx-faction-name（天庭白金）//★T4 阵营名字号28→36 放大适配UI

            // 中央信息面板——叠 "02:00:00" 计时
            _panelCenter = MakePanel("Panel_Center_Info", "ui/panel_center_info", new Vector2(0f, 400f), new Vector2(460f, 150f));
            timerText = MakeLabel(_panelCenter.transform, new Vector2(0f, 0f), 44, new Color(1f, 0.9f, 0.4f));
            timerText.text = "02:00:00";

            // 玄朝 HP（右）—— 任务1 用户拍板：玄朝面板置右，与原版"天庭左/玄朝右"一致
            _panelXuanchao = MakePanel("Panel_Xuanchao_Hp", "ui/panel_xuanchao_hp", new Vector2(520f, 400f), new Vector2(640f, 160f));
            var barX = MakeSlider(_panelXuanchao.transform, new Vector2(-10f, -3.6f), new Vector2(521f, 25f));   // ★用户手动改 图四 HPBar: pos(-10,-3.6) size(521,25)
            _fillXuanchao = barX.fillRect.GetComponent<Image>();
            _fillXuanchao.color = new Color(1f, 0f, 0f);   // ★用户手动改 玄朝血条 fill 红 #FF0000 (R255 G0 B0)
            hud.baseHpXuanchao = barX;
            hud.soldierCountXuanchao = MakeLabel(_panelXuanchao.transform, new Vector2(-300f, -48f), 32, Color.white);
            hud.killCountXuanchao = MakeLabel(_panelXuanchao.transform, new Vector2(300f, -48f), 32, Color.white);
            // P1-6 缺口2：玄朝基地血量数值 Text（叠在血条中央）
            hud.hpTextXuanchao = MakeLabel(_panelXuanchao.transform, new Vector2(0f, 0f), 24, Color.white);   // ★用户手动改 图六 HpLabel: pos(0,~0)
            // ★块C：补玄朝阵营名，对位 Web .lx-faction-name（玄朝红 #DC143C）
            MakeButtonLabel(_panelXuanchao.transform, new Vector2(0f, 46f), 36, new Color(0.86f, 0.11f, 0.24f), "玄朝");   //★T4 阵营名字号28→36 放大适配UI
            // 任务1 验证探针：打印两面板 anchoredPosition，铁证天庭(-520 左)/玄朝(+520 右)已互换
            Debug.Log("[PanelProbe] 天庭Hp=" + _panelTianting.rectTransform.anchoredPosition + " 玄朝Hp=" + _panelXuanchao.rectTransform.anchoredPosition);
        }

        // ===================== 底部四按钮 =====================
        // 对位 Web 按钮 key：summon(天庭召唤)/summon-red(玄朝召唤)/ult(圣裁)/ultEnemy(同死)
        private void BuildButtons()
        {
            // ★块B（文部 I4 失分：按钮文字全消失）：原实现只 MakeButtonImage 建背景，未建文字。
            // 对位 Web UIManager.js 实测色值（§零·4）：玄朝召唤 #fff / 同死 #fff / 圣裁 #5a4700 / 天庭召唤 #5a4700；
            // 文字由 MakeButtonLabel 补齐，siblingIndex 置顶防被按钮 Image 遮住。
            var darkGold = new Color(0x5a / 255f, 0x47 / 255f, 0x00 / 255f, 1f);   // #5a4700 暗金（天庭阵营/圣裁）

            // ★任务9（用户拍板同位交换，S4 复审锚定）：当前布局(玄朝召唤@-540/同死@-180/圣裁@+180/天庭召唤@+540)。用户拍板最终顺序(从左到右)=
            //   天庭召唤 | 圣裁 | 同死 | 玄朝召唤（天庭系全左、玄朝系全右）。故四钮 x 全部取反：天庭召唤540→-540、圣裁180→-180、同死-180→180、玄朝召唤-540→540。
            //   y=-380、字号80、文案/图标/功能/触发全不动（只换位置）。
            _btnSummon = MakeButtonImage("Btn_Summon", "ui/btn_tianting_summon", new Vector2(-540f, -380f), new Vector2(200f, 120f));
            hud.btnSummon = _btnSummon.GetComponent<Button>();
            MakeButtonLabel(_btnSummon.transform, Vector2.zero, 80, darkGold, "天庭召唤");   // ★用户手动改 四按钮字号44→80

            _btnUlt = MakeButtonImage("Btn_Ult", "ui/btn_shengcai_ult", new Vector2(-180f, -380f), new Vector2(200f, 120f));
            hud.btnUlt = _btnUlt.GetComponent<Button>();
            MakeButtonLabel(_btnUlt.transform, Vector2.zero, 80, darkGold, "圣裁");   // ★用户手动改 四按钮字号44→80

            _btnUltEnemy = MakeButtonImage("Btn_UltEnemy", "ui/btn_tongsi_ult", new Vector2(180f, -380f), new Vector2(200f, 120f));
            hud.btnUltEnemy = _btnUltEnemy.GetComponent<Button>();
            MakeButtonLabel(_btnUltEnemy.transform, Vector2.zero, 80, Color.white, "同死");   // ★用户手动改 四按钮字号44→80

            _btnSummonRed = MakeButtonImage("Btn_SummonRed", "ui/btn_xuanchao_summon", new Vector2(540f, -380f), new Vector2(200f, 120f));
            hud.btnSummonRed = _btnSummonRed.GetComponent<Button>();
            MakeButtonLabel(_btnSummonRed.transform, Vector2.zero, 80, Color.white, "玄朝召唤");   // ★用户手动改 四按钮字号44→80
        }

        // 复用已有 HUDController 时，若其 Start 已跑过，原按钮监听绑定在旧按钮上，需给我们的新按钮兜底接大招。
        // 判定依据：Start 是唯一会填充 hud.ult 的地方；ult != null 即视为 Start 已执行（否则勿重复挂，避免双重触发）。
        private void RebindIfNeeded()
        {
            if (_instantiatedHud) return;   // 自建实例 Start 尚未跑，下一帧自动绑定，无需兜底
            if (hud == null || hud.ult == null) return;   // Start 未执行 -> 其会自动绑定到已赋值的新按钮
            if (hud.btnUlt != null) hud.btnUlt.onClick.AddListener(() => hud.ult.TriggerUltimate(0));
            if (hud.btnUltEnemy != null) hud.btnUltEnemy.onClick.AddListener(() => hud.ult.TriggerUltimate(1));
            Debug.LogWarning("[UILoader] 复用已有 HUDController：其 Start 已执行，已兜底接管新按钮大招；召唤按钮动作需外部经 hud.OnAction(key,fn) 注册");
        }

        // ===================== 阵营切换（视觉层，UILoader 内实现，勿改 HUDController.SetFaction 的 TODO）=====================
        public void SetFactionVisual(string faction)
        {
            if (hud == null) return;
            hud.SetFaction(faction);   // 记录 _faction（其切换视觉为 TODO，故视觉在此实现）
            bool xuan = faction == "xuanchao";
            // ★任务4：玄朝召唤/同死(玄朝系)去降饱和置灰——恒走高亮分支(原图红黑亮色)，不随阵营切换置灰；天庭系按钮仍随阵营高亮/置灰。
            ApplyButtonTheme(_btnSummon, hud.tiantingTheme, !xuan);       // 天庭召唤
            ApplyButtonTheme(_btnUlt, hud.tiantingTheme, !xuan);          // 圣裁(天庭)
            ApplyButtonTheme(_btnSummonRed, hud.xuanchaoTheme, true);     // 玄朝召唤——任务4 恒高亮(去置灰)
            ApplyButtonTheme(_btnUltEnemy, hud.xuanchaoTheme, true);      // 同死——任务4 恒高亮(去置灰)
            Debug.Log($"[UILoader] 阵营切换 -> {faction} 玄朝召唤色={(_btnSummonRed==null?"null":_btnSummonRed.color.ToString())} 同死色={(_btnUltEnemy==null?"null":_btnUltEnemy.color.ToString())}");
        }

        private void ApplyButtonTheme(Image img, FactionTheme theme, bool active)
        {
            if (img == null) return;
            // 保留按钮美术的前提下叠主题色：激活=向 primary 靠拢，非激活=降饱和灰
            img.color = active ? Color.Lerp(Color.white, theme.primary, 0.55f)
                               : new Color(0.55f, 0.55f, 0.55f, 0.75f);
        }

        // ===================== 对外转发（持有 hud 引用）=====================
        public void UpdateHud(float tiantingHpPct, float xuanchaoHpPct, float tiantingHp, float xuanchaoHp, int tSoldier, int xSoldier, int tKill, int xKill)
            => hud?.UpdateHud(tiantingHpPct, xuanchaoHpPct, tiantingHp, xuanchaoHp, tSoldier, xSoldier, tKill, xKill);
        public void SetCooldown(string key, float remain) => hud?.SetCooldown(key, remain);
        public void OnAction(string key, Action fn) => hud?.OnAction(key, fn);
        public void SetTimer(string s) { if (timerText) timerText.text = s; }

        // ===================== 装配工具 =====================
        // 建面板 Image：加载 Sprite + 原生尺寸 + 收敛到目标框 + preserveAspect 保比例
        internal Image MakePanel(string name, string resPath, Vector2 pos, Vector2 maxSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.sprite = LoadSprite(resPath);
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.raycastTarget = false;                       // 面板不拦截点击
            // 注：不可在此设 alphaHitTestMinimumThreshold——Resources 的 cutout PNG 为 Non-Readable，
            //     Unity 抛 InvalidOperationException（"alphaHitTestMinimumThreshold ... not readeable"）。
            //     cutout 真透明区 alpha=0，默认阈值亦不拦点击，故省略。
            SetNativeFit(img, maxSize);
            img.rectTransform.anchoredPosition = pos;
            Debug.Log($"[PanelProbe] {name} resPath={resPath} sizeDelta={img.rectTransform.sizeDelta} localScale={img.rectTransform.localScale} worldScale={img.transform.lossyScale} rect={img.rectTransform.rect}");
            return img;
        }

        // 建按钮 Image（另挂 Button）——底四按钮，可交互
        internal Image MakeButtonImage(string name, string resPath, Vector2 pos, Vector2 maxSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.sprite = LoadSprite(resPath);
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.raycastTarget = true;
            // 注：同 MakePanel——Resources cutout PNG 为 Non-Readable，勿设 alphaHitTestMinimumThreshold 以免抛异常；
            //     透明区 alpha=0 默认不拦点击。
            SetNativeFit(img, maxSize);
            img.rectTransform.anchoredPosition = pos;
            // ★BtnProbe：打印按钮屏幕坐标(position=ScreenSpaceOverlay下即屏幕像素)、anchored、激活态，定位按钮是否在视口
            Debug.Log($"[BtnProbe] {name} resPath={resPath} pos={img.rectTransform.position} anchored={img.rectTransform.anchoredPosition} active={go.activeInHierarchy} parentTrans={(img.transform.parent != null)} sizeDelta={img.rectTransform.sizeDelta} localScale={img.rectTransform.localScale} worldScale={img.transform.lossyScale} rect={img.rectTransform.rect}");

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;     // 主题色由 SetFactionVisual 手动控，禁默认色阶干扰
            btn.targetGraphic = img;
            // ★T4（用户第4项：鼠标悬停=升起+高亮响应）：挂悬停反馈
            AttachHover(img);
            return img;
        }

        // ★T4 悬停反馈：按钮鼠标悬停→上移+放大(高亮响应)，移出还原。
        //   不碰 color（禁用 SetFactionVisual/ApplyButtonTheme 的主题色打架）；升距 16px + 放大 1.08 即"升起+高亮"视觉。
        private static void AttachHover(Image img)
        {
            if (img == null) return;
            var rt = img.rectTransform;
            Vector2 basePos = rt.anchoredPosition;
            Vector3 baseScale = rt.localScale;
            var et = img.gameObject.GetComponent<EventTrigger>();
            if (et == null) et = img.gameObject.AddComponent<EventTrigger>();
            et.triggers.Clear();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => { rt.anchoredPosition = basePos + new Vector2(0f, 16f); rt.localScale = new Vector3(baseScale.x * 1.08f, baseScale.y * 1.08f, 1f); });
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => { rt.anchoredPosition = basePos; rt.localScale = baseScale; });
            et.triggers.Add(enter);
            et.triggers.Add(exit);
        }

        // 建 Slider（0..1 血条，纯展示不可交互），fill 赋予 fillRect 供 Slider 自动驱动
        private Slider MakeSlider(Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("HPBar", typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var s = go.GetComponent<Slider>();
            s.minValue = 0f; s.maxValue = 1f; s.direction = Slider.Direction.LeftToRight;
            s.transition = Selectable.Transition.None;
            s.interactable = false;                          // 纯展示，勿拖拽

            // Background（targetGraphic，撑满）
            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(go.transform, false);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.35f);
            bgImg.raycastTarget = false;
            s.targetGraphic = bgImg;

            // Fill Area（撑满）
            var fa = new GameObject("Fill Area", typeof(RectTransform));
            fa.transform.SetParent(go.transform, false);
            var faRt = fa.GetComponent<RectTransform>();
            faRt.anchorMin = Vector2.zero; faRt.anchorMax = Vector2.one;
            faRt.offsetMin = Vector2.zero; faRt.offsetMax = Vector2.zero;

            // Fill（fillRect：Slider 依值动其 anchorMax.x）
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fa.transform, false);
            var fRt = fill.GetComponent<RectTransform>();
            fRt.anchorMin = Vector2.zero; fRt.anchorMax = Vector2.one;
            fRt.offsetMin = Vector2.zero; fRt.offsetMax = Vector2.zero;
            var fImg = fill.GetComponent<Image>();
            fImg.raycastTarget = false;
            s.fillRect = fRt;

            s.SetValueWithoutNotify(1f);   // 满血起始，刷新填充视觉
            return s;
        }

        // 建 Text 标签（LegacyRuntime.ttf；2022.2+ 已弃 Arial）
        internal Text MakeLabel(Transform parent, Vector2 pos, int fontSize, Color color)
        {
            var go = new GameObject("HpLabel", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(180f, 60f);

            var t = go.GetComponent<Text>();
            t.font = GetChineseFont(fontSize);
            t.fontSize = fontSize;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.text = "0";
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            Debug.Log($"[UILoader] MakeLabel text={t.text} font={t.font.name} size={fontSize} color={color} parent={parent.name}");
            return t;
        }

        // 加载 Sprite：Resources/ui/<resPath>，Texture2D -> Sprite（pixelsPerUnit=1 令原生尺寸=像素）
        // ★块D加固（lesson09#97 isReadable 坑）：Non-Readable 纹理 Sprite.Create 产物无效，退化为纯色方块；
        //   Resources 的 PNG 默认 isReadable:0。加固：判空 + isReadable 检查 + try/catch 兜底 + 日志，防静默崩坏。
        internal Sprite LoadSprite(string resPath)
        {
            var tex = Resources.Load<Texture2D>(resPath);
            if (tex == null) { Debug.LogWarning($"[UILoader] 资源加载失败: {resPath}"); return null; }
            try
            {
                if (!tex.isReadable)
                {
                    Debug.LogWarning($"[UILoader] {resPath} 纹理 Non-Readable（isReadable:0），Sprite.Create 无效——需在导入设置设 isReadable:1。已兜底返回 null 防纯色方块。");
                    return null;
                }
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 1f);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UILoader] {resPath} Sprite.Create 异常: {e.Message}");
                return null;
            }
        }

        // 建按钮/面板文字标签（块B/块C）——对位 Web UIManager.js inline 实测（§零·4）：
        //   按钮颜色 = 玄朝召唤 #fff / 同死 #fff / 圣裁 #5a4700 / 天庭召唤 #5a4700；阵营名用阵营主题色（天庭白金/玄朝红）。
        //   u3d Canvas 1920 基准按比例放大；siblingIndex 置顶防被面板/按钮 Image 遮住（文部 I4 曾因文字不可见判 0）。
        internal Text MakeButtonLabel(Transform parent, Vector2 pos, int fontSize, Color color, string text)
        {
            // ★T4（用户第4项：按钮/阵营名中文看不清 + 需放大适配UI）：撤销旧"烘焙 Sprite"方案。
            //   旧方案源于 debugger"SimHei 打包运行时对 CJK 栅格化失败"的定性——但 Player.log 实证推翻：
            //   [UILoader] SimHei loaded: hasChar天=True hasChar庭=True 以及 [FloatText] 中文栅格化=OK。
            //   故 SimHei 动态字体在打包 exe 可渲染中文，改用 real UGUI Text + 中文 Shadow(Color(0,0,0,0.7)) 防对比度不足。
            var go = new GameObject("BtnLabel", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(280f, 76f);

            var t = go.GetComponent<Text>();
            t.font = GetChineseFont(fontSize);
            t.fontSize = fontSize;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.transform.SetAsLastSibling();   // 置顶防遮挡
            // ★T4：中文 Shadow 防白底/亮底对比度不足（lesson09 中文 UI 教训）
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.7f);
            sh.effectDistance = new Vector2(2f, -2f);
            Debug.Log($"[UILoader] MakeButtonLabel text={text} font={t.font.name} size={fontSize} color={color} parent={parent.name} pos={pos} sibling={t.transform.GetSiblingIndex()}");
            return t;
        }

        // ★块B/C 文字真正渲染（lesson09：LegacyRuntime.ttf 内置 Latin 无中文字形→中文汉字渲染为空，数字/拉丁能显；
        //   CreateDynamicFontFromOSFont 在 Standalone Player 渲染中文亦不可靠——数字能显中文不能，是动态字体 atlas 未载中文字形）。
        //   ★改用【导入字体资源】黑体 SimHei.ttf（Resources/fonts/SimHei，includeFontData:1 动态字体，UGUI 中文标准可靠做法，
        //   运行时按需渲染任意字符含中文）。兜底回内置字体保数字/拉丁。
        internal Font GetChineseFont(int size)
        {
            try
            {
                var f = Resources.Load<Font>("fonts/SimHei");
                if (f != null)
                {
                    // ★S5 运行时探针（§零·1 dev-closed-loop）：确认动态字体在构建里能否真栅格化 CJK。
                    //   若 HasCharacter('天')=false 且 characterInfo 只含数字，则证明"字体atlas无CJK"（需换 TextMeshPro/烘焙）。
                    var probe = "天庭召唤圣裁死玄朝";
                    try { f.RequestCharactersInTexture(probe, size, FontStyle.Normal); } catch { }
                    Debug.Log($"[UILoader] SimHei loaded: dynamic={f.dynamic} hasChar天={f.HasCharacter('天')} hasChar庭={f.HasCharacter('庭')} hasChar召={f.HasCharacter('召')} chars={f.characterInfo.Length}");
                    return f;
                }
                Debug.LogWarning("[UILoader] resources/fonts/SimHei 未加载，兜底内置字体 LegacyRuntime.ttf");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UILoader] 加载 SimHei 字体异常: {e.Message}");
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // nativeSize 基准 + 统一 localScale 收敛进 target（uniform scale 保长宽比 = preserveAspect 的代码保证）
        // nativeSize 基准 + 统一 localScale 收敛进 target（uniform scale 保长宽比 = preserveAspect 的代码保证）
        private void SetNativeFit(Image img, Vector2 maxSize)
        {
            if (img.sprite == null) return;
            // ★修复（debugger 铁证闭环）：勿用 Image.SetNativeSize()——
            //   Image.pixelsPerUnit = sprite.ppu / Canvas.referencePixelsPerUnit = 1/100 = 0.01，
            //   故 SetNativeSize 把 sizeDelta 膨胀 referencePixelsPerUnit 倍(72x45→7200x4500)，
            //   再 SetNativeFit 读 rt.rect.width=7200 → scale=0.01 → worldScale≈0 → Sprite 渲染<1px 不可见。
            //   这是独立于字体栅格化的第2个死结。改为直接按 sprite.rect 像素设 sizeDelta，再 localScale 收敛进 maxSize。
            var rt = img.rectTransform;
            float pw = img.sprite.rect.width, ph = img.sprite.rect.height;
            rt.sizeDelta = new Vector2(pw, ph);
            float w = Mathf.Max(pw, 1f), h = Mathf.Max(ph, 1f);
            float scale = Mathf.Min(1f, Mathf.Min(maxSize.x / w, maxSize.y / h));
            rt.localScale = new Vector3(scale, scale, 1f);
        }
    }
}

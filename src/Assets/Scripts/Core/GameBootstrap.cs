// GameBootstrap.cs —— 主入口/全局协调（对位 Web main.js 编排层）+ 存读档 + 对象池
// 单文件聚合骨架（仅 GameManager 一个 MonoBehaviour 匹配文件名，SaveSystem/ObjectPool 为静态工具类）。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Liuhuo.Audio;   // P6(F5) 音频接线：GameBootstrap 调 AudioController
using Liuhuo.Model;   // Phase-B P0-3 BaseModelFactory 基地代码模型（白金宫殿/黑红堡垒）
using Liuhuo.UI;      // Phase-B P0-4 UILoader 四按钮三面板（对位 Web UIManager）
using Liuhuo.FX;      // s5 v3.2：AutoUlt 调 UltController（大招视觉验证）
using UnityEngine.Profiling;   // S5 Mono探针 GetMonoUsedSizeLong

namespace Liuhuo.Core
{
    public enum GameState { Start, LevelSelect, Battle, Pause, GameOver }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance;
        public GameState State { get; private set; } = GameState.Start;

        // S5 注入：士兵工厂 + 胜负判定记录
        [Header("S5 战斗协调")]
        public SoldierFactory factory;
        public BaseSystem blueBase;   // 天庭(蓝)
        public BaseSystem redBase;    // 玄朝(红)
        public int winTeam = -1;       // -1=未分胜负；0=天庭胜/1=玄朝胜

        // P2 第七轮(TC-F2): ProfilerRecorder 采 GC.Alloc(bytes/帧, Player 构建可用含 Release)；第六轮用 GetMonoUsedSizeLong 采不到分配率=接口错。必 Dispose。
        private long _lastGBytes;   // P2(TC-F2): GC.GetTotalMemory 帧差近似 GC.Alloc(ProfilerRecorder 在 ProfilerModule 默认未引 CS0246,改用 System.GC 可编译)

        [Header("S5 自动化自走(无头验证用，正式交付保持 false)")]
        [Tooltip("true=进 Play 自动开战并召唤士兵(免键盘)，用于无头最小闭环验证")]
        public bool autoTest = false;
        [Tooltip("自动召唤每阵营数量(10=最小闭环 / 50=满编)；autoTest=false 时忽略")]
        public int autoSummon = 50;   // 第五轮 P0-2：10→50（满编50v50，对齐 S2 TC-A2 soldiers=100）

        void Awake() { Instance = this; DontDestroyOnLoad(gameObject); }

        void Start()
        {
            // ---- Phase-B 自举（代码自举：BuildWin 每次重建场景 + 域重载丢绑定，全部运行时 Create/EnsureCreated）----
            // P0-3 基地：S5_Builder.BuildScene 用 天庭+40/玄朝-40 —— 与本轮对齐一致（对位 Web 玄朝-40左/天庭+40右，src3d RED_BASE_X=-40/BLUE_BASE_X=+40）。
            //          ★2026-09-04 修正：旧实现误判 Web 为"天庭左/玄朝右"，用 BaseModelFactory 默认(-40/+40)覆盖成反方向；现回归 天庭+40/玄朝-40，与召唤区/UI/Web 三方一致。
            CreateBasesPhaseB();
            // P0-1 双地图加载（竖向主图 + 横向 gBase 叠底 ZTest Always 置顶，对位 Web gBase）
            MapLoader.Create();
            // P0-6 建筑避让（6 栋占位建筑注册 Bounds，供 SoldierAI 躲避）
            BuildingManager.Create();
            // P0-5 战局四阶段 + 诗词弹幕（订阅 CombatSystem 击杀/基地事件）
            BattleStage.EnsureCreated();
            StageQuotes.EnsureCreated();
            // ★任务8 场面词：订阅基地受击(BaseSystem.onDamaged)供 baseLow/comeback/firstBaseHit 触发（基地已在 CreateBasesPhaseB 建好）
            StageQuotes.Instance.BindBases(blueBase, redBase);
            // P0-4 对位 Web 四按钮三面板 + 血条计数计时 + 阵营主题切换（UILoader 全权建，避免双 Canvas）
            UILoader.Create();
            // A1（P0-8 HUD补全）：暂停面板 + 设置面板 + 抗锯齿/Bloom 双toggle 持久化（对位 Web openPauseMenu/openSettings）
            SettingsPanel.Create();
            // B4（屏幕震动消费侧）：确保 BattleCamera 实例存在，其 Start 回填 Camera.main，Update 恒消费 UltController.GetShake()
            BattleCamera.EnsureCreated();

            // 订阅基地摧毁 → 胜负判定（对位 Web 基地摧毁 → gameOver）
            CombatSystem.OnBaseDestroyed += OnBaseDestroyed;
            // 工部二 S4 ◎1：击杀飘字 —— 订阅 OnSoldierKilled 显示"击杀!"(对位 Web kill 飘字)
            CombatSystem.OnSoldierKilled += OnSoldierKilledFx;
            // S5 R1 防御性双保险：即使场景 Inspector 绑定在域重载后丢失，运行时也自动回填基地引用
            // （工部二 S4 确认 GameManager 声明了 blueBase/redBase 但未显式转交 factory）
            if (factory != null) { factory.blueBase = blueBase; factory.redBase = redBase; }
            // P0-3 接线（对位 Web UIManager 召唤按钮）：为 _actions["summon"]/["summon-red"] 注册回调 → factory.Summon(team)。
            // 根因：全项目此前从未注册这两个 key（Grep 零命中），HUDController.Bind("summon",...) 的 _actions.TryGetValue 恒失败 → 点击无效果。
            // 天庭召唤=team 0、玄朝召唤=team 1，与 F1/F2/AutoRun 复用同一 factory.Summon(team) 入口；factory 为空时静默（与键盘召唤一致）。
            // 注意：必须在 UILoader.Create() 之后（UILoader.Instance/hud 才存在），且 factory.blueBase/redBase 注入之后（Summon 需取 enemyBase）。
            if (factory != null && UILoader.Instance != null && UILoader.Instance.hud != null)
            {
                UILoader.Instance.hud.OnAction("summon", () => factory.Summon(0));       // 天庭召唤(蓝)
                UILoader.Instance.hud.OnAction("summon-red", () => factory.Summon(1));   // 玄朝召唤(红)
                Debug.Log("[GameManager] 召唤按钮已接线: summon->Summon(0) / summon-red->Summon(1)");
            }
            // 契约2/Y1：战斗开始即挂 DeathFXManager，保证基地摧毁特效订阅早于爆裂（only 播一次，勿在 OnBaseDestroyed 直调 PlayBaseDestroyed）
            DeathFXManager.EnsureCreated();
            if (autoTest || HasArg("-autoTest")) { autoTest = true; StartCoroutine(AutoRun()); }
            else { SetState(GameState.Battle); }   // ★任务3：取消开始页+关卡选择页，生产态直入战斗（对位原版去掉 start/level-select 过渡；战斗初始化已在 Start 顶部完成，FrontendUI.EnterBattle 本身也调 SetState(Battle)）
        }

        // Phase-B P0-3：用 BaseModelFactory 生成白金宫殿(天庭蓝)/黑红堡垒(玄朝红)替换场景占位基地
        // ★任务1(2026-09-06)：按用户 Inspector 参数精确摆放双方基地（对位截图 Transform）。
        void CreateBasesPhaseB()
        {
            // 天庭3d模型：Position(38.1,0,71.4) Rotation(0,-178.387,0) Scale(40,40,40)
            Vector3 tinPos = new Vector3(38.1f, 0f, 71.4f);
            Quaternion tinRot = Quaternion.Euler(0f, -178.387f, 0f);
            Vector3 tinScale = new Vector3(40f, 40f, 40f);
            // 玄朝基地3d模型：Position(-28.69862,0.00066661,71.34374) Rotation(0.532,-182.32,-0.027) Scale(40,40,40)
            Vector3 xuaPos = new Vector3(-28.69862f, 0.00066661f, 71.34374f);
            Quaternion xuaRot = Quaternion.Euler(0.532f, -182.32f, -0.027f);
            Vector3 xuaScale = new Vector3(40f, 40f, 40f);
            if (blueBase != null) blueBase.gameObject.SetActive(false);   // 隐藏 S5 占位(防叠加重复命中)
            if (redBase != null) redBase.gameObject.SetActive(false);
            blueBase = BaseModelFactory.CreateBase(0, tinPos, tinRot, tinScale);
            redBase = BaseModelFactory.CreateBase(1, xuaPos, xuaRot, xuaScale);
            // ★用户拍板(2026-09-07)：大招也能打爆基地。旧结算只订阅 CombatSystem.OnBaseDestroyed（子弹命中路径），
            //   而 UltController 直接调 BaseSystem.ApplyHit → onDestroyed（绕过 CombatSystem 静态事件），故"大招打爆基地不结算"。
            //   此处把基地 onDestroyed 也桥接到 OnBaseDestroyed 结算入口（双通道都通到同一个结算），修复"基地归零却不结算/士兵卡住"。
            BindBaseDestroyed(blueBase);
            BindBaseDestroyed(redBase);
            // ★任务3：天庭基地旁加点光源(用户拍板:照亮天庭基地;物理不可见/无mesh不出现在相机视野——ortho俯视y=80,光本体无网格)。
            //   URP下运行时AddComponent<Light>即被渲染;point光放天庭基地上方y=18,range 30覆盖基地。
            var tfg = new GameObject("TiantingPointLight");
            tfg.transform.position = new Vector3(35f, 30f, 93f);   // ★任务1修正：点光位置按用户 Inspector 参数 (35,30,93)
            var tl = tfg.AddComponent<Light>();
            tl.type = LightType.Point;
            tl.color = new Color(1f, 0.95f, 0.8f);       // 暖金,呼应天庭白金
            tl.range = 30f;
            tl.intensity = 240f;                          // URP点光(范围m级) 240 适中照亮基地,非过曝
            tl.shadows = LightShadows.None;               // 无阴影,免额外开销
            Debug.Log($"[LightProbe] 天庭点光 created @{tfg.transform.position} range={tl.range} intensity={tl.intensity} color={tl.color}");
            // ★任务4：删除地图上两个 zone-ring(召唤区地面光环)——呼吸光环改挂士兵脚下(SoldierHalo)，不再保留地图级光环。
            //   CreateBasesPhaseB 不再调用 ZoneRing.Create(0/1)；召唤区 center 仍由 SoldierFactory 出怪用(不受影响)。
            Debug.Log($"[GameManager] Phase-B 基地替换完成 天庭@{tinPos} 玄朝@{xuaPos} (用户 Inspector 参数)+士兵呼吸光环挂载，地图 zone-ring 已删");
            // ★任务5：复刻原版45°左上→右下飘落火花氛围(新AmbientFx.cs自举生成,对位 src3d/main.js L1683-1762)
            AmbientFx.EnsureCreated();
        }

        // 自动自走：进 Play 后免键盘召唤并开战（S5 无头闭环验证）
        IEnumerator AutoRun()
        {
            yield return null; // 等一帧确保 factory/基地/动画器就绪
            SetState(GameState.Battle);
            // P0-5（完整版视觉对比强制前置，S2 TC-B1/B2/B6/P0-5）：autoTest 也要截主画面全景，
            //   否则"产品同状态全景截图"硬判据无法产出。士兵走动数秒聚齐、非大招白光掩盖（lesson09#88 帧选峰值教训，取稳定站位帧）。
            CaptureShot("u3d_start", Color.white);   // 战斗开战全景（对齐 golden_start/afterStart 同状态）
            // P6(F5) 接线：战斗开始 -> BGM 占位
            if (AudioController.Instance != null) AudioController.Instance.PlayBattleBgm();
            if (factory != null)
            {
                for (int i = 0; i < autoSummon; i++) { factory.Summon(0); factory.Summon(1); }
            }
            // 满编稳定站位全景（对齐 golden_full50v50；召唤后等 5s 士兵走位聚齐，避开 AutoUlt 大招白光遮盖）
            yield return new WaitForSeconds(5f);
            CaptureShot("u3d_panorama", Color.white);
            Debug.Log($"[GameManager] 自动自走战斗已启动 summon={autoSummon}×2阵营");
            // 战斗进行中全景（对齐 golden_battle_entry；再等 2s 已进入交战）
            yield return new WaitForSeconds(2f);
            CaptureShot("u3d_battle", Color.white);
            // s5 v3.2（工部二 §13.2-①）：autoTest 自动放大招，让大招视觉在 Player 运行时可见（否则视觉从未被触发）
            if (autoTest) StartCoroutine(AutoUlt());
            if (autoTest) StartCoroutine(ReportRuntime()); // S5 诊断：周期性上报运行时状态
            if (autoTest) StartCoroutine(ReportSoldierMaterials()); // ★任务6 诊断：士兵运行时材质/颜色实证(autoTest-only)
            if (autoTest) StartCoroutine(UltRepeatProbe());         // ★任务7 大招无限触发验证：圣裁/同死冷却结束可重复触发且每次生效+冷却约束(autoTest-only)
            if (autoTest) StartCoroutine(StageQuotesProbe());       // ★任务8 场面词验证：7分类文案逐字+三句式+分类冷却+reset(autoTest-only)
            if (autoTest) StartCoroutine(BattlefixProbe());         // ★战场4问题 T1朝向/T2闪白/T3光环/T4基地 数值实证(autoTest-only)
        }

        // autoTest 自动放大招：开局后交替触发圣裁(t=0)/同死，让金色光柱/红激光雨在 Player 运行时可见
        IEnumerator AutoUlt()
        {
            yield return new WaitForSeconds(1f);
            if (UltController.Instance != null) UltController.Instance.TriggerUltimate(0);   // 圣裁(天庭)
            // s5T1: 半球 ease-in(p2) 慢启, 0.8s 仅扩~4.5; 改多帧拍半球膨胀+波纹(golden 对比)
            yield return new WaitForSeconds(2.0f);
            CaptureShot("holy_fx_1", new Color(1f, 0.84f, 0f));
            yield return new WaitForSeconds(1.0f);
            CaptureShot("holy_fx_2", new Color(1f, 0.84f, 0f));
            yield return new WaitForSeconds(1.5f);
            CaptureShot("holy_fx_3", new Color(1f, 0.84f, 0f));
            yield return new WaitForSeconds(2.5f);
            if (UltController.Instance != null) UltController.Instance.TriggerUltimate(1);   // 同死(玄朝)
            // 同死雨幕成形约1.5s 取峰值帧
            yield return new WaitForSeconds(1.5f);
            CaptureShot("same_fx", new Color(1f, 0.27f, 0.27f));
            Debug.Log("[GameManager] autoTest 已自动放大招 圣裁+同死（视觉层应可见）");
        }

        // 工部二 §14.2-①：Player 运行时截图——ScreenCapture.CaptureScreenshot 写入 persistentDataPath，
        // 附 [Capture] 路径日志供 Player.log 回溯取图；expected 供 B 轴取色比对（圣裁金#FFD700/同死红#FF4444）
        void CaptureShot(string name, Color expected)
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[Capture] 已存图: {path} expectedHex=#{ColorUtility.ToHtmlStringRGB(expected)}");
        }

        // S5 诊断协程：每 3s 上报士兵/基地/胜负，供 console 采集（验证战斗演进+静止判据+胜负闭环）
        IEnumerator ReportRuntime()
        {
            float t = 0f; Vector3 last0 = Vector3.zero; bool haveLast = false;
            var dcRec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Draw Calls Count");   // B2/D1 运行时 drawcalls 采集(UnityStats CS0234 不可用,改 ProfilerRecorder,见 lesson09#84)
            while (true)
            {
                yield return new WaitForSeconds(3f);
                t += 3f;
                int cnt = factory != null && SoldierFactory.ActiveSoldiers != null ? SoldierFactory.ActiveSoldiers.Count : -1;
                string bh = blueBase != null ? blueBase.hp.ToString("F0") : "null";
                string rh = redBase != null ? redBase.hp.ToString("F0") : "null";
                string s1 = "无士兵";
                if (factory != null && SoldierFactory.ActiveSoldiers != null && SoldierFactory.ActiveSoldiers.Count > 0)
                {
                    var s = SoldierFactory.ActiveSoldiers[0];
                    s1 = $"enemyBase={(s.enemyBase != null ? "OK" : "NULL")} team={s.team} pos={s.transform.position}";
                    if (haveLast) s1 += $" 移{(s.transform.position - last0).magnitude:F2}";
                    last0 = s.transform.position; haveLast = true;
                }
                // S5 R4: Player 内采 Mono 上限(亿字节), 冒烟从 Player.log 读 [S5Mono]
                Debug.Log($"[S5Mono] t={t:F0}s monoMb={(Profiler.GetMonoUsedSizeLong()/1048576f):F1} monoHeapMb={(Profiler.GetMonoHeapSizeLong()/1048576f):F1}");
                // P2 第七轮(TC-F2): GC.GetTotalMemory 帧差近似分配净增(ProfilerRecorder 在 ProfilerModule 默认未引 CS0246,改 System.GC 可编译); FPS/DrawCall unscaledDeltaTime/UnityStats
                long gb = System.GC.GetTotalMemory(false);
                Debug.Log($"[S5GC] managedHeapMB={(gb / 1048576f):F1} heapDeltaKB={(_lastGBytes > 0 ? (gb - _lastGBytes) / 1024f : 0f):F1}");
                _lastGBytes = gb;
                int dcDraw = dcRec.Valid ? (int)dcRec.LastValue : -1;   // drawcalls=最近帧值, -1=recorder无效
                Debug.Log($"[S5Perf] fps={(Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f):F0}  dcDraw={dcDraw}");
                Debug.Log($"[Probe] t={t:F0}s soldiers={cnt} winTeam={winTeam} state={State} 基地HP(天庭={bh}/玄朝={rh}) s1[{s1}]");
            }
        }

        // ★任务6 诊断实证（autoTest-only，生产态不激活）：遍历士兵输出运行时 Renderer.sharedMaterial.name + _BaseColor + _BaseMap 是否 null，
        //   分阵营统计"无贴图素模/红base"数量，并抓天庭/玄朝各 1 名存活士兵近景特写，供判别"天庭金误红"vs"玄朝红预期"（S4 任务6 三路排查 C）。
        IEnumerator ReportSoldierMaterials()
        {
            yield return new WaitForSeconds(1.5f);
            var soldiers = SoldierFactory.ActiveSoldiers;
            if (soldiers == null || soldiers.Count == 0) { Debug.Log("[T6Probe] 无士兵,探针跳过"); yield break; }
            int tianTotal = 0, tianSusp = 0, xuanTotal = 0, xuanSusp = 0, printed = 0;
            Soldier tianPick = null, xuanPick = null;
            foreach (var s in soldiers)
            {
                if (s == null || !s.tag.isAlive) continue;
                var rnds = s.GetComponentsInChildren<Renderer>(true);
                string mats = ""; bool hasMesh = false; bool noTex = false, baseRed = false;
                foreach (var r in rnds)
                {
                    if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                    hasMesh = true;
                    var m = r.sharedMaterial;
                    if (m == null) { mats += "[NULL]"; continue; }
                    bool hasBase = m.HasProperty("_BaseMap");
                    bool texNull = hasBase && m.GetTexture("_BaseMap") == null;
                    Color c = Color.white;
                    if (m.HasProperty("_BaseColor")) c = m.GetColor("_BaseColor");
                    if (texNull) noTex = true;
                    if (c.r > 0.55f && c.g < 0.4f && c.b < 0.4f) baseRed = true;
                    mats += $"[{m.name}|{(hasBase ? (texNull ? "NO_TEX" : "TEX") : "NOPROP")}|base={ColorUtility.ToHtmlStringRGB(c)}]";
                }
                if (!hasMesh) continue;
                if (s.team == 0) { tianTotal++; if (noTex || baseRed) tianSusp++; if (tianPick == null) tianPick = s; }
                else { xuanTotal++; if (noTex || baseRed) xuanSusp++; if (xuanPick == null) xuanPick = s; }
                if (printed < 6) { Debug.Log($"[T6Probe] team={s.team} id={s.name} mats={mats}"); printed++; }
            }
            Debug.Log($"[T6Probe] 统计 天庭(t=0)总={tianTotal} 无贴图素模/红base={tianSusp} | 玄朝(t=1)总={xuanTotal} 无贴图素模/红base={xuanSusp}");
            if (tianPick != null) YieldCloseup(tianPick, "soldier_tian_closeup");
            yield return new WaitForSeconds(0.5f); // 分帧：同帧两次 CaptureScreenshot 只写最后一张(实测 tian 被 xuan 覆盖),须隔帧独立写盘
            if (xuanPick != null) YieldCloseup(xuanPick, "soldier_xuan_closeup");
        }

        // ★战场4问题 S5_exec 探针（autoTest-only，生产态不激活）：T1朝向/T2受击闪白/T3光环销毁/T4基地刷新 数值实证。
        //   无损执行 T1/T2 → 直接调生产 RestartBattle() 遍历 T3(ClearAll销毁残留光环)+T4(CreateBasesPhaseB重建+factory刷新) 修复路径 → 重启后校验。
        //   全部 null 守卫：探针任何异常不得让生产 exe 崩溃(V5)。
        IEnumerator BattlefixProbe()
        {
            yield return new WaitForSeconds(8f);   // 等士兵攻击/移动稳定、AutoUlt 大招示意窗过后
            var sk = SoldierFactory.ActiveSoldiers;

            // === T1 朝向：运动方向对齐(移动时面部=移动方向,T1-2) + 最近敌兵夹角(攻击朝向代理,T1-1/1-4) ===
            if (sk != null && sk.Count > 1)
            {
                Soldier s = sk[0]; Soldier t1Pick = null; int okMove = 0, okEnemy = 0, n = 0;
                if (s != null && s.tag.isAlive)
                {
                    // T1-2 移动朝向：采样0.2s位移方向 vs 面部yaw
                    Vector3 p0 = s.transform.position;
                    yield return new WaitForSeconds(0.2f);
                    Vector3 move = s.transform.position - p0; move.y = 0f;
                    Vector3 fwd = s.transform.rotation * Vector3.right; fwd.y = 0f;   // ★T1诊断：正脸=+X(aimOffset=-90),测局部+X；旧测transform.forward(=+Z)是循环论证恒通过
                    float facYaw = fwd.sqrMagnitude > 1e-6f ? Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg : 0f;
                    float moveYaw = move.sqrMagnitude > 1e-5f ? Mathf.Atan2(move.x, move.z) * Mathf.Rad2Deg : facYaw;
                    float dMove = Mathf.Abs(Mathf.DeltaAngle(facYaw, moveYaw));
                    okMove = dMove <= 30f ? 1 : 0;
                    Debug.Log($"[BF-T1] 移动方向对齐: 面yaw={facYaw:F1} 移yaw={moveYaw:F1} |Δ|={dMove:F1}° 朝向移动方向={okMove==1} (期望|Δ|小)");
                }
                foreach (var x in sk)
                {
                    if (x == null || !x.tag.isAlive || n >= 6) continue;
                    Soldier ne = null; float nd = float.MaxValue;
                    foreach (var e in sk)
                        if (e != null && e != x && e.tag.isAlive && e.team != x.team)
                        {
                            float d2 = (e.transform.position - x.transform.position).sqrMagnitude;
                            if (d2 < nd) { nd = d2; ne = e; }
                        }
                    Vector3 fx = x.transform.rotation * Vector3.right; fx.y = 0f;   // ★T1诊断：正脸=+X
                    float fy = fx.sqrMagnitude > 1e-6f ? Mathf.Atan2(fx.x, fx.z) * Mathf.Rad2Deg : 0f;
                    float ty = fy;
                    if (ne != null) { Vector3 toE = ne.transform.position - x.transform.position; toE.y = 0f; ty = toE.sqrMagnitude > 1e-6f ? Mathf.Atan2(toE.x, toE.z) * Mathf.Rad2Deg : 0f; }
                    float d = Mathf.Abs(Mathf.DeltaAngle(fy, ty));
                    n++; if (d <= 25f) okEnemy++; if (t1Pick == null) t1Pick = x;
                }
                float pct = n > 0 ? 100f * okEnemy / n : 0f;
                Debug.Log($"[BF-T1] 最近敌兵夹角(攻击朝向代理,测正脸+X): 抽样={n} 正脸朝敌兵(≤25°)={okEnemy} 占比={pct:F0}% (aimOffset=-90,正脸=+X对敌)");
                if (t1Pick != null) { YieldCloseup(t1Pick, "bf_t1_closeup"); yield return new WaitForSeconds(0.4f); }
            }
            else Debug.Log("[BF-T1] 士兵不足,朝向探针跳过");

            // === T2 受击闪白：受击前→瞬(期望白 R/G/B>1.5)→+0.2s复位（S2 T2-2/T2-3/T2-4）===
            if (sk != null && sk.Count > 0)
            {
                var s2 = sk[0];
                if (s2 != null && s2.tag.isAlive)
                {
                    var r = s2.transform.GetComponentInChildren<Renderer>();
                    if (r != null && r.material.HasProperty("_BaseColor"))
                    {
                        Color before = r.material.GetColor("_BaseColor");
                        Debug.Log($"[BF-T2] 受击前 base=#{ColorUtility.ToHtmlStringRGB(before)} ({before.r:F2},{before.g:F2},{before.b:F2})");
                        s2.TakeDamage(1f, Vector3.forward);
                        yield return new WaitForSeconds(0.05f);
                        Color flash = r.material.GetColor("_BaseColor");
                        bool isWhite = flash.r > 1.5f && flash.g > 1.5f && flash.b > 1.5f;
                        Debug.Log($"[BF-T2] 受击瞬 base=#{ColorUtility.ToHtmlStringRGB(flash)} ({flash.r:F2},{flash.g:F2},{flash.b:F2}) 白闪={isWhite} (期望白(2,2,2), R/G/B>1.5)");
                        CaptureShot("bf_t2_hit_flash", new Color(1f, 1f, 1f));
                        yield return new WaitForSeconds(0.2f);
                        Color after = r.material.GetColor("_BaseColor");
                        bool restored = Mathf.Approximately(after.r, before.r) && Mathf.Approximately(after.g, before.g) && Mathf.Approximately(after.b, before.b);
                        Debug.Log($"[BF-T2] 受击后0.2s base=#{ColorUtility.ToHtmlStringRGB(after)} 复位={restored} (==受击前,无闪白/闪红残留)");
                    }
                }
            }
            else Debug.Log("[BF-T2] 无士兵,闪白探针跳过");

            // === 生产 RestartBattle()：遍历 T3(ClearAll→销毁残留光环)+T4(CreateBasesPhaseB重建+factory刷新) 修复路径 ===
            Debug.Log("[BF-T3/4] 触发生产 RestartBattle() (T3-ClearAll销毁光环+T4-建基+factory刷新) ...");
            RestartBattle();
            yield return new WaitForSeconds(1.2f);   // 等 Destroy 一帧生效 + 新基地实例化

            // === T3 光环销毁：ClearAll 后工厂下残留 SoldierHalo 数应=0（无上一局残留）===
            int halos = factory != null ? factory.GetComponentsInChildren<SoldierHalo>(true).Length : -1;
            Debug.Log($"[BF-T3] RestartBattle后 工厂下SoldierHalo={halos} (期望0, 光环不累积)");

            // === T4 基地刷新：factory.blueBase/redBase 指向新实例 + IsDestroyed=false + 敌兵可搜索 ===
            bool bRef = factory != null && factory.blueBase != null && factory.redBase != null && factory.blueBase == blueBase && factory.redBase == redBase;
            bool bAlive = blueBase != null && redBase != null && !blueBase.IsDestroyed && !redBase.IsDestroyed;
            Debug.Log($"[BF-T4] factory.blueBase/redBase==gameManager新基座?{bRef} 新基地IsDestroyed=false?{bAlive} (T4-1重建OK)");
            if (factory != null)
            {
                var s3 = factory.Summon(0);   // 召唤1名天庭兵验证其 enemyBase(敌方=玄朝基地) 指向新实例
                if (s3 != null)
                    Debug.Log($"[BF-T4] 重开召唤天庭兵 enemyBase==(新玄朝基)?{s3.enemyBase == redBase} enemyBaseIsAlive={s3.enemyBase != null && !s3.enemyBase.IsDestroyed} (T4-2可搜索新建基地前置OK)");
                CaptureShot("bf_t4_after_restart", Color.white);
            }
            Debug.Log("[BF-Probe] 完 (T1朝向/T2闪白/T3光环/T4基地 数值实证已输出)");
        }

        // ★任务7 大招"无限触发"验证（autoTest-only，生产态不激活）：用户报圣裁/同死只能触发一次，
        //   实测证明"冷却结束可重复触发、每次生效 + 冷却约束仍生效"。连触3次取证（基地 maxHp=10000，探针总伤害<3000，
        //   不会打爆基地触发结算 timeScale=0 卡死探针）。判据 T7-1 圣裁≥3次每次 applyDamage/T7-2 同死≥3次每次 tick/T7-3 冷却拦截。
        IEnumerator UltRepeatProbe()
        {
            yield return new WaitForSeconds(2.0f);   // 避开 AutoUlt 同帧竞争，等实例/FX 就绪
            var ult = UltController.Instance;
            if (ult == null) { Debug.Log("[T7Probe] UltController.Instance==null,跳过"); yield break; }

            // T7-1 圣裁：清冷却(ResetCooldown=模拟冷却已结束)后可重复触发≥3次，且每次 ApplyHolyDamage 生效
            ult.cooldown = 0.5f;   // 缩短冷却模拟"冷却很快结束"，便于快速连触取证
            for (int i = 1; i <= 3; i++)
            {
                ult.ResetCooldown(0);        // 清掉 AutoUlt 落的 30s 冷却 = 立即 ready
                ult.TriggerUltimate(0);      // 圣裁
                yield return new WaitForSeconds(3.3f);   // 等 holyAttackAt=3.0 执行 ApplyHolyDamage(打红基地-500)
                Debug.Log($"[T7Probe] 圣裁第{i}次触发 期望[Ult] 圣裁 applyDamage 已出现(1行/次)");
            }
            // T7-2 同死：同理连触≥3次，每次已见 tick 给士兵伤害生效
            ult.cooldown = 0.5f;
            for (int i = 1; i <= 3; i++)
            {
                ult.ResetCooldown(1);
                ult.TriggerUltimate(1);      // 同死
                yield return new WaitForSeconds(0.4f);   // 等第一波 tick(每 0.25s)打士兵
                Debug.Log($"[T7Probe] 同死第{i}次触发 期望[Ult] 同死 tick 已出现");
            }
            // T7-3 冷却约束仍生效（"无限触发"≠无视冷却）：触发后立即再触发应被 IsReady 拦
            ult.cooldown = 30f;              // 恢复生产冷却
            ult.ResetCooldown(0);            // 先清冷却让第1次必触发
            ult.TriggerUltimate(0);
            yield return new WaitForSeconds(0.2f);
            ult.TriggerUltimate(0);          // 立即再触发→应被拦(remain~30s)
            Debug.Log("[T7Probe] 冷却约束测试 期望[Ult] team=0 冷却中 remain 1条");
            Debug.Log("[T7Probe] 完 (判据: 圣裁/同死各≥3条触发+每次applyDamage/tick生效+冷却拦截)");
        }

        // ★任务8 场面词"源码级复刻"验证（autoTest-only，生产态不激活）：对照 S4 判据 T8-1文案逐字 / T8-2三句式 / T8-3分类冷却 / T8-5重开reset。
        //   清各分类冷却后依次触发 7 分类 → Player.log 证 7 分类文案逐字 + opening/frenzy 三句式逐句 + 同分类立即重触发被冷却拦 + reset 后重触发通过。
        IEnumerator StageQuotesProbe()
        {
            yield return new WaitForSeconds(8.0f);   // 避开局 autoTest 5s 自动 opening + 战斗进行，保证探针触发的分类有时间输出
            var sq = StageQuotes.Instance;
            if (sq == null) { Debug.Log("[T8Probe] StageQuotes.Instance==null,跳过"); yield break; }
            // T8-1/T8-2：清各分类冷却保证首次必触发，依次输出 7 分类文案（multi=三句式单句式对齐原版 STAGE_QUOTES）
            foreach (var c in new[] { StageQuotes.Category.Opening, StageQuotes.Category.Wipe, StageQuotes.Category.Frenzy,
                                      StageQuotes.Category.Comeback, StageQuotes.Category.BaseLow, StageQuotes.Category.FirstBaseHit,
                                      StageQuotes.Category.Victory })
                sq.ResetCooldown(c);
            sq.PublicFire(StageQuotes.Category.Opening);       // multi 三句式
            sq.PublicFire(StageQuotes.Category.Wipe);          // single
            sq.PublicFire(StageQuotes.Category.Frenzy);        // multi 三句式
            sq.PublicFire(StageQuotes.Category.Comeback);      // single
            sq.PublicFire(StageQuotes.Category.BaseLow);       // single
            sq.PublicFire(StageQuotes.Category.FirstBaseHit);  // single
            sq.PublicFire(StageQuotes.Category.Victory);       // single
            // T8-3 分类冷却（对位 stageQuotes.js canFire）：刚 fire 过 Frenzy(记下时间戳)，立即再触发同分类应被 [冷却] 拦
            Debug.Log("[T8Probe] 分类冷却测试: 立即再触发 Frenzy 期望[冷却]");
            sq.PublicFire(StageQuotes.Category.Frenzy);
            // T8-5 重开 reset（对位 stageQuotes.js QuoteCooldown.reset）：清 opening 冷却后重触发应通过（三句式再现）
            Debug.Log("[T8Probe] reset测试: ResetCooldown(Opening) 后重触发 Opening 期望三句式再现");
            sq.ResetCooldown(StageQuotes.Category.Opening);
            sq.PublicFire(StageQuotes.Category.Opening);
            yield return new WaitForSeconds(5.0f);   // 等 multi 三句式协程逐句播完取证（S4 判据要求"按序显现+字体递进变大"）
            Debug.Log("[T8Probe] 完 (判据: 7分类文案逐字/三句式按序/分类冷却/reset)");
        }

        // 近景相机抓士兵特写（判别红/金用）：临时相机置顶，暂关主相机，拍完恢复销毁。
        void YieldCloseup(Soldier s, string shotName)
        {
            if (s == null) return;
            var main = Camera.main;
            var camGo = new GameObject("T6ProbeCam");
            camGo.transform.position = s.transform.position + new Vector3(6f, 4f, 4.5f);
            var cam = camGo.AddComponent<Camera>();
            cam.depth = 100; cam.fieldOfView = 40f;
            camGo.transform.LookAt(s.transform.position + new Vector3(0f, 1.7f, 0f));
            if (main != null) main.enabled = false;
            CaptureShot(shotName, Color.white);
            StartCoroutine(RestoreMainCam(main, camGo));
        }

        IEnumerator RestoreMainCam(Camera main, GameObject camGo)
        {
            yield return null;
            if (main != null) main.enabled = true;
            Destroy(camGo);
        }

        void OnDestroy()
        {
            CombatSystem.OnBaseDestroyed -= OnBaseDestroyed;
            CombatSystem.OnSoldierKilled -= OnSoldierKilledFx;
        }

        public void SetState(GameState s)
        {
            State = s;
            Debug.Log($"[GameManager] 状态切换 -> {s}");
        }

        // S5 验证入口：命令行 `-autoTest` 触发自动自走（交付态无此参=false，正常游戏需玩家操作；测试态加参即自动开战）
        static bool HasArg(string name)
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a == name) return true;
            return false;
        }

        // ★用户要求(2026-09-06)：删除击杀飘字。原 FloatingText.Show("击杀!",...,80) 已移除；击杀仅由 BattleStage.GetKills 计分，
        //   不再弹视觉飘字(避免 WorldSpace 近大远小失真)。参数签名保留以匹配 CombatSystem.OnSoldierKilled 委托。
        private void OnSoldierKilledFx(Soldier s)
        {
        }

        // 基地摧毁回调：敌方基地被摧毁即胜负分出（T2 结算+爆炸+重载）
        private void OnBaseDestroyed(int destroyedTeam)
        {
            if (State == GameState.GameOver) return;   // ★防重复结算：子弹命中(CombatSystem.OnBaseDestroyed) 与 大招(BaseSystem.onDestroyed) 双通道可能先后触发同一结算
            // destroyedTeam 是被摧毁的阵营，胜者是其对立阵营
            winTeam = destroyedTeam == 0 ? 1 : 0;
            SetState(GameState.GameOver);
            // P6(F5) 接线：基地摧毁 -> 爆炸音；GameOver -> 停 BGM+SFX
            var destroyedBase = destroyedTeam == 0 ? blueBase : redBase;
            if (AudioController.Instance != null)
            {
                AudioController.Instance.PlayExplosionSfx(destroyedBase != null ? destroyedBase.transform.position : Vector3.zero);
                AudioController.Instance.StopAll();
            }
            // T2：结算序列——爆炸/闪光用真实时间播主体(此刻 timeScale=1) -> 爆炸后隐藏败方基地 -> 弹结算面板置 timeScale=0
            StartCoroutine(GameOverSequence(destroyedBase, winTeam));
            Debug.Log($"[GameManager] 胜负已判 -> {(winTeam == 0 ? "天庭(蓝)" : "玄朝(红)")}胜");
        }

        // ★用户拍板(2026-09-07)：把基地 onDestroyed 桥接到结算入口（大招打爆基地也能结算）。
        //   BaseSystem.onDestroyed 是 UnityEvent 字段且未初始化 = new（BaseModelFactory 运行时 AddComponent 动态创建可为 null，见 Lesson #105），
        //   故先确保非空再 AddListener；b.Team 在 AddListener 时捕获（闭包），防重开基地复用旧回调。
        void BindBaseDestroyed(BaseSystem b)
        {
            if (b == null) return;
            if (b.onDestroyed == null) b.onDestroyed = new UnityEngine.Events.UnityEvent();
            int t = b.Team;
            b.onDestroyed.AddListener(() => OnBaseDestroyed(t));
            Debug.Log($"[GameManager] BindBaseDestroyed team={t}（基地 onDestroyed→结算 已桥接）");
        }

        // T2 结算序列：先播爆炸主体(真实时间) -> 隐藏败方基地 -> 弹结算面板置 timeScale=0（T2-2 爆炸+隐藏 / T2-4 结算暂停）
        IEnumerator GameOverSequence(BaseSystem destroyedBase, int winner)
        {
            yield return new WaitForSeconds(0.6f);
            if (destroyedBase != null) destroyedBase.gameObject.SetActive(false);   // T2-2 爆炸后隐藏败方基地
            Liuhuo.UI.SettlementManager.EnsureCreated();
            if (Liuhuo.UI.SettlementManager.Instance != null)
            {
                Liuhuo.UI.SettlementManager.Instance.Show(winner);                 // T2-1 结算画面(T2-4 内部置 timeScale=0)
                // autoTest 验证（生产态 autoTest=false 不激活，不污染交付）：
                //   T2-1 结算画面视觉 -> capture；T2-3 重载 -> 2s 后模仿"点击重新开始"调 RestartBattle（无头点不了按钮）
                if (autoTest) CaptureShot("settlement_ui", Color.white);
                if (autoTest)
                {
                    yield return new WaitForSecondsRealtime(2f);   // timeScale=0，须 Realtime 才继续
                    Debug.Log("[GameManager] autoTest 模拟点击重新开始 -> RestartBattle");
                    RestartBattle();
                }
            }
        }

        // T2-3 重载：结算面板"点击重新开始"回调走此入口（对位 Web main.js _resetBattle）
        public void RestartBattle()
        {
            if (factory != null) factory.ClearAll();          // 士兵/弹体全清
            DeathFXManager.Rescan();                          // 重开后重订阅新基地 onDestroyed（防重开后基地摧毁不播爆炸）
            if (BattleStage.Instance != null) BattleStage.Instance.Reset();   // 战局击杀/计时/阶段归零
            CreateBasesPhaseB();                              // 基地从 Resources 全量重建（非prefab）
            // ★T4(S4)：基地搜不到根治——SoldierFactory.blueBase/redBase 仍指旧(已摧毁)基地；
            //   新召唤士兵 Init 注入的 enemyBase=旧基地 → 敌兵搜不到重建基地。重开后刷新 factory 基地引用到新实例。
            if (factory != null) { factory.blueBase = blueBase; factory.redBase = redBase; }
            // ★任务8 场面词：基地重建为新实例，重绑 onDamaged 供 baseLow/comeback/firstBaseHit 重开后仍触发
            if (StageQuotes.Instance != null) StageQuotes.Instance.BindBases(blueBase, redBase);
            winTeam = -1;                                     // 胜负复位
            Time.timeScale = 1f;                              // T2-4 恢复（OnRestart 已置1，双保险）
            SetState(GameState.Battle);
            Debug.Log("[GameManager] RestartBattle 已完成 (基地重建+清兵清弹+战局重置, timeScale=1)");
        }

        // S5 调试入口：按 F1 各召唤 10 士兵（最小闭环测试），按 F2 冲满 50v50
        void Update()
        {
            // A1（P0-8 HUD补全）：ESC 切换暂停/继续（对位 Web openPauseMenu，Time.timeScale 归零/恢复）
            if (Input.GetKeyDown(KeyCode.Escape))
                Liuhuo.UI.SettingsPanel.SetPaused(!Liuhuo.UI.SettingsPanel.IsPaused);

            // P1-6 缺口3：HUD 数据流动。每帧把真实战局数据喂给 HUD（击杀/双方存活/基地血量/倒计时）。
            // 放在战斗守卫之前，使非战斗态也显示基础值（基地满血、倒计时满、击杀/存活 0）。
            RefreshHud();

            if (State != GameState.Battle || factory == null) return;
            if (Input.GetKeyDown(KeyCode.F1))
            {
                for (int i = 0; i < 10; i++) { factory.Summon(0); factory.Summon(1); }
                Debug.Log("[GameManager] F1 召唤 各10 士兵 (最小闭环)");
            }
            else if (Input.GetKeyDown(KeyCode.F2))
            {
                for (int i = 0; i < 50; i++) { factory.Summon(0); factory.Summon(1); }
                Debug.Log("[GameManager] F2 召唤 各50 士兵 (满编 50v50)");
            }
            else if (Input.GetKeyDown(KeyCode.F3))
            {
                SetState(GameState.Battle);
                Debug.Log("[GameManager] F3 进入战斗 (Battle 状态)");
            }
        }

        // P1-6 缺口3：HUD 数据流动。把真实战局数据喂给 HUD（击杀/双方存活/基地血量/倒计时）。
        // 数据源：BattleStage(击杀按得分方/计时/基地摧毁) + SoldierFactory(存活) + 本类 blueBase/redBase(基地血量)。
        // UILoader 全权代理转发到 HUDController（UILoader.Instance.UpdateHud / SetTimer），自每帧 RefreshHud 调用。
        private void RefreshHud()
        {
            var ui = Liuhuo.UI.UILoader.Instance;
            if (ui == null) return;

            var stage = BattleStage.Instance;
            // 击杀：BattleStage 按得分方记（0=天庭/1=玄朝）
            int tKill = stage != null ? stage.GetKills(0) : 0;
            int xKill = stage != null ? stage.GetKills(1) : 0;
            // 存活：按阵营过滤 ActiveSoldiers
            int tAlive = CountAlive(0);
            int xAlive = CountAlive(1);
            // 基地血量：pct 驱动 Slider，绝对血量驱动 HP 数字 Text（缺口2）
            float tHpPct = blueBase != null ? blueBase.GetHpNormalized() : 0f;
            float xHpPct = redBase != null ? redBase.GetHpNormalized() : 0f;
            float tHp = blueBase != null ? blueBase.hp : 0f;
            float xHp = redBase != null ? redBase.hp : 0f;

            ui.UpdateHud(tHpPct, xHpPct, tHp, xHp, tAlive, xAlive, tKill, xKill);

            // 倒计时：battleDurationRef - BattleElapsed（对位 HUD 顶部 02:00 计时，开局满 2:00 递减到 00:00）
            float dur = stage != null ? stage.battleDurationRef : 120f;
            float remaining = Mathf.Max(0f, dur - (stage != null ? stage.BattleElapsed : 0f));
            int totalSec = (int)remaining;
            ui.SetTimer($"{totalSec / 60:00}:{totalSec % 60:00}");
        }

        // P1-6 缺口3：按阵营统计存活士兵数。ActiveSoldiers 含死亡动画未回池者，须判 tag.isAlive（复刻 BattleStage.CountAliveSoldiers）。
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
    }

    // 存读档（对位 electron-adapter.js，Unity 侧 PlayerPrefs 等价物）
    public static class SaveSystem
    {
        public static void SaveBattle(string jsonState) { PlayerPrefs.SetString("battle_state", jsonState); PlayerPrefs.Save(); }
        public static string LoadBattle() { return PlayerPrefs.GetString("battle_state", ""); }
        public static void ClearBattle() { PlayerPrefs.DeleteKey("battle_state"); }
        public static float GetVolume(string k, float def) { return PlayerPrefs.GetFloat(k, def); }
        public static void SetVolume(string k, float v) { PlayerPrefs.SetFloat(k, v); PlayerPrefs.Save(); }
        public static int GetBestKills() { return PlayerPrefs.GetInt("best_kills", 0); }
        public static void SetBestKills(int v) { if (v > GetBestKills()) { PlayerPrefs.SetInt("best_kills", v); PlayerPrefs.Save(); } }
    }

    // 通用对象池（士兵/子弹/特效复用，防 GC 抖动）
    public class ObjectPool<T> where T : Component
    {
        private readonly Stack<T> _pool = new Stack<T>();
        private readonly T _prefab;
        private readonly Transform _parent;

        public ObjectPool(T prefab, int prewarm, Transform parent)
        {
            _prefab = prefab; _parent = parent;
            for (int i = 0; i < prewarm; i++)
            {
                var o = Object.Instantiate(prefab, parent);
                o.gameObject.SetActive(false);
                _pool.Push(o);
            }
        }
        public T Get(Vector3 pos, Quaternion rot)
        {
            T o = _pool.Count > 0 ? _pool.Pop() : Object.Instantiate(_prefab, _parent);
            o.transform.SetPositionAndRotation(pos, rot);
            o.gameObject.SetActive(true);
            return o;
        }
        public void Release(T o) { o.gameObject.SetActive(false); _pool.Push(o); }
    }
}

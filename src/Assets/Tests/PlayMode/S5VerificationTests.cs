// S5VerificationTests.cs —— S5 执行闭环自测（PlayMode）
// 加载真实 Phase1（autoTest=true 自动召唤 50v50）验证完整战斗闭环 + 采集性能。
// 项目脚本在默认 Assembly-CSharp（无 asmdef）→ 测试程序集无法编译期引用，用运行时反射访问游戏类型。
// 性能口径（守 G7 红线）：R3 优先级② FrameTimingManager 采真实 CPU 帧时；nographics 采不到则如实标注来源（逻辑帧率=墙钟推得，非 Profiler 冒充）。
// R4 修正：ProfilerRecorder 在本项目测试程序集编译不过（CS0246 ProfilingModule 不可达）→ 改 FrameTimingManager + System.GC。
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

public class S5VerificationTests
{
    const float WALL_LIMIT = 75f;   // 战斗闭环等待上限（墙钟秒）

    [UnityTest]
    public IEnumerator S5_FullClosedLoop()
    {
        // 0) 加载真实 Phase1 场景（autoTest=true → GameManager.Start 触发 AutoRun 召唤 50v50）
        var load = SceneManager.LoadSceneAsync("Phase1", LoadSceneMode.Single);
        while (!load.isDone) yield return null;
        yield return null; // 让 GameManager.Start/AutoRun 在本帧后触发

        var gmGo = GameObject.Find("GameManager");
        var facGo = GameObject.Find("SoldierFactory");
        Assert.IsNotNull(gmGo, "[S5] 加载 Phase1 后未找到 GameManager");
        Assert.IsNotNull(facGo, "[S5] 加载 Phase1 后未找到 SoldierFactory");
        if (gmGo == null || facGo == null) yield break;

        // ---- 反射句柄 ----
        var gmT = gmGo.GetType();                     // Liuhuo.Core.GameManager
        var fWinTeam = gmT.GetField("winTeam");
        var pState = gmT.GetProperty("State");
        var fFactory = gmT.GetField("factory");
        var factory = fFactory.GetValue(gmGo);
        var facT = factory.GetType();                 // Liuhuo.Core.SoldierFactory
        var pActive = facT.GetProperty("ActiveSoldiers", BindingFlags.Public | BindingFlags.Static);
        var fBlueBase = facT.GetField("blueBase");
        var fRedBase = facT.GetField("redBase");

        // ---- D0 基准：factory 基地注入 + 士兵生成量 ----
        int active0 = CountList(pActive.GetValue(null));
        bool blueNull = fBlueBase.GetValue(factory) == null;
        bool redNull = fRedBase.GetValue(factory) == null;
        Debug.Log($"[S5-D0] factory 注入: blueBaseNull={blueNull} redBaseNull={redNull} ActiveSoldiers.Count={active0}");

        // ---- D0 士兵1 细节 + 移动探测（验证士兵是否静止 / A1 是否命中）----
        var soldier0 = FirstElement(pActive.GetValue(null));
        if (soldier0 != null)
        {
            var st = soldier0.GetType();              // Liuhuo.Core.Soldier
            var fEnemy = st.GetField("enemyBase");
            var fTeam = st.GetField("team");
            bool enNull = fEnemy != null && fEnemy.GetValue(soldier0) == null;
            int team = fTeam != null ? (int)fTeam.GetValue(soldier0) : -1;
            var p0 = ((Component)soldier0).transform.position;
            Debug.Log($"[S5-D0] 士兵1 enemyBaseNull={enNull} team={team} pos0={p0}");
            yield return new WaitForSeconds(1.2f);
            var p1 = ((Component)soldier0).transform.position;
            float moved = (p1 - p0).magnitude;
            Debug.Log($"[S5-D0] 士兵1 1.2s 位移={moved:F2} => {(moved > 0.05f ? "移动✓" : "静止✗(A1/A2/A3命中)")}");
        }

        // ---- 性能采集（R3②FrameTimingManager + BCL GC，避开 ProfilerRecorder 模块不可达）----
        int cpuSum = 0, ftFrames = 0;
        long gcStart = System.GC.GetTotalMemory(false);
        long gcMin = long.MaxValue, gcMax = 0;

        // ---- 战斗闭环等待（R5：winTeam != -1 且 State==GameOver）----
        int winTeam = -1; string state = ""; float wall = 0f; bool over = false; int logicalFrames = 0;
        while (wall < WALL_LIMIT)
        {
            yield return null; wall += Time.deltaTime; logicalFrames++;

            FrameTimingManager.CaptureFrameTimings();
            var ft = new FrameTiming[4];
            uint n = FrameTimingManager.GetLatestTimings(4, ft);
            for (int i = 0; i < n && i < 4; i++)
                if (ft[i].cpuFrameTime > 0f) { cpuSum += (int)ft[i].cpuFrameTime; ftFrames++; }

            long gcNow = System.GC.GetTotalMemory(false);
            if (gcNow < gcMin) gcMin = gcNow;
            if (gcNow > gcMax) gcMax = gcNow;

            if (fWinTeam != null) winTeam = (int)fWinTeam.GetValue(gmGo);
            var so = pState != null ? pState.GetValue(gmGo) : null;
            state = so != null ? so.ToString() : "";
            if (winTeam != -1 && state == "GameOver") { over = true; break; }
        }

        long gcEnd = System.GC.GetTotalMemory(false);
        float avgCpuMs = ftFrames > 0 ? (float)cpuSum / ftFrames : -1f;
        float realFps = avgCpuMs > 0 ? 1000f / avgCpuMs : -1f;
        float logicalFps = wall > 0 ? logicalFrames / wall : -1f;
        string fpsStr = realFps > 0 ? realFps.ToString("F1") : "nographics无帧时数据(不冒充)";
        Debug.Log($"[S5-Perf] 真实CPU帧均={avgCpuMs:F1}ms=>真实FPS={fpsStr} | 逻辑帧率={logicalFps:F1}(来源=墙钟推得,非Profiler,标注)");
        Debug.Log($"[S5-Perf] GC(start={gcStart/1024L}KB min={gcMin/1024L}KB max={gcMax/1024L}KB end={gcEnd/1024L}KB)");
        Debug.Log($"[S5-R5] winTeam={winTeam} state={state} GameOver={over} 墙钟={wall:F1}s");

        Assert.IsTrue(over, $"[S5] 75s 内未达 GameOver (winTeam={winTeam}, state={state}) —— 士兵静止或AI未闭环");
        Assert.IsTrue(winTeam == 0 || winTeam == 1, $"[S5] winTeam 非法: {winTeam}");
        Assert.GreaterOrEqual(active0, 1, "[S5] 无士兵生成 (A3 命中)");
    }

    static int CountList(object listObj)
    {
        if (listObj is ICollection col) return col.Count;
        if (listObj != null && listObj is IEnumerable en)
        {
            int c = 0; foreach (var _ in en) c++; return c;
        }
        return 0;
    }

    static object FirstElement(object listObj)
    {
        if (listObj is IEnumerable en)
        {
            var e = en.GetEnumerator();
            return e.MoveNext() ? e.Current : null;
        }
        return null;
    }
}

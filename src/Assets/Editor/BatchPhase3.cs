// BatchPhase3.cs —— Phase-3 batchmode 批入口（B6：batchmode 独占项目，关 Editor 后由 u3d-batch-verify.py 调）
// 复盘#1：确定性任务(打包/构建)走 batchmode spawn-and-exit，从 -logFile 提证据，不信 exit code。
// 现有 batch 入口：SceneAudit.DumpAll / S5_Builder.BuildAll / VerifyP6.SetAutoTest / URPSetup.FixDefaultRenderer / ImportSettingsOptimizer.OptimizeAll。
// 本类补 Phase-3 缺口：G 打包 Win exe（BuildWin）。E(Mono≤400) 须运行时采样，详见各自 Play 验证入口。
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Liuhuo.EditorTools
{
    public static class BatchPhase3
    {
        const string ScenePath = "Assets/Scenes/Phase1.unity";   // BuildScene 保存的战斗场景（含 GameManager/factory/双基地/相机）
        const string OutExe = "Build/Liuhuo.exe";

        // G 打包：先 BuildAll 重建场景/预制体/资产，再 BuildPlayer 产出 exe（冒烟首帧可见 + build.log evidence）
        public static void BuildWin()
        {
            Debug.Log("[BatchPhase3] BuildWin BEGIN");
            S5_Builder.BuildAll();                 // 重建士兵/子弹/基地/Phase1 场景
            AssetDatabase.SaveAssets();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            var report = BuildPipeline.BuildPlayer(
                new[] { ScenePath },
                OutExe,
                BuildTarget.StandaloneWindows64,
                BuildOptions.None);
            var s = report.summary;
            Debug.Log($"[BatchPhase3] BuildWin result={s.result} sizeBytes={s.totalSize} errors={s.totalErrors} warn={s.totalWarnings} output={OutExe} totalTime={s.totalTime.TotalSeconds:F1}s");
            Debug.Log($"[BatchPhase3] BuildWin {s.result.ToString()} -> {OutExe}");
        }
    }
}

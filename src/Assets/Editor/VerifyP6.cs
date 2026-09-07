// VerifyP6.cs —— P6 自测辅助：设场景 GameManager.autoTest（进 Play 自动战斗，触发音频接线点+相机）
// 复用 VerifyHitFX 模式（Liuhuo.EditorTools 静态类 + 反射入口）。验证完调 SetAutoTest(false) 恢复交付态（F5）。
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Liuhuo.Core;

namespace Liuhuo.EditorTools
{
    public static class VerifyP6
    {
        // 设场景 GameManager.autoTest 并保存场景；true=进 Play 自动战斗(触发战斗开始BGM+士兵受击)，false=交付态(F5)
        public static void SetAutoTest(bool on)
        {
            int n = 0;
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var gm = go.GetComponent<GameManager>();
                if (gm == null) continue;
                gm.autoTest = on;
                if (on) gm.autoSummon = 10;   // 最小闭环 10v10
                n++;
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("[VerifyP6] autoTest->" + on + " GameManager count=" + n);
        }
    }
}

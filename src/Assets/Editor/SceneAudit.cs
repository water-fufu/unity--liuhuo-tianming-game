// SceneAudit.cs —— P0 前置核查诊断：遍历当前场景 Renderer 打印机材 shader/instancing，遍历相机
// 供 Phase-2 s5 P0 scene_audit.md 采集 + P2(Instancing)/P5(URP) 复用
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Liuhuo.EditorTools
{
    public static class SceneAudit
    {
        // 调用方：unity-mcp reflection-method-call SceneAudit.DumpAll（编辑器内，非 Play）
        public static void DumpAll()
        {
            var sb = new StringBuilder();
            var scene = SceneManager.GetActiveScene();
            sb.AppendLine($"[SceneAudit] 场景={scene.name} path={scene.path} 已载入={scene.isLoaded}");

            var rends = Object.FindObjectsOfType<Renderer>(true);
            sb.AppendLine($"[SceneAudit] Renderer总数={rends.Length}");
            foreach (var r in rends)
            {
                if (r == null) continue;
                var go = r.gameObject;
                var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0)
                {
                    sb.AppendLine($"[SceneAudit] {go.name} | (no sharedMaterial) | rendererType={r.GetType().Name}");
                    continue;
                }
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    string shader = m.shader != null ? m.shader.name : "null";
                    string rpTag = shader.Contains("Universal") ? "URP"
                        : (shader.Contains("Standard") ? "Builtin" : (shader.Contains("Lit") ? "URP/Lit" : "Other"));
                    string inst = m.enableInstancing ? "INSTANCING" : "no-inst";
                    sb.AppendLine($"[SceneAudit] {go.name} | {m.name} | shader={shader} | {rpTag} | {inst}");
                }
            }

            var cams = Object.FindObjectsOfType<Camera>(true);
            sb.AppendLine($"[SceneAudit] Camera总数={cams.Length}");
            foreach (var c in cams)
                sb.AppendLine($"[SceneAudit] CAMERA {c.name} ortho={c.orthographic} size={c.orthographicSize} pos={c.transform.position} rot={c.transform.eulerAngles}");

            Debug.Log(sb.ToString());
        }
    }
}

// GpuInstancingEnabler.cs —— P2 GPU Instancing：遍历 Assets/Art 材质勾 Enable GPU Instancing
// 对位 s3 P2(TC-E5)：士兵材质×2 + 基地材质×2。SkinnedMeshRenderer(带动画) 实际受骨骼限制，记录但勾选。
using UnityEditor;
using UnityEngine;

namespace Liuhuo.EditorTools
{
    public static class GpuInstancingEnabler
    {
        // 调用方：unity-mcp reflection-method-call（编辑器内）
        public static void EnableAll()
        {
            var sb = new System.Text.StringBuilder();
            string[] mats = AssetDatabase.FindAssets("t:Material", new string[]{"Assets/Art"});
            sb.AppendLine($"[GI] 材质总数={mats.Length}");
            int changed = 0;
            foreach (var guid in mats)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;
                bool was = mat.enableInstancing;
                if (was) { sb.AppendLine($"[GI] {path} | {mat.name} | 已开启，跳过"); continue; }
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                changed++;
                sb.AppendLine($"[GI] {path} | {mat.name} | enableInstancing: false->true");
            }
            AssetDatabase.SaveAssets();
            sb.AppendLine($"[GI] 共改动 {changed} 个材质");
            Debug.Log(sb.ToString());
        }
    }
}

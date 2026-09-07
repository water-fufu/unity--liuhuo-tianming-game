// ImportSettingsOptimizer.cs —— P1 内存优化：士兵/模型 FBX + 纹理导入设置
// 对位 s3 P1(TC-F1)：FBX Read/Write=Off + MeshCompression=Low + AnimationCompression=Optimal；纹理 Compressed + MipMaps + MaxSize=1024
using UnityEditor;
using UnityEngine;

namespace Liuhuo.EditorTools
{
    public static class ImportSettingsOptimizer
    {
        // 调用方：unity-mcp reflection-method-call（编辑器内）。先记旧值→改→保存打印新值（先检测再改，规则14）
        public static void OptimizeAll()
        {
            var sb = new System.Text.StringBuilder();

            // ---- FBX / 模型导入设置 ----
            string[] models = AssetDatabase.FindAssets("t:Model", new string[]{"Assets/Art/Models"});
            sb.AppendLine($"[Import] 模型总数={models.Length}");
            foreach (var guid in models)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) continue;
                sb.AppendLine($"[Import] FBX {path} 改前 readable={imp.isReadable} meshComp={imp.meshCompression} animComp={imp.animationCompression}");
                imp.isReadable = false;
                imp.meshCompression = ModelImporterMeshCompression.Low;
                if (imp.animationType != ModelImporterAnimationType.None)
                    imp.animationCompression = ModelImporterAnimationCompression.Optimal;
                EditorUtility.SetDirty(imp);
                imp.SaveAndReimport();
                sb.AppendLine($"[Import] FBX {path} 改后 readable={imp.isReadable} meshComp={imp.meshCompression} animComp={imp.animationCompression}");
            }

            // ---- 纹理导入设置 ----
            string[] tex = AssetDatabase.FindAssets("t:Texture2D", new string[]{"Assets/Art"});
            sb.AppendLine($"[Import] 纹理总数={tex.Length}");
            foreach (var guid in tex)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".psd") || path.EndsWith(".tga")) continue;
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                sb.AppendLine($"[Import] TEX {path} 改前 compression={ti.textureCompression} mip={ti.mipmapEnabled} maxSize={ti.maxTextureSize}");
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.mipmapEnabled = true;
                ti.maxTextureSize = 512;   // G1 第四轮：1024→512（士兵贴图2048实测大头，压Mono）
                EditorUtility.SetDirty(ti);
                ti.SaveAndReimport();
                sb.AppendLine($"[Import] TEX {path} 改后 compression={ti.textureCompression} mip={ti.mipmapEnabled} maxSize={ti.maxTextureSize}");
            }

            Debug.Log(sb.ToString());
        }
    }
}

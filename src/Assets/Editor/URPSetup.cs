// URPSetup.cs —— P5 URP 管线启用：建 URP Asset + 切 Graphics/Quality Settings + 升级 Built-in Standard 材质→URP/Lit
// 根治坑#40（URP/Lit 材质在 Built-in 回退 Standard→洋红）。F3 防回退：相对 Built-in 基线(FPS 223.4/DrawCall 79)在 P7 对比。
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Liuhuo.EditorTools
{
    [InitializeOnLoad]
    public static class URPSetup
    {
        // 编译域重载后自动强修 default renderer：绕开 MCP 反射调静态方法(EnableURP/FixDefaultRenderer)不可靠的坑。
        // 磁盘 m_RendererDataList 已对但运行时内存实例列表空(EnableURP 首次 Create(renderer) 时未入 list) →
        // 缺省管线报 "Default Renderer is missing"。本构造器在每次编译完成后用强壮版重填。
        static URPSetup()
        {
            EditorApplication.delayCall += delegate { try { FixDefaultRendererForce(); } catch (System.Exception e) { Debug.Log("[URP] 自动强修异常 " + e.Message); } };
        }
        public static void EnableURP()
        {
            var sb = new System.Text.StringBuilder();
            EnsureFolder("Assets/Settings");

            // 1. 建 RendererData + URP Asset（标准向导路径）
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, "Assets/Settings/URP_Renderer.asset");
            var asset = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(asset, "Assets/Settings/URP_PipelineAsset.asset");

            // 2. 切管线（GraphicsSettings + QualitySettings 都设，否则回退 Built-in）
            GraphicsSettings.defaultRenderPipeline = asset;
            QualitySettings.renderPipeline = asset;
            AssetDatabase.SaveAssets();
            sb.AppendLine("[URP] 管线已启用 asset=" + asset.name + " renderer=" + renderer.name);

            // 3. 升级 Built-in Standard 材质 -> URP/Lit（防洋红坑#40）
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            int n = 0;
            var guids = AssetDatabase.FindAssets("t:Material");
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || m.shader == null) continue;
                if (m.shader.name == "Standard")
                {
                    m.shader = lit;
                    m.SetFloat("_Smoothness", 0.05f);
                    n++;
                }
            }
            sb.AppendLine("[URP] Standard->URP/Lit 升级 " + n + " 个材质");
            UpgradeSceneRenderers();
            Debug.Log(sb.ToString());
        }

        // 场景内嵌材质（非独立 .mat 资产，如 Ground）也需 Standard->URP/Lit，否则 URP 下回退洋红(坑#40)
        static void UpgradeSceneRenderers()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            int sceneN = 0;
            var rends = Object.FindObjectsOfType<Renderer>(true);
            foreach (var r in rends)
            {
                if (r == null) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    if (m.shader.name == "Standard")
                    {
                        m.shader = lit;
                        m.SetFloat("_Smoothness", 0.05f);
                        sceneN++;
                    }
                }
            }
            Debug.Log("[URP] 场景内嵌 Standard 材质 -> URP/Lit 升级 " + sceneN + " 个");
        }

        // P5 遗留修复：URP asset 未设 default renderer（Create(renderer) 未填入 RendererDataList），
        // 致 URP 报 "Default Renderer is missing on URP_PipelineAsset"（渲染失效/洋红）。补设 rendererDataList + defaultRendererIndex。
        public static void FixDefaultRenderer()
        {
            var asset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) { Debug.Log("[URP] FixDefaultRenderer: 默认管线非URP，跳过"); return; }
            var so = new SerializedObject(asset);
            var list = so.FindProperty("m_RendererDataList");
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>("Assets/Settings/URP_Renderer.asset");
            int before = list.arraySize;
            if (before == 0 && renderer != null)
            {
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            }
            var idx = so.FindProperty("m_DefaultRendererIndex");
            if (idx != null) idx.intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"[URP] FixDefaultRenderer 完成 before={before} size={list.arraySize} defaultIdx={(idx != null ? idx.intValue : -1)}");
        }

        // R1 强壮版：显式 LoadAssetAtPath 加载管线资产 + renderer，不依赖 GraphicsSettings 已注册管线。
        // 旧 FixDefaultRenderer 依赖 GraphicsSettings.defaultRenderPipeline（若非URP为null会走"跳过"分支，实际没修到）。
        // 本版本直接定位 asset 磁盘实例，强制把 renderer 塞进 m_RendererDataList + 设 defaultIdx=0，并补注册 GraphicsSettings/QualitySettings。
        public static void FixDefaultRendererForce()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/URP_PipelineAsset.asset");
            if (asset == null) { Debug.Log("[URP] FixDefaultRendererForce: 管线资产不存在"); return; }
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>("Assets/Settings/URP_Renderer.asset");
            if (renderer == null) { Debug.Log("[URP] FixDefaultRendererForce: renderer 资产不存在"); return; }
            var so = new SerializedObject(asset);
            var list = so.FindProperty("m_RendererDataList");
            if (list.arraySize == 0)
            {
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            }
            var idx = so.FindProperty("m_DefaultRendererIndex");
            if (idx != null) idx.intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            // 补注册：确保运行时管线真的挂在 GraphicsSettings/QualitySettings（防 missing 持续）
            GraphicsSettings.defaultRenderPipeline = asset;
            QualitySettings.renderPipeline = asset;
            AssetDatabase.SaveAssets();
            Debug.Log($"[URP] FixDefaultRendererForce 完成 listSize={list.arraySize} defaultIdx={(idx != null ? idx.intValue : -1)} 管线已注册 asset={asset.name}");
        }

        static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}

// MapLoader.cs —— P0-1 双地图加载（竖向主地图 + 横向 gBase 叠底），对位 Web gBase 配置
// -----------------------------------------------------------------------------
// 布局约定（Liuhuo.Core，纯运行时创建，不依赖场景预设）：
//   竖向主地图：150×200 XZ 平面 @ (0,0,0)，贴 game_map_bg_v2，主战场（含 MeshCollider 供士兵站立）。
//   横向 gBase：300×84 远景叠底，默认 @ (0,0.4,-196)，贴 horizontal_map_seamless_v1，
//               通过自定义 Liuhuo/OverlayUnlit 实现 ZTest Always（深度穿透、恒置顶层），对位 Web gBase。
//               pitch 绕"下边界轴"倾斜：子级 Quad 先 rotX(-90) 平铺到 XZ，再把下边界平移到父级原点
//               （即下边界=旋转轴），父级再整体绕 X 轴旋转 pitch，保证绕下边界为轴。
//               分界线（横向图下边界 ≈ 世界 Z=-196）因此可见。
//   全局入口：MapLoader.Create()。map-config.json（Resources/config/map-config）启动读取覆盖 gBase
//               全部参数(width/height/x/y/z/pitch/renderOrder)，缺失则用内置默认(0,0.4,-196, 300×84)。
// -----------------------------------------------------------------------------
using UnityEngine;

namespace Liuhuo.Core
{
    public class MapLoader : MonoBehaviour
    {
        /// <summary>map-config.json 的 Resources 路径（不带扩展名）。Resources.Load&lt;TextAsset&gt;。</summary>
        public const string DefaultConfigPath = "config/map-config";

        // ===== 竖向主地图 =====
        [Header("竖向主地图")]
        [Tooltip("主地图 X 宽（m）")]
        public float mainWidth = 500f;
        [Tooltip("主地图 Z 深（m）")]
        public float mainDepth = 230.3606f;   // ★用户图 Scale Z=23.03606 → mainDepth=23.03606*10
        [Tooltip("主地图贴图 Resources 路径")]
        public string mainTextureKey = "map/game_map_bg_v2";
        [Tooltip("主地图 Y 高度（贴合地面）")]
        public float mainY = 0.01f;   // 略抬以免与场景占位 Ground 共面 z-fight
        [Tooltip("主地图 Z 深度偏移（向前/后移动主图）")]
        public float mainZ = 12f;   // ★用户图 Position Z=12
        [Tooltip("true=运行时隐藏场景里已存在的同名占位 Ground(30×30)的渲染，防止叠影")]
        public bool replaceExistingGround = true;

        // ===== 横向 gBase（默认对位 Web，未读配置时兜底） =====
        [Header("横向 gBase")]
        [Tooltip("gBase 下边界锚点（接触点）世界坐标，默认 (0,0.4,-196)")]
        public Vector3 gBasePosition = new Vector3(0f, 0.4f, -196f);
        [Tooltip("gBase 宽（X 向，m）")]
        public float gBaseWidth = 300f;
        [Tooltip("gBase 高（平铺后为纵深/沿倾斜面，m）")]
        public float gBaseHeight = 84f;
        [Tooltip("绕下边界轴的俯仰角（0=全平铺，90=立起），对位 Web pitch")]
        public float gBasePitch = 45f;
        [Tooltip("叠底在透明队列之上的偏移：renderQueue = 3000 + renderOrder")]
        public int gBaseRenderOrder = 2;
        [Tooltip("gBase 贴图 Resources 路径")]
        public string gBaseTextureKey = "map/horizontal_map_seamless_v1";
        [Tooltip("置顶叠底 shader 名（自定义，ZTest Always）")]
        public string gBaseShaderName = "Liuhuo/OverlayUnlit";

        // ===== 第二张图 MainMap(1)（远景置顶叠底，ZTest Always 不被主图遮挡） =====
        [Header("第二张图 MainMap(1)")]
        [Tooltip("true=生成第二张远景图（叠底置顶）")]
        public bool secondEnabled = true;
        [Tooltip("第二张图 位置（世界坐标）")]
        public Vector3 secondPosition = new Vector3(0f, -12f, -252f);
        [Tooltip("第二张图 绕 X 轴俯仰角（倾斜立起）")]
        public float secondPitch = 50f;
        [Tooltip("第二张图 localScale（Unity Inspector 直接填入的值）")]
        public Vector3 secondScale = new Vector3(90f, 1f, 20f);
        [Tooltip("第二张图 渲染顺序：renderQueue = 3000 + renderOrder")]
        public int secondRenderOrder = 2;
        [Tooltip("第二张图 贴图 Resources 路径")]
        public string secondTextureKey = "map/horizontal_map_seamless_v1";
        [Tooltip("置顶叠底 shader 名（自定义，ZTest Always）")]
        public string secondShaderName = "Liuhuo/OverlayUnlit";

        // 运行时暴露的变换，供外部(相机/调试)引用
        public Transform MainMapRoot { get; private set; }
        public Transform GBaseRoot { get; private set; }
        public Transform SecondMapRoot { get; private set; }
        public Material GBaseMaterial { get; private set; }
        public bool IsBuilt { get; private set; }
        /// <summary>是否成功应用到 ZTest Always（false 表示降级为置顶 queue，深度遮挡仍可能有风险）。</summary>
        public bool GBaseDepthAlways { get; private set; }
        /// <summary>gBase 叠底开关（默认 true）。主相机俯视时直立墙会挡中央形成黑幕，map-config.json 置 enabled:false 关闭。</summary>
        public bool gBaseEnabled = true;

        // ===== 公共入口 =====
        /// <summary>创建并自动构建两个地图。AddComponent 同步触发 Awake，返回时已构建完毕。</summary>
        public static MapLoader Create()
        {
            var go = new GameObject("MapLoader");
            return go.AddComponent<MapLoader>();
        }

        void Awake()
        {
            // AddComponent 同步调用 Awake：先读配置(可能覆盖字段)再构建，Create 返回即可用
            if (Application.isPlaying) LoadConfig();
            BuildAll();
        }

        // ===== 构建主流程（同步） =====
        void BuildAll()
        {
            BuildMainMap();
            if (gBaseEnabled) BuildGBase();
            if (secondEnabled) BuildSecondMap();
            IsBuilt = true;
            Debug.Log($"[MapLoader] 构建完成 main={mainWidth}x{mainDepth}@({0},{mainY},{0}) | " +
                      $"gBase=({gBasePosition.x},{gBasePosition.y},{gBasePosition.z}) {gBaseWidth}x{gBaseHeight} " +
                      $"pitch={gBasePitch} ro={gBaseRenderOrder} depthAlways={GBaseDepthAlways}");
        }

        // ===== 竖向主地图 =====
        void BuildMainMap()
        {
            // 若场景已有占位 Ground，先隐藏其渲染，避免 150×200 与 30×30 共面 z-fight（不改场景文件）
            if (replaceExistingGround) HideCoLocatedGround();

            var tex = Resources.Load<Texture2D>(mainTextureKey);
            var mat = MakeUnlitMaterial(tex, Color.white, "main");
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "MainMap";
            plane.transform.SetParent(transform, false);
            plane.transform.position = new Vector3(0f, mainY, mainZ);
            // 默认 Plane 10×10，缩放到 mainWidth×mainDepth
            plane.transform.localScale = new Vector3(mainWidth / 10f, 1f, mainDepth / 10f);
            var r = plane.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            // 保留 Plane 自带 MeshCollider，供士兵站立/射线
            MainMapRoot = plane.transform;
        }

        // 场景里已存在的 "Ground"（30×30 占位）仅关闭渲染，保留其 Collider（防破坏依赖），叠影即可消除。
        void HideCoLocatedGround()
        {
            var go = GameObject.Find("Ground");
            if (go == null) return;
            var r = go.GetComponent<Renderer>();
            if (r != null) r.enabled = false;
            Debug.Log("[MapLoader] 已隐藏场景占位 Ground 的渲染（保留其 Collider），由 MainMap 替代显示");
        }

        // ===== 横向 gBase（叠底） =====
        void BuildGBase()
        {
            var tex = Resources.Load<Texture2D>(gBaseTextureKey);

            // 叠底需要 ZTest Always 才"穿透置顶"；URP 标准 Lit/Unlit 无材质级 ZTest 开关 → 用自定义 shader。
            // Editor/场景验证能命中；Player 若被 strips → Shader.Find 返回 null，走兜底降级并如实说明。
            Shader sh = Shader.Find(gBaseShaderName);
            Material mat;
            if (sh != null)
            {
                mat = new Material(sh);
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex"))  mat.SetTexture("_MainTex", tex);
                GBaseDepthAlways = true;
            }
            else
            {
                // 兜底：URP/Unlit + 置顶 queue。注意无 ZTest Always，深度遮挡仍可能盖住 → 如实降级。
                mat = MakeUnlitMaterial(tex, Color.white, "gBase");
                GBaseDepthAlways = false;
                Debug.LogWarning($"[MapLoader] 未找到 {gBaseShaderName}，降级 URP/Unlit+置顶queue。" +
                                 " depthTest=false 的恒置顶效果仅自定义 shader(ZTest Always)可达成；" +
                                 "若为 Player 打包被 strips，请将 Liuhuo/OverlayUnlit 加入 GraphicsSettings 的 Always Included Shaders。");
            }
            // 置顶：透明队列(3000) + 自定义偏移
            mat.renderQueue = 3000 + gBaseRenderOrder;
            GBaseMaterial = mat;

            // 父级 Root = 下边界/旋转轴锚点，位于世界 gBasePosition，整体绕 X 轴旋转 pitch
            var parent = new GameObject("GBase_Root");
            parent.transform.SetParent(transform, false);
            parent.transform.position = gBasePosition;
            parent.transform.rotation = Quaternion.Euler(gBasePitch, 0f, 0f);
            GBaseRoot = parent.transform;

            // 子级 Quad：局部几何 rotX(-90) 平铺到 XZ(上边缘→-Z)，再平移使"下边缘"移到父级原点(轴上)
            var plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plane.name = "GBase";
            plane.transform.SetParent(parent.transform, false);
            plane.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // 平铺：下边缘→局部 +Z/2
            plane.transform.localScale = new Vector3(gBaseWidth, gBaseHeight, 1f);
            plane.transform.localPosition = new Vector3(0f, 0f, -gBaseHeight * 0.5f);   // 下边缘归零=绕轴
            var r = plane.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var col = plane.GetComponent<Collider>();
            if (col != null) col.enabled = false;   // 纯视觉叠底，不参与物理/射线
        }

        // ===== 第二张图 MainMap(1)：远景叠底，ZTest Always 置顶不被主图遮挡 =====
        void BuildSecondMap()
        {
            var tex = Resources.Load<Texture2D>(secondTextureKey);
            Shader sh = Shader.Find(secondShaderName);
            Material mat;
            if (sh != null)
            {
                mat = new Material(sh);
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex"))  mat.SetTexture("_MainTex", tex);
            }
            else
            {
                // 兜底：无 ZTest Always，可能被主图遮挡 → 如实降级
                mat = MakeUnlitMaterial(tex, Color.white, "second");
                Debug.LogWarning($"[MapLoader] 未找到 {secondShaderName}，第二张图降级 URP/Unlit+置顶queue（无 ZTest Always，可能被主图遮挡）");
            }
            mat.renderQueue = 3000 + secondRenderOrder;   // 透明队列之上，置顶

            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "MainMap (1)";
            plane.transform.SetParent(transform, false);
            plane.transform.position = secondPosition;             // (0,-12,-252)
            plane.transform.localRotation = Quaternion.Euler(secondPitch, 0f, 0f);  // X 倾斜立起
            plane.transform.localScale = secondScale;               // (90,1,20) 直接填入
            var r = plane.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var col = plane.GetComponent<Collider>();
            if (col != null) col.enabled = false;   // 纯视觉远景，不参与物理/射线
            SecondMapRoot = plane.transform;
        }

        // ===== 材质辅助（Unlit） =====
        Material MakeUnlitMaterial(Texture2D tex, Color c, string tag)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Unlit/Texture");
            if (s == null) s = Shader.Find("Sprites/Default");
            var m = new Material(s);
            if (tex != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_MainTex"))  m.SetTexture("_MainTex", tex);
            }
            else
            {
                Debug.LogWarning($"[MapLoader] 贴图缺失({tag})，用纯色兜底");
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color"))      m.SetColor("_Color", c);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            return m;
        }

        // ===== 读 map-config.json（对位 Web fetch），覆盖 gBase 参数 =====
        void LoadConfig()
        {
            var cfg = Resources.Load<TextAsset>(DefaultConfigPath);
            if (cfg == null)
            {
                Debug.Log($"[MapLoader] 未找到 {DefaultConfigPath}.json，用内置默认 gBase=({gBasePosition.x},{gBasePosition.y},{gBasePosition.z}) {gBaseWidth}x{gBaseHeight} pitch={gBasePitch}");
                return;
            }
            try
            {
                var root = JsonUtility.FromJson<MapConfig>(cfg.text);
                if (root == null || root.gBase == null)
                {
                    Debug.LogWarning("[MapLoader] map-config.json 无 gBase 节点，用内置默认");
                    return;
                }
                var g = root.gBase;
                // 仅覆盖显式提供的字段（缺省保持内置默认），未提供的字段是哨兵值
                if (!float.IsNaN(g.width)  && g.width  > 0f) gBaseWidth  = g.width;
                if (!float.IsNaN(g.height) && g.height > 0f) gBaseHeight = g.height;
                if (!float.IsNaN(g.x)) gBasePosition.x = g.x;
                if (!float.IsNaN(g.y)) gBasePosition.y = g.y;
                if (!float.IsNaN(g.z)) gBasePosition.z = g.z;
                if (!float.IsNaN(g.pitch)) gBasePitch = g.pitch;
                if (!float.IsNaN(g.renderOrder)) gBaseRenderOrder = Mathf.RoundToInt(g.renderOrder);
                gBaseEnabled = g.enabled;
                Debug.Log($"[MapLoader] 读取 {DefaultConfigPath}.json：gBase=({gBasePosition.x},{gBasePosition.y},{gBasePosition.z}) {gBaseWidth}x{gBaseHeight} pitch={gBasePitch} ro={gBaseRenderOrder}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MapLoader] map-config.json 解析失败，用内置默认: " + e.Message);
            }
        }

        // ===== map-config.json schema（哨兵值：NaN/-1 表示"未提供"，JsonUtility 读不到时不覆盖） =====
        [System.Serializable]
        public class GBaseNode
        {
            public float width = float.NaN;
            public float height = float.NaN;
            public float x = float.NaN;
            public float y = float.NaN;
            public float z = float.NaN;
            public float pitch = float.NaN;
            public float renderOrder = float.NaN;
            public bool enabled = true;
        }

        [System.Serializable]
        public class MapConfig
        {
            public GBaseNode gBase;
        }
    }
}

// PoseAnimSystem.cs —— B 方案：静态网格 GPU 实例化渲染（B2-T4 修正版，S5 P0-4 方案A）
// 对位 Web SoldierRenderManager 实例化路线。现实约束：士兵源模型 tianting_soldier_q.fbx 为无骨骼
// 静态网格（m_Bones=0，非蒙皮可动），PoseBaker 的「walk 剪辑 + SkinnedMeshRenderer.BakeMesh」产 4 姿势
// 方案不适用（骨骼为空→BakeMesh 无效）。故退化为「取士兵 Renderer 的 sharedMesh（退化蒙皮/静态皆取
// sharedMesh）+ 实例材质 × Graphics.DrawMeshInstanced 双阵营驱动」→ 目标 DC≤5（D1 硬线）。
// ★ P0-4 方案A：白无贴图根因 = URP/Lit + SRP Batcher 下 instanced 材质属性不落地 GPU（连 _BaseColor 都不吃）。
//   修复 =
//   ① 分红蓝 mesh+材质：天庭资源(Mat_TianTing_Soldier 绑 tianting_soldier_basecolor.jpg 金士兵)/
//      玄朝资源(Mat_XuanChao_Soldier 绑 xuanchao_soldier_basecolor.jpg 红士兵)，各自保留真实贴图本色。
//   ② 材质从 URP/Lit 改 URP/Unlit：本项目实测 Shader.Find("Universal Render Pipeline/Lit")→null 洋红，
//      URP/Unlit 可渲(见 BaseModelFactory L134-144 注释)，规避 SRP Batcher + instancing 不吃 per-material 属性。
//   ③ 复制源材质 _BaseMap 贴图 + 白色 tint 保留贴图本色(金/红士兵天然阵营色)。
//   ④ 渲染用新 API Graphics.RenderMeshInstanced(2021.2+/URP14)，正确应用材质属性(旧 API DrawMeshInstanced 为坑源)。
// 零侵入：RuntimeInitializeOnLoadMethod 自举，不改现有脚本。
using System.Collections.Generic;
using UnityEngine;

namespace Liuhuo.Core
{
    [DefaultExecutionOrder(-90)]
    public class PoseAnimSystem : MonoBehaviour
    {
        public static PoseAnimSystem Instance { get; private set; }

        SoldierFactory _factory;
        Mesh _blueMesh;                  // 天庭士兵共享网格
        Mesh _redMesh;                   // 玄朝士兵共享网格
        int _subCount = 1;               // 网格 subMesh 数
        Material[] _blueMats;            // 每 subMesh 天庭 instanced 材质(URP/Unlit + 天庭贴图)
        Material[] _redMats;             // 每 subMesh 玄朝 instanced 材质(URP/Unlit + 玄朝贴图)
        readonly List<Matrix4x4> _blue = new List<Matrix4x4>(128);
        readonly List<Matrix4x4> _red = new List<Matrix4x4>(128);
        readonly Dictionary<Soldier, int> _hidden = new Dictionary<Soldier, int>();
        // 方案B开关：false=弃 instanced(URP instanced 材质属性不落地 GPU→白,记忆 u3d-drawmeshinstanced-material-whitened)，
        //             士兵走 prefab 本体 MeshRenderer(Mat_XX URP/Unlit+金贴图)换取 dist10 金/红贴图战士；
        //             true=启用 instanced(DC≤5 但视觉白)。I 轴视觉 fidelity 优先于 DC≤5。
        static readonly bool s_useInstanced = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("[PoseAnimSystem]");
            DontDestroyOnLoad(go);
            go.AddComponent<PoseAnimSystem>();
        }

        void Awake() { Instance = this; }

        void Start()
        {
            if (!s_useInstanced) { enabled = false; return; }   // 方案B：禁自身则 Update 不跑，不隐藏 Soldier 原始 Renderer → 士兵走 prefab 本体 Mat_XX 上色
            _factory = FindObjectOfType<SoldierFactory>();
            if (_factory == null || _factory.bluePrefab == null)
            {
                Debug.LogError("[PoseAnim] 未找到 SoldierFactory/bluePrefab，B 禁用");
                enabled = false;
                return;
            }

            // P0-4 方案A：蓝/红各取 Renderer(材质 = Mat_TianTing/Mat_XuanChao，_BaseMap 贴图对)
            var blueRend = GrabRenderer(_factory.bluePrefab);
            var redRend = _factory.redPrefab != null ? GrabRenderer(_factory.redPrefab) : null;
            if (blueRend == null)
            {
                Debug.LogWarning("[PoseAnim] 士兵无可取 Renderer(无 MeshRenderer/SkinnedMeshRenderer)，禁用自身，回退原渲染路径");
                enabled = false;
                return;
            }
            _blueMesh = GetMesh(blueRend);
            _redMesh = redRend != null ? GetMesh(redRend) : null;
            if (_blueMesh == null || _blueMesh.vertexCount == 0)
            {
                Debug.LogWarning("[PoseAnim] 士兵 sharedMesh 为空，禁用自身，回退原渲染路径");
                enabled = false;
                return;
            }
            if (_redMesh == null || _redMesh.vertexCount == 0) _redMesh = _blueMesh;   // 玄朝网格兜底
            _subCount = Mathf.Max(1, _blueMesh.subMeshCount);

            // 阵营色(天庭金≈(1.0,0.82,0.35)/玄朝红≈(0.82,0.12,0.1))：dist10 士兵由 basecolor 贴图提供金/红，
            // 此 tint 作 instanced 材质 base color 保险——贴图采样生效则叠加本色，失效则至少显示阵营色(防全白)。
            _blueMats = BuildInstancedMats(blueRend, new Color(1.0f, 0.82f, 0.35f));   // 天庭(金)
            _redMats  = BuildInstancedMats(redRend, new Color(0.82f, 0.12f, 0.1f));    // 玄朝(红)
            Debug.Log($"[PoseAnim] B 就绪 blueMesh={_blueMesh.name} redMesh={_redMesh.name} subMesh={_subCount}");

            foreach (var s in SoldierFactory.ActiveSoldiers) EnsureHidden(s);
        }

        // P0-4：从源 Renderer 的 sharedMaterials(本体=Mat_TianTing/Mat_XuanChao,_BaseMap贴图对)建 URP/Unlit instanced 材质
        Material[] BuildInstancedMats(Renderer rend, Color tint)
        {
            var arr = new Material[_subCount];
            if (rend == null) return arr;
            Material[] src = rend.sharedMaterials;
            for (int i = 0; i < _subCount; i++)
            {
                var baseM = (i < src.Length) ? src[i] : (src.Length > 0 ? src[0] : null);
                arr[i] = baseM != null ? MakeInstancedMat(baseM, tint) : null;
            }
            return arr;
        }

        // 用 URP/Unlit 建 instanced 材质(规避 URP/Lit+SRP Batcher 属性不落地)：复制源贴图 + 白色tint 保留贴图本色(金/红士兵)
        static Material MakeInstancedMat(Material src, Color tint)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Standard");
            var m = new Material(s) { enableInstancing = true };
            if (src.HasProperty("_BaseMap"))
                m.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
            else if (src.HasProperty("_MainTex"))
                m.SetTexture("_MainTex", src.GetTexture("_MainTex"));
            // 阵营色 base color(金/红)：instanced 材质贴图在 URP 下采样可能不落地 GPU → 全白(记忆 u3d-drawmeshinstanced-material-whitened)。
            // SetColor 作兜底——贴图采样生效则叠加本色，失效则至少显示阵营色。双属性(_BaseColor URP/_Color Standard)保险。
            m.SetColor("_BaseColor", tint);
            m.SetColor("_Color", tint);
            return m;
        }

        void Update()
        {
            if (_blueMesh == null) return;
            _blue.Clear(); _red.Clear();
            var all = SoldierFactory.ActiveSoldiers;
            for (int i = 0; i < all.Count; i++)
            {
                Soldier s = all[i];
                if (s == null || !s.gameObject.activeInHierarchy) continue;
                // 用完整 localToWorldMatrix 保留死亡压扁/前倾等实际表现（含 scale/rotation），非仅 yaw
                var m = s.transform.localToWorldMatrix;
                if (s.team == 0) _blue.Add(m); else _red.Add(m);
                EnsureHidden(s);
            }
            for (int sub = 0; sub < _subCount; sub++)
            {
                if (_blueMats != null && _blueMats[sub] != null) DrawMeshSub(_blueMesh, sub, _blueMats[sub], _blue);
                if (_redMats != null && _redMats[sub] != null) DrawMeshSub(_redMesh, sub, _redMats[sub], _red);
            }
        }

        // P0-4 方案A：用新 API RenderMeshInstanced(正确应用材质属性)；旧 API DrawMeshInstanced 为白块坑源
        static void DrawMeshSub(Mesh mesh, int sub, Material mat, List<Matrix4x4> bucket)
        {
            if (bucket.Count == 0) return;
            var rp = new RenderParams(mat)
            {
                worldBounds = new Bounds(Vector3.zero, new Vector3(2000f, 2000f, 2000f)),
                matProps = null,
            };
            Graphics.RenderMeshInstanced(rp, mesh, sub, bucket.ToArray(), bucket.Count);
        }

        static Renderer GrabRenderer(Soldier prefab)
        {
            if (prefab == null) return null;
            var sk = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
            if (sk != null && sk.sharedMesh != null && sk.sharedMesh.vertexCount > 0) return sk;
            var mr = prefab.GetComponentInChildren<MeshRenderer>();
            if (mr != null && GetMesh(mr) != null && GetMesh(mr).vertexCount > 0) return mr;
            return (Renderer)sk ?? mr;
        }

        // Renderer 无 sharedMesh 聚合属性：SkinnedMeshRenderer 直接取 .sharedMesh；
        // MeshRenderer 的 mesh 来自同 GameObject 的 MeshFilter.sharedMesh。
        static Mesh GetMesh(Renderer r)
        {
            var sk = r as SkinnedMeshRenderer;
            if (sk != null) return sk.sharedMesh;
            var mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        void EnsureHidden(Soldier s)
        {
            if (s == null) return;
            if (_hidden.ContainsKey(s)) return;
            var rends = s.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < rends.Length; i++) rends[i].enabled = false;
            _hidden[s] = 0;
        }
    }
}

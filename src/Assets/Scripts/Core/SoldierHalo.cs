// SoldierHalo.cs —— 士兵脚下阵营色呼吸光环（对位 dist10 SoldierRenderManager haloMesh，任务4）
// 原版 SoldierRenderManager.js L678-694：RingGeometry(0.34,0.56,24) rotateX(-90°) 平铺 XZ，
//   haloColor = (team==='blue') ? 0xFFD700 : 0xFF3B30；MeshBasicMaterial{AdditiveBlending, depthWrite:false,
//   DoubleSide, opacity:(blue?0.55:1.0)}；renderOrder=1。
// 呼吸原版 _syncHalos L959-1000：s = 1 + sin(_frameCount*0.05 + i)*0.15（每实例相位错开防同步），
//   位置 m.setPosition(soldier.x, 0.03, soldier.z)（只取 x/z，y 恒定 0.03 贴地）。
// u3d 等价：24 段内0.34/外0.56 圆环，URP/Unlit Additive 双面贴地 y=0.03，每帧同步士兵 x/z + 呼吸脉动。
// ★独立 GameObject 而非士兵子物体：士兵 DieFx 倒地会改 localScale(压扁)+rotation，若光环挂子物体会连带变形；
//   对位原版 haloMesh 是独立 InstancedMesh 只取 x/z，故跟随用独立物体。
using UnityEngine;
using UnityEngine.Rendering;

namespace Liuhuo.Core
{
    public class SoldierHalo : MonoBehaviour
    {
        const int SEG = 24;                       // 对位原版 RingGeometry(0.34,0.56,24)
        const float INNER = 0.34f, OUTER = 0.56f;
        // dist10 haloMesh 纯色：蓝队(天庭)=金 0xFFD700 / 红队(玄朝)=红 0xFF3B30（对位原版，非 ZoneRing 的红0xff0000）
        static readonly Color TIAN_GOLD = new Color(1f, 0.843f, 0f, 0.55f);    // 0xFFD700 + opacity0.55
        static readonly Color XUAN_RED  = new Color(1f, 0.231f, 0.188f, 1f);   // 0xFF3B30 + opacity1.0

        Soldier _soldier;      // 被跟随的士兵（供 Update 取 position.x/z + team 定色，已确认后绑定）
        int _team;
        float _phase;          // 脉动相位（每兵错开，对位原版 _frameCount*0.05+i；用 Random 起始错开防同步）

        /// <summary>为士兵创建并绑定光环（由 Soldier.EnsureHalo 调用；独立 GameObject 挂 factory/场景下）。</summary>
        public static SoldierHalo Create(Soldier soldier, Transform parent)
        {
            var go = new GameObject("SoldierHalo_" + soldier.team);
            if (parent != null) go.transform.SetParent(parent, false);
            var halo = go.AddComponent<SoldierHalo>();
            halo._soldier = soldier;
            halo._team = soldier.team;
            halo._phase = Random.value * Mathf.PI * 2f;
            halo.Build();
            return halo;
        }

        /// <summary>绑定（池化复用士兵时由 EnsureHalo 调用，刷新 soldier 引用）。</summary>
        public void Bind(Soldier soldier)
        {
            _soldier = soldier;
            if (soldier != null) _team = soldier.team;
            gameObject.SetActive(true);
        }

        void Build()
        {
            var mf = gameObject.AddComponent<MeshFilter>();
            var mr = gameObject.AddComponent<MeshRenderer>();
            mf.sharedMesh = BuildRingMesh();
            mr.sharedMaterial = BuildMat();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        static Mesh BuildRingMesh()
        {
            var mesh = new Mesh();
            var verts = new Vector3[SEG * 2];
            var tris = new int[SEG * 6];
            for (int i = 0; i < SEG; i++)
            {
                float a = (i / (float)SEG) * Mathf.PI * 2f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts[i * 2]     = new Vector3(c * INNER, 0f, s * INNER);
                verts[i * 2 + 1] = new Vector3(c * OUTER, 0f, s * OUTER);
            }
            for (int i = 0; i < SEG; i++)
            {
                int a = i * 2, b = i * 2 + 1, c = ((i + 1) % SEG) * 2, d = ((i + 1) % SEG) * 2 + 1;
                int t = i * 6;
                tris[t]     = a; tris[t + 1] = c; tris[t + 2] = b;
                tris[t + 3] = b; tris[t + 4] = c; tris[t + 5] = d;
            }
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            return mesh;
        }

        Material BuildMat()
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Standard");
            var m = new Material(s);
            Color c = _team == 0 ? TIAN_GOLD : XUAN_RED;
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            // Additive 透明(对位原版 AdditiveBlending)：_Blend=2, 双属性兜底(同 ZoneRing 构造，防 URP 属性不落地)
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.One);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        void Update()
        {
            if (_soldier == null) return;
            Vector3 p = _soldier.transform.position;
            transform.position = new Vector3(p.x, 0.03f, p.z);   // 对位原版 y=0.03 贴地，只取 x/z
            // 呼吸脉动：1+sin(t*2+phase)*0.15（每兵相位错开，对位原版 _frameCount*0.05+i）
            // ★对位原版 _syncHalos 的 makeScale(s*soldierScale)：士兵 prefab scale3.5，环须随士兵放大
            //   （否则顶点半径 0.34/0.56 相对 3.5 士兵过小几乎不可见）；k=呼吸因子 * sc(soldierScale)。
            float sc = Mathf.Max(0.01f, _soldier.transform.localScale.x);
            float k = 1f + Mathf.Sin(Time.time * 2f + _phase) * 0.15f;
            transform.localScale = new Vector3(k * sc, 1f, k * sc);
        }
    }
}

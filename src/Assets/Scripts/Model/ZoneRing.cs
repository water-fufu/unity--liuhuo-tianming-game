// ZoneRing.cs —— 召唤区地面发光光环（对位 dist10 的 zone-ring）
// dist10 main.js L354-373：new THREE.Mesh(new THREE.RingGeometry(1.6,2.6,64),
//   new THREE.MeshBasicMaterial({color:defs[i].color, transparent:true, opacity:0.6,
//   blending:THREE.AdditiveBlending, depthWrite:false, side:THREE.DoubleSide}))
//   ring.rotation.x=-Math.PI/2; ring.position.set(defs[i].cx, 0.05, defs[i].cz);
//   玄朝红 0xff0000 @ redZoneTop、天庭金 0xffd700 @ blueZoneTop。
//   呼吸 dist10 L3828-3834：ring.scale.setScalar(1 + Math.sin(_zoneRingT*2) * 0.15)。
// u3d 等价：手工圆环 Mesh(XZ 平面,y≈0 贴地,法线+Up) + URP/Unlit 透明 Additive 双面 + 呼吸缩放组件。
// 现实约束：Unity 无内置 RingGeometry，故用 64 段内1.6/外2.6 顶点构造圆环；Additive 走 URP/Unlit 的
//   _Blend=2(Additive) + _SrcBlend=SrcAlpha/_DstBlend=One + _ZWrite=0 + _Surface=1(Transparent)，防 URP/Lit 加 SRP Batcher 属性不落地。
using UnityEngine;
using UnityEngine.Rendering;

namespace Liuhuo.Model
{
    public class ZoneRing : MonoBehaviour
    {
        const int SEG = 64;
        const float INNER = 1.6f, OUTER = 2.6f;
        // dist10 zone-ring 纯色：天庭金 0xffd700 / 玄朝红 0xff0000（对位 dist10 而非 BaseModelFactory.Red/Gold）
        static readonly Color TIAN_GOLD = new Color(1f, 0.84f, 0f, 0.6f);   // 0xffd700 + opacity0.6
        static readonly Color XUAN_RED  = new Color(1f, 0f, 0f, 0.6f);      // 0xff0000 + opacity0.6

        int _team;   // 0=天庭(右)/1=玄朝(左)，由 Create+SetTeam 注入后 BuildMat 定色

        /// <summary>在召唤区中心创建地面光环。</summary>
        /// <param name="team">0=天庭(右)/1=玄朝(左)</param>
        /// <param name="center">召唤区中心(如 (30,0,35)/(-30,0,35))</param>
        public static ZoneRing Create(int team, Vector3 center)
        {
            var go = new GameObject(team == 0 ? "ZoneRing_TianTing" : "ZoneRing_XuanChao");
            go.transform.position = new Vector3(center.x, 0.05f, center.z);  // 贴地 y=0.05
            var zr = go.AddComponent<ZoneRing>();
            zr._team = team;
            zr.Build();
            return zr;
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
                verts[i * 2] = new Vector3(c * INNER, 0f, s * INNER);
                verts[i * 2 + 1] = new Vector3(c * OUTER, 0f, s * OUTER);
            }
            for (int i = 0; i < SEG; i++)
            {
                int a = i * 2, b = i * 2 + 1, c = ((i + 1) % SEG) * 2, d = ((i + 1) % SEG) * 2 + 1;
                int t = i * 6;
                tris[t] = a; tris[t + 1] = c; tris[t + 2] = b;
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
            // Additive 透明(对位 dist10 AdditiveBlending opacity0.6)：_Blend=2=Additive, 双属性兜底
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
            // 呼吸：scale=1+sin(t*2)*0.15（dist10 L3828-3834）；只动 XZ(y 保持 1, 贴地)
            float k = 1f + Mathf.Sin(Time.time * 2f) * 0.15f;
            transform.localScale = new Vector3(k, 1f, k);
        }
    }
}

// HolyJudgmentFx.cs —— 圣裁视觉层（对位原版 Web fx/HolyJudgment.js 金色波前扩散，复刻 golden baseline）
// 纯视觉：不承担伤害（伤害在 UltController.ApplyHolyDamage）。全程序化，无外部 Sphere/Cylinder 几何体（mesh 代码生成）。
// 防品红（工部二 §13.2-②）：粒子走 ParticleFxPool 的 URP Particles/Unlit additive；半球冲击波 mesh 走本工程
//   自定义 URP Unlit shader「LiuhuoFX/HolyDome」（复刻原版 ShaderMaterial 片元公式）。
// 结构（对位原版 HolyJudgment.js，1:1 三件套）：
//   ① 金色光柱：实心【开口锥台 mesh】直冲天顶（CylinderGeometry(0.6,1.4,260) 对位 → 顶 r0.6 底 r1.4 高260，y=130）；
//      0.3s 淡入 → 末 0.6s 淡出，全程存在 6s（原版从来"从天而降/1.3s早停"，此前误读已删）。
//   ② 半球冲击波：程序化上半球 Mesh + LiuhuoFX/HolyDome shader（局部坐标 d=length(xz) 实时计算
//      ring 亮环 + 双频动态波纹 + 中心光斑 + 边缘遮罩 + 亮环金色增亮），scale 0.1→250（半径250→直径500m，用户拍板）ease-in(p²)。
//   ③ 屏幕闪金 overlay（对位原版 DOM rgba(255,215,0,0.55) opacity 0.55→0，跟随相机全屏 quad，additive）。
// 伤害数值：holyLifespan=6.0 / visualRadius=250(直径500m)（用户拍板；原版源码 DOME_MAX_SCALE=500→直径1000m 不采用）。
using System.Collections.Generic;
using UnityEngine;

namespace Liuhuo.FX
{
    public class HolyJudgmentFx : MonoBehaviour
    {
        private float _lifespan = 6.0f;
        private float _maxRadius = 250f;         // 用户拍板：半径250 → 直径500m（不跟原版源码 500→直径1000m）
        private float _elapsed = 0f;

        private MeshRenderer _pillar;            // ①实心锥台光柱 mesh
        private Material _pillarMat;             // 光柱材质（URP Unlit additive 金）
        private Transform _dome;                 // ②半球冲击波 mesh
        private Material _domeMat;               // 半球材质（LiuhuoFX/HolyDome 自定义 shader）
        private Transform _flash;                // ③屏幕闪金 overlay quad
        private Material _flashMat;              // 闪金材质（URP Unlit additive 金）

        private static Mesh _domeMesh;           // 共享程序化上半球 Mesh（半径1，rel. local radius d 存 position.xz）
        private static Material _domeMatShared;  // 共享半球材质（Shader.Find 一次，避免每实例泄漏）
        private float _lastDomeLog = -1f;   // s5T1 诊断: dome scale 演变采样限频
        private static Mesh _pillarMesh;         // 共享程序化开口锥台 Mesh（顶0.6底1.4 高260）
        private const float kBeamHeight = 260f;  // 光柱高度（对位原版 CylinderGeometry 高 260）
        private const float kBeamHalf = 130f;    // =kBeamHeight*0.5，上移让柱底贴地（y=130 直冲天顶，原版 position.y=130）

        public void Init(float lifespan, float maxRadius)
        {
            _lifespan = lifespan; _maxRadius = maxRadius;
            BuildVisual();
        }

        void BuildVisual()
        {
            // ① 金光柱：实心开口锥台 mesh（顶 r0.6 底 r1.4 高260），y=130 直冲天顶，全程存在 6s
            BuildPillar();

            // ② 半球冲击波：程序化上半球 Mesh + LiuhuoFX/HolyDome shader（实时片元波纹）
            BuildDome();

            // ③ 屏幕闪金 overlay（跟随相机，additive 金，渐隐）
            BuildScreenFlash();
        }

        // ① 金色光柱：实心开口锥台 mesh（原版 CylinderGeometry(0.6,1.4,260,12,1,true) 对位）
        void BuildPillar()
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) return;                                  // 防御：无 shader 则不建光柱
            if (_pillarMesh == null) _pillarMesh = MakePillarMesh();
            if (_pillarMat == null)
            {
                _pillarMat = new Material(sh);
                _pillarMat.SetFloat("_Surface", 1f);
                _pillarMat.SetFloat("_Blend", 2f);      // Additive：URP 自动 SrcAlpha,One = src.rgb*srcAlpha+dst（对位原版 AdditiveBlending）
                _pillarMat.SetFloat("_SrcBlend", 5f);   // SrcAlpha
                _pillarMat.SetFloat("_DstBlend", 1f);   // One
                _pillarMat.SetFloat("_ZWrite", 0f);
                _pillarMat.SetFloat("_Cull", 0f);       // 双面渲染（原版 side:DoubleSide）
                _pillarMat.SetColor("_BaseColor", new Color(1f, 0.84f, 0f, 1f));   // 金 #FFD700，透明度在 Update 控
            }
            var go = new GameObject("HolyPillar");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, kBeamHalf, 0f);   // 直冲天顶（y=130，柱底贴地 0..260）
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _pillarMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _pillarMat;
            _pillar = mr;
        }

        // ② 半球冲击波：程序化上半球网格 + LiuhuoFX/HolyDome 自定义 shader（局部坐标 d=length(xz) 实时波纹）
        void BuildDome()
        {
            var sh = Shader.Find("LiuhuoFX/HolyDome");       // 自定义 shader（复刻原版 ShaderMaterial 公式）
            if (sh == null) { Debug.Log("[HolyFx] ERROR dome shader NOT FOUND, dome skipped"); return; }
            Debug.Log("[HolyFx] dome shader FOUND: " + sh.name + " supported=" + sh.isSupported);
            if (_domeMesh == null) _domeMesh = MakeDomeMesh();
            if (_domeMatShared == null)
            {
                _domeMatShared = new Material(sh);
                _domeMatShared.SetColor("_BaseColor", new Color(1f, 0.84f, 0f, 1f));   // 金
                _domeMatShared.SetFloat("_Elapsed", 0f);
                _domeMatShared.SetFloat("_Fade", 0f);
            }
            _domeMat = _domeMatShared;

            var go = new GameObject("HolyDome");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.1f, 0f);   // 半球赤道贴地（对位原版 mesh.position.y=0.1）
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _domeMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _domeMat;
            go.transform.localScale = Vector3.one * 0.1f;
            _dome = go.transform;
            Debug.Log("[HolyFx] dome built verts=" + _domeMesh.vertexCount + " pos=" + transform.position + " initScale=0.1");
        }

        // ③ 屏幕闪金 overlay（对位原版 DOM rgba(255,215,0,0.55) opacity 0.55→0）。跟随相机全屏 quad，additive 金，ZTest Always 置顶。
        void BuildScreenFlash()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "HolyFlash";
            go.transform.SetParent(transform, false);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);   // 去掉 Quad 自带 MeshCollider
            _flashMat = new Material(sh);
            _flashMat.SetFloat("_Surface", 1f);
            _flashMat.SetFloat("_Blend", 0f);           // Alpha：URP 自动 SrcAlpha,OneMinusSrcAlpha（对位原版 DOM 普通半透明金）
            _flashMat.SetFloat("_ZWrite", 0f);
            _flashMat.SetFloat("_ZTest", 8f);   // CompareFunction.Always → 忽略深度恒置顶于 3D
            _flashMat.SetColor("_BaseColor", new Color(1f * 0.55f, 0.84f * 0.55f, 0f, 1f));
            go.GetComponent<MeshRenderer>().sharedMaterial = _flashMat;
            _flash = go.transform;
        }

        void OnDestroy()
        {
            // 清理本实例私有运行时材质（光柱 + 屏幕闪金），避免长局内逐次累积泄漏。
            //   _domeMat 指向共享静态 _domeMatShared，由后续实例复用，不销毁。
            if (_pillarMat != null) Destroy(_pillarMat);
            if (_flashMat != null) Destroy(_flashMat);
        }

        void Update()
        {
            _elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(_elapsed / Mathf.Max(0.1f, _lifespan));   // 0..1 归一化进度

            // ② 半球冲击波：scale 0.1→250（直径500m），ease-in(p²) 由慢至快（对位原版 L99-101）
            if (_dome != null)
            {
                float s = 0.1f + (Mathf.Max(_maxRadius, 0.1f) - 0.1f) * (p * p);
                _dome.localScale = Vector3.one * s;
                // 原版：dome.material.uniforms.uTime.value = elapsed（驱动波纹流动）+ uFade 渐隐
                if (_domeMat != null)
                {
                    _domeMat.SetFloat("_Elapsed", _elapsed);
                    float fade = Mathf.Min(1f, Mathf.Max(0f, (_elapsed - 1.6f) / 0.8f));  // 1.6s 后渐隐（原版 L107）
                    _domeMat.SetFloat("_Fade", fade);
                }
            }
            if (_dome != null && _elapsed - _lastDomeLog >= 0.5f) { _lastDomeLog = _elapsed; Debug.Log("[HolyFx] domeScale t=" + _elapsed.ToString("F2") + " scale=" + _dome.localScale.x.ToString("F2")); }

            // ① 光柱淡入/淡出：0.3s 淡入，末 0.6s 淡出（原版 L104-105）；全程存在到 _lifespan
            if (_pillarMat != null)
            {
                float fadeIn = _elapsed < 0.3f ? _elapsed / 0.3f : 1f;
                float fadeOut = _elapsed > _lifespan - 0.6f ? (_lifespan - _elapsed) / 0.6f : 1f;
                float op = fadeIn * fadeOut;
                // URP Unlit additive：用 RGB 亮度近似 opacity（additive 下 alpha 融入 RGB，故用 _BaseColor 亮度*系数 + 固定 alpha=1 保混合）
                _pillarMat.SetColor("_BaseColor", new Color(1f * Mathf.Max(0f, op), 0.84f * Mathf.Max(0f, op), 0f, 1f));
            }

            // ③ 闪金淡出 + 跟随相机（原版 L109 flash.opacity 0.55 - (elapsed/6)*0.9）
            if (_flash != null && _flashMat != null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    _flash.position = cam.transform.position + cam.transform.forward * 6f;
                    _flash.rotation = cam.transform.rotation;
                    float dist = 6f;
                    float h = cam.orthographic
                        ? cam.orthographicSize * 2f
                        : 2f * dist * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f);
                    float w = h * cam.aspect;
                    _flash.localScale = new Vector3(w * 1.15f, h * 1.15f, 1f);
                    float fk = Mathf.Max(0f, 0.55f - (_elapsed / _lifespan) * 0.9f);
                    _flashMat.SetColor("_BaseColor", new Color(1f * fk, 0.84f * fk, 0f, 1f));
                }
            }

            if (_elapsed >= _lifespan)
            {
                Object.Destroy(gameObject);
            }
        }

        // 程序化上半球 Mesh（半径1）：uv.x = 局部水平距离 d（顶=0 .. 缘=1），供 shader 里 length(positionOS.xz) 采样。
        static Mesh MakeDomeMesh()
        {
            const int segX = 32, segY = 16;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            verts.Add(new Vector3(0f, 1f, 0f)); uvs.Add(new Vector2(0f, 1f));   // 顶点（北极）
            for (int j = 1; j <= segY; j++)
            {
                float phi = (float)j / segY * Mathf.PI * 0.5f;   // 0..90°
                float y = Mathf.Cos(phi);
                float r = Mathf.Sin(phi);                         // 水平半径 0..1
                for (int i = 0; i <= segX; i++)
                {
                    float th = (float)i / segX * Mathf.PI * 2f;
                    verts.Add(new Vector3(r * Mathf.Cos(th), y, r * Mathf.Sin(th)));
                    uvs.Add(new Vector2(r, 1f));                  // uv.x = d
                }
            }
            // 顶盖三角扇
            for (int i = 0; i < segX; i++) { tris.Add(0); tris.Add(1 + i); tris.Add(2 + i); }
            // 环带四角
            for (int j = 0; j < segY - 1; j++)
            {
                int row = 1 + j * (segX + 1);
                int next = row + (segX + 1);
                for (int i = 0; i < segX; i++)
                {
                    int a = row + i, b = row + i + 1, c = next + i, d = next + i + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }

            var mesh = new Mesh();
            mesh.vertices = verts.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            // 防视锥剔除：半球会扩到 250（直径500m），网格 bounds 撑大到 2000（对位原版 mesh.frustumCulled=false 的等价做法）
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            return mesh;
        }

        // 程序化开口锥台 Mesh（对位原版 CylinderGeometry(0.6,1.4,260,12,1,true)）：顶环 r0.6、底环 r1.4、高260、绕y、不封盖。
        static Mesh MakePillarMesh()
        {
            const int seg = 12;
            const float rTop = 0.6f, rBot = 1.4f;
            const float halfH = kBeamHalf;   // 130（mesh 中心在原点，y=-130..130，游戏里 localPosition.y=130 贴地）
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a = (float)i / seg * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                // 顶环（y=+halfH）与底环（y=-halfH）
                verts.Add(new Vector3(ca * rTop, halfH, sa * rTop));
                verts.Add(new Vector3(ca * rBot, -halfH, sa * rBot));
                uvs.Add(new Vector2((float)i / seg, 1f));
                uvs.Add(new Vector2((float)i / seg, 0f));
            }
            for (int i = 0; i < seg; i++)
            {
                int n = (i + 1) % seg;
                int i0 = i * 2, i1 = i * 2 + 1, i2 = n * 2, i3 = n * 2 + 1;
                tris.Add(i0); tris.Add(i1); tris.Add(i2);   // 侧面（Unlit 不看法线，双面渲染 Cull Off）
                tris.Add(i2); tris.Add(i1); tris.Add(i3);
            }
            var mesh = new Mesh();
            mesh.vertices = verts.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 300f);   // 防视锥剔除（光柱高260）
            return mesh;
        }
    }
}

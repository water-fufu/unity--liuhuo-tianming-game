// SameDeathFx.cs —— 同死视觉层（对位 Web fx/SameDeath.js 红色激光雨，任务 P1-1 贴合 golden baseline）
// 纯视觉：不承担伤害（伤害在 UltController.ApplySameTick）。全程序化，无外部 Cube 几何体。
// 防品红（工部二 §13.2-②）：粒子走 ParticleFxPool 的 URP Particles/Unlit additive；落地涟漪 mesh 走 URP/Unlit additive。
// 结构（对位 Web SameDeath.js）：
//   ① 红色激光雨：Box 顶面连续下落的【拉长实体激光柱】——ParticleSystem renderMode=Stretch + LaserMat(激光条纹理)，
//      沿速度拉长成为细密倾盆光柱（对位 Web InstancedMesh CylinderGeometry(0.15,0.15,4.5,4) color 0xff3030 opacity0.9）。
//      为确定性下落：startSpeed=0 + velocityOverLifetime 恒定 -Y(_fallSpeed=40)，与 shape 发射方向解耦。
//   ② 落地涟漪：平躺地面【环 RingGeometry 0.3→4.0 外扩 opacity 0.8→0】（对位 Web _makeBallPool 波前外扩）。
//   ③ 血雾：暗红上行烟（对位 fx_soft_smoke）。
// 伤害数值：sameLifespan=7.0 / sameMaxLasers=80 / sameWaveEvery=0.25（S2 E2 权威值）。
using System.Collections.Generic;
using UnityEngine;

namespace Liuhuo.FX
{
    public class SameDeathFx : MonoBehaviour
    {
        private float _lifespan = 7.0f;
        private float _waveEvery = 0.25f;
        private float _elapsed = 0f;
        private float _nextWave = 0f;
        private float _fallFrom = 80f;    // FALL_FROM=80
        private float _fallSpeed = 40f;   // FALL_SPEED=40
        private ParticleSystem _rain;     // 激光雨幕（Stretch 拉长）

        // 落地涟漪池（平躺地面环，环形 Mesh + 独立材质自淡出）
        private const int kRipplePool = 96;                // ★用户拍板(2026-09-07)：涟漪每波×4 → 池容量×4(24→96) 防复用致密度不均
        private readonly List<Transform> _ripples = new List<Transform>();
        private readonly List<Material> _rippleMats = new List<Material>();
        private readonly List<float> _rippleLife = new List<float>();
        private int _rippleIdx = 0;
        private static Mesh _rippleMesh;                    // 共享 annulus（RingGeometry 内0.3 外0.5 等价）
        private static Material _rippleMatShared;           // URP/Unlit additive 红（外层淡出在实例材质的 _BaseColor）

        public void Init(float lifespan, int maxLasers, float waveEvery)
        {
            _lifespan = lifespan; _waveEvery = waveEvery;
            // maxLasers 保留参数对接 UltController（MAX_LASERS=80 在 Web 为单波上限）；Unity 端以 emission rate 实现连绵雨密度
            BuildRain();
            BuildRipples();
        }

        // 红色激光雨幕：Box 顶面，Stretch 沿速度拉长成光柱，velocityOverLifetime 恒定 -Y 下落
        void BuildRain()
        {
            var go = new GameObject("SameRain");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var m = ps.main;
            m.loop = true; m.playOnAwake = true;
            m.startLifetime = 2.2f;               // 从 FALL_FROM=80 落到地 40m/s 约 2s，留余量
            m.maxParticles = 4000;                // ★用户拍板(2026-09-07)：rate×4 后活跃粒子增多(576/s×2.2s≈1267)，default 1000 不够，扩到 4000
            m.startSpeed = 0f;                    // shape 不给初速，下落全由 velocityOverLifetime 接管（确定性）
            m.startSize = 0.35f;                  // 激光柱宽（细）；长度由 Stretch 拉伸
            m.startColor = new Color(1f, 0.188f, 0.188f, 0.9f);   // #FF3030 opacity≈0.9
            m.gravityModifier = 0f;

            var em = ps.emission;
            em.rateOverTimeMultiplier = _waveEvery <= 0f ? 1f : 144f / _waveEvery;   // ★用户拍板(2026-09-07)：伤害范围全向×2(50→100)面积×4, 保单位面积频率不变→rate×4(36→144, 每波道数×4)

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(360f, 1f, 280f);            // ★用户拍板(2026-09-07)：伤害全向×2 视觉同步×2：覆盖 ±180 × ±140（原 ±90×±70，随伤害半径 100 同步铺满）
            sh.position = new Vector3(0f, _fallFrom, 0f);

            // 恒定 -Y 下落（World 空间，与 shape 发射方向解耦，保证 Stretch 沿 -Y 拉长）
            var vol = ps.velocityOverLifetime;
            vol.enabled = true;
            vol.space = ParticleSystemSimulationSpace.World;
            vol.x = 0f; vol.y = -_fallSpeed; vol.z = 0f;

            var r = go.GetComponent<ParticleSystemRenderer>();
            if (r == null) r = go.AddComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;   // 沿速度拉长成实体激光柱
            r.velocityScale = 0.1f;                            // 40*0.1≈4.0m + size 0.35 ≈4.35m（对位 Cylinder 高4.5）
            r.lengthScale = 1f;
            r.sharedMaterial = ParticleFxPool.LaserMat();      // URP Particles/Unlit additive + 激光条纹理（防品红）
            ps.Play();
            _rain = ps;
            Debug.Log($"[SameProbe] 粒子雨密度 发射率rate={em.rateOverTimeMultiplier:F2} 发射面scale={sh.scale} maxParticles={m.maxParticles} (范围×2后 面积×4→rate×4 36→144 保单位面积频率)");
        }

        // 落地涟漪池：平躺地面 annulus 环，独立材质自淡出
        void BuildRipples()
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) return;                            // 防御：无 shader 则不建涟漪
            if (_rippleMesh == null) _rippleMesh = MakeRippleMesh();
            if (_rippleMatShared == null)
            {
                _rippleMatShared = new Material(sh);
                _rippleMatShared.SetFloat("_Surface", 1f);
                _rippleMatShared.SetFloat("_Blend", 0f);
                _rippleMatShared.SetFloat("_SrcBlend", 1f);
                _rippleMatShared.SetFloat("_DstBlend", 1f);
                _rippleMatShared.SetFloat("_ZWrite", 0f);
                _rippleMatShared.SetColor("_BaseColor", new Color(1f, 0.25f, 0.25f, 0.8f));   // 红涟漪
            }

            for (int i = 0; i < kRipplePool; i++)
            {
                var go = new GameObject("SameRipple");
                go.transform.SetParent(transform, false);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = _rippleMesh;
                var mr = go.AddComponent<MeshRenderer>();
                var mat = new Material(_rippleMatShared);      // 每环独立材质（自淡出 opacity 各不同）
                mr.sharedMaterial = mat;
                go.SetActive(false);
                _ripples.Add(go.transform);
                _rippleMats.Add(mat);
                _rippleLife.Add(-1f);
            }
        }

        void OnDestroy()
        {
            // 清理每涟漪独立实例材质（自淡出），避免长局内逐次累积泄漏。
            //   _rippleMatShared 为共享静态，后续实例复用，不销毁。
            for (int i = 0; i < _rippleMats.Count; i++)
                if (_rippleMats[i] != null) Destroy(_rippleMats[i]);
            _rippleMats.Clear();
        }

        void Update()
        {
            _elapsed += Time.deltaTime;

            // 波次调度：每 _waveEvery 一波，连播至 LIFESPAN（波前 16-23 道；涟漪+血雾落地）
            if (_elapsed <= _lifespan && _elapsed >= _nextWave)
            {
                _nextWave = _elapsed + _waveEvery;
                SpawnRipples();
            }

            // 涟漪自淡出（0.3→4.0 外扩 + RGB 亮度 0.8→0；additive 用亮度淡出）
            for (int i = 0; i < _ripples.Count; i++)
            {
                if (_rippleLife[i] < 0f) continue;
                _rippleLife[i] += Time.deltaTime;
                float p = Mathf.Min(1f, _rippleLife[i] / 0.6f);
                var t = _ripples[i];
                if (t == null) continue;
                t.localScale = Vector3.one * (0.3f + (4.0f - 0.3f) * p);   // 涟漪放大上限 4.0（对位 Web v95 需求4a）
                float b = 0.8f * (1f - p);
                _rippleMats[i].SetColor("_BaseColor", new Color(1f * b, 0.25f * b, 0.25f * b, 1f));
                if (p >= 1f) { _rippleLife[i] = -1f; t.gameObject.SetActive(false); }
            }

            if (_rain != null && _elapsed >= _lifespan)
            {
                _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                Object.Destroy(gameObject);
            }
        }

        // 落地涟漪 + 血雾（对位 Web L124-134 落地爆炸球 + v98 M5 血雾）。每波 5-8 个涟漪（落地更密）。
        void SpawnRipples()
        {
            int n = 20 + Random.Range(0, 16);   // ★用户拍板(2026-09-07)：范围×4面积, 涟漪每波×4(5-8→20-35) 保单位面积密度
            for (int k = 0; k < n; k++)
            {
                Vector3 pos = new Vector3(Random.Range(-180f, 180f), 0.05f, Random.Range(-140f, 140f));   // ★用户拍板(2026-09-07)：涟漪范围随视觉×2 → ±180×±140（原 ±90×±70）
                SpawnRipple(pos);
                // 血雾：暗红 #8B0000 上行烟（对位 fx_soft_smoke）
                if (Random.value < 0.3f)
                    ParticleFxPool.EmitBurst(transform.position + pos + Vector3.up * 0.2f,
                        new Color(0.55f, 0.05f, 0.08f, 0.7f), 8, 1.5f, 0.9f, 1.2f);
            }
        }

        // 从池取出一个涟漪环放置于 pos（round-robin 复用）
        void SpawnRipple(Vector3 pos)
        {
            if (_ripples.Count == 0 || _rippleMats.Count == 0) return;
            int i = _rippleIdx; _rippleIdx = (_rippleIdx + 1) % _ripples.Count;
            var t = _ripples[i];
            if (t == null) return;
            t.gameObject.SetActive(true);
            t.localPosition = new Vector3(pos.x, 0.05f, pos.z);   // 涟漪贴地（fx 原点≈地面，本地 y 只需 +0.05）
            t.localScale = Vector3.one * 0.3f;
            _rippleLife[i] = 0f;
        }

        // 共享 annulus Mesh（RingGeometry 内0.3 外0.5 等价，平躺 XZ 面，环面朝 +Y）
        static Mesh MakeRippleMesh()
        {
            const float inner = 0.3f, outer = 0.5f;
            const int seg = 32;
            var verts = new Vector3[seg * 2];
            var uv = new Vector2[seg * 2];
            var tris = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a = (float)i / seg * Mathf.PI * 2f;
                verts[i] = new Vector3(Mathf.Cos(a) * outer, 0f, Mathf.Sin(a) * outer);
                verts[seg + i] = new Vector3(Mathf.Cos(a) * inner, 0f, Mathf.Sin(a) * inner);
                uv[i] = new Vector2((float)i / seg, 1f);
                uv[seg + i] = new Vector2((float)i / seg, 0f);
            }
            for (int i = 0; i < seg; i++)
            {
                int i2 = (i + 1) % seg;
                tris.Add(i); tris.Add(seg + i); tris.Add(i2);
                tris.Add(i2); tris.Add(seg + i); tris.Add(seg + i2);
            }
            var mesh = new Mesh();
            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

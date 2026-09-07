// HitFx.cs : programmatic hit-effect layer (matches original three.js hit.json), built the SAME way
// as the ult Fx (HolyJudgmentFx/SameDeathFx) -- fully procedural, NO external PNG, NO prefab dependency.
// Why no black frame: soft textures are generated in code with feathered alpha (SoftTex/StreakTex/RingTex),
// and materials are URP Unlit additive -- identical to the ults that imported cleanly.
//
// DIFFERENCE from the ult chain (the one thing that would silently break):
//   The "black streaks" MUST use a NORMAL-blend (SrcAlpha, OneMinusSrcAlpha) DARK material.
//   Under additive (One, One), black adds ZERO brightness -> the streaks would be invisible.
//   So HitFx builds its own dark normal-blend material for the black lines, kept separate from the
//   additive pool material used for the bright parts.
//
// Structure (per user requirements):
//   1) center glowing body   : billboard soft-circle, 0 -> peak -> 0 pulse
//   2) black streaks going up: dark STREAK particles, upward velocity, normal blend
//   3) lots of debris        : sphere burst flying down/out (gravity), additive bright
//   4) outer BIG ring        : camera-facing quad, RingTex, color = _main, expands outward
//   5) outer SMALL ring      : camera-facing quad, RingTex, color = _light (lighter), smaller radius
//
// Colors (per confirmed mapping): XuanChao hit = bright gold (#FFD700), TianTing hit = vivid deep red (#FF3030).
using System.Collections.Generic;
using UnityEngine;

namespace Liuhuo.FX
{
    public class HitFx : MonoBehaviour
    {
        private float _lifespan = 0.7f;      // hit burst ~0.6-0.7s
        private float _elapsed = 0f;
        private Color _main;                 // glowing body / big ring color
        private Color _light;                // small ring lighter color

        private ParticleSystem _core;        // 1) center glow (billboard soft circle)
        private ParticleSystem _streaks;     // 2) black streaks up (normal blend)
        private Transform _ringBig;          // 4) big ring quad
        private Material _ringBigMat;
        private Transform _ringSmall;        // 5) small ring quad
        private Material _ringSmallMat;
        private static Mesh _annulusMesh;    // shared camera-facing hollow ring (annulus) mesh

        public void Init(Color main, Color light)
        {
            _main = main; _light = light;
            Build();
        }

        void Build()
        {
            SpawnCore();
            SpawnStreaks();
            SpawnDebris();
            SpawnRings();
        }

        // 1) center glowing body : a single billboard soft-circle particle, size pulses then fades
        void SpawnCore()
        {
            var go = new GameObject("HitCore");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 1f;
            var ps = go.AddComponent<ParticleSystem>();
            var m = ps.main;
            m.loop = false; m.playOnAwake = true;
            m.startLifetime = _lifespan;
            m.startSpeed = 0f;
            m.startSize = 2.2f;
            m.startColor = new Color(_main.r, _main.g, _main.b, 1f);
            m.gravityModifier = 0f;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.05f;
            // size 0 -> peak -> 0 (pulse)
            var so = ps.sizeOverLifetime; so.enabled = true;
            var sk = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0f));
            so.size = new ParticleSystem.MinMaxCurve(1f, sk);
            // alpha fade
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(_main, 0f), new GradientColorKey(_main, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (r == null) r = go.AddComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = ParticleFxPool.Mat();
            ps.Play();
            _core = ps;
        }

        // 2) black streaks going up : NORMAL blend dark material; additive would make black invisible
        void SpawnStreaks()
        {
            var go = new GameObject("HitStreaks");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 1f;
            var ps = go.AddComponent<ParticleSystem>();
            var m = ps.main;
            m.loop = false; m.playOnAwake = true;
            m.startLifetime = 0.55f;
            m.startSpeed = 7f;
            m.startSize = 0.16f;
            m.startColor = new Color(0.02f, 0.02f, 0.03f, 0.9f);   // near-black
            m.gravityModifier = 0.2f;                              // slight drop-back
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)6) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 55f; sh.radius = 0.25f;
            // upward velocity (additive to initial burst so streaks rise)
            var vol = ps.velocityOverLifetime; vol.enabled = true;
            vol.space = ParticleSystemSimulationSpace.World;
            vol.y = 6f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (r == null) r = go.AddComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.35f;
            r.lengthScale = 1f;
            r.sharedMaterial = DarkStreakMat();
            ps.Play();
            _streaks = ps;
        }

        // 3) lots of debris flying down/out : bright additive sphere burst with gravity
        void SpawnDebris()
        {
            ParticleFxPool.EmitBurst(transform.position + Vector3.up * 1f,
                _main, 40, 9f, 0.35f, 0.7f);
            // secondary brighter flecks
            ParticleFxPool.EmitBurst(transform.position + Vector3.up * 1.2f,
                _light, 20, 12f, 0.22f, 0.55f);
        }

        // 4)+5) two expanding rings (hollow annulus, camera-facing, solid additive), big=_main / small=_light
        //   ★ 环形必须用空心环带几何 + 纯色 additive；绝不能用 quad + RingTex —— RingTex 是圣裁半球专用的
        //     一维 (uv.x=d) 亮度纹理，铺到平面 quad 上被拉伸成竖直渐变带，不是光环。（用户"双光环没出现"的根因。）
        void SpawnRings()
        {
            if (_annulusMesh == null) _annulusMesh = MakeAnnulusMesh();
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) return;

            _ringBigMat = RingMat(sh, _main);
            _ringBig = MakeRing("HitRingBig", _ringBigMat, 1f);

            _ringSmallMat = RingMat(sh, _light);
            _ringSmall = MakeRing("HitRingSmall", _ringSmallMat, 0.6f);
        }

        Transform MakeRing(string name, Material mat, float startScale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 1f;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _annulusMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            go.transform.localScale = Vector3.one * startScale;
            return go.transform;
        }

        Material RingMat(Shader sh, Color c)
        {
            var mat = new Material(sh);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", 1f);   // One
            mat.SetFloat("_DstBlend", 1f);   // One -> additive
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);       // Cull Off：双面渲染，无视绕序必可见
            mat.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 1f));
            // 不用 RingTex：纯色 additive 环直接用 _BaseColor，URP/Unlit 默认 _BaseMap=白 1x1，避免拉伸伪影。
            return mat;
        }

        // unique normal-blend DARK material for the black streaks (additive would erase black)
        static Material _darkMat;
        Material DarkStreakMat()
        {
            if (_darkMat != null) return _darkMat;
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) return null;
            _darkMat = new Material(sh);
            _darkMat.SetFloat("_Surface", 1f);
            _darkMat.SetFloat("_Blend", 0f);
            _darkMat.SetFloat("_SrcBlend", 5f);   // SrcAlpha
            _darkMat.SetFloat("_DstBlend", 10f);  // OneMinusSrcAlpha -> normal blend
            _darkMat.SetFloat("_ZWrite", 0f);
            _darkMat.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.03f, 0.9f));
            _darkMat.SetTexture("_BaseMap", ParticleFxPool.StreakTex());
            _darkMat.SetTexture("_MainTex", ParticleFxPool.StreakTex());
            return _darkMat;
        }

        void Update()
        {
            _elapsed += Time.deltaTime;

            // rings expand outward + fade
            float p = Mathf.Min(1f, _elapsed / _lifespan);
            if (_ringBig != null)
            {
                float s = 1f + (7f - 1f) * (p * p);      // ease-in
                _ringBig.localScale = new Vector3(s, s, 1f);
                FaceCamera(_ringBig);
            }
            if (_ringSmall != null)
            {
                float s = 0.6f + (4.5f - 0.6f) * (p * p); // smaller max
                _ringSmall.localScale = new Vector3(s, s, 1f);
                FaceCamera(_ringSmall);
            }

            // fade ring brightness out near end (additive fades by RGB)
            if (_ringBigMat != null)
            {
                float k = 1f - Mathf.Min(1f, _elapsed / _lifespan);
                _ringBigMat.SetColor("_BaseColor", new Color(_main.r * k, _main.g * k, _main.b * k, 1f));
            }
            if (_ringSmallMat != null)
            {
                float k = 1f - Mathf.Min(1f, _elapsed / _lifespan);
                _ringSmallMat.SetColor("_BaseColor", new Color(_light.r * k, _light.g * k, _light.b * k, 1f));
            }

            if (_elapsed >= _lifespan)
            {
                if (_core != null) _core.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (_streaks != null) _streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                Object.Destroy(gameObject);
            }
        }

        void FaceCamera(Transform t)
        {
            var cam = Camera.main;
            if (cam == null) return;
            t.rotation = cam.transform.rotation;
        }

        void OnDestroy()
        {
            // clean per-instance runtime ring materials (avoid leak over many hits)
            if (_ringBigMat != null) Destroy(_ringBigMat);
            if (_ringSmallMat != null) Destroy(_ringSmallMat);
        }

        // 程序化空心环带 Mesh（XY 平面，法线 +Z，配 Cull Off 双面渲染）：外径 1、内径 0.62 → 环带粗细可控。
        //   受击"双光环"用：两枚同轴环，大环=_main 色、小环=_light 色，外扩至 7 / 4.5，FaceCamera 始终朝向相机。
        static Mesh MakeAnnulusMesh(float inner = 0.62f, float outer = 1f, int seg = 40)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a = (float)i / seg * Mathf.PI * 2f;
                float cx = Mathf.Cos(a), cy = Mathf.Sin(a);
                verts.Add(new Vector3(cx * inner, cy * inner, 0f));
                verts.Add(new Vector3(cx * outer, cy * outer, 0f));
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
            }
            for (int i = 0; i < seg; i++)
            {
                int n = (i + 1) % seg;
                int i0 = i * 2, i1 = i * 2 + 1, i2 = n * 2, i3 = n * 2 + 1;
                tris.Add(i0); tris.Add(i1); tris.Add(i2);   // 法线 +Z（Unlit 不看法线，绕序仅为背面剔除正确）
                tris.Add(i2); tris.Add(i1); tris.Add(i3);
            }
            var mesh = new Mesh();
            mesh.vertices = verts.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

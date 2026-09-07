// ParticleFxPool.cs —— 大招/受击粒子系统对象池（程序化创建，复 GameBootstrap.ObjectPool<T>）
// 目的：圣裁/同死大招粒子化 —— burst 型粒子层全部走 ObjectPool<ParticleSystem>，防每帧
//       Instantiate/Destroy 的 GC 抖动（E1/E2 判分 "additive+池化"）。
// 防品红（工部二 §13.2-②）：材质用 "Universal Render Pipeline/Particles/Unlit"（URP 原生粒子 shader，
//       Blend[_SrcBlend][_DstBlend] 状态命令控制混合，非 Built-in "Particles/Standard Unlit" → URP 下不会洋红）。
// 共性根因修复(任务 P1-1)：
//     原只用白色无纹理 additive 材质 → 粒子全渲染成白硬块。现改为给 additive 材质挂一张【程序生成软圆形发光纹理】
//     (128x128 径向渐变 falloff) 作 _BaseMap/_MainTex，颜色仍走 startColor，但有了软掩 → 金/红发光质感出来。
//     颜色继续 startColor，单材质服务所有阵营色。
// 新纹理：SoftTex(软圆) / StreakTex(激光条) / RingTex(波纹环)。激光另建 LaserMat()（URP Particles/Unlit additive + 激光条纹理）。
using System.Collections;
using UnityEngine;
using Liuhuo.Core;

namespace Liuhuo.FX
{
    public static class ParticleFxPool
    {
        static ObjectPool<ParticleSystem> _burstPool;   // 球形爆发
        static ObjectPool<ParticleSystem> _ringPool;    // 环形波前
        static Material _additiveMat;                   // 共享软圆 additive（圣裁/同死/受击粒子共用）
        static Material _laserMat;                      // 激光条 additive（同死激光柱用）
        static Texture2D _softTex;                      // 软圆发光纹理（径向 falloff）
        static Texture2D _streakTex;                    // 激光条纹理（长亮条，Stretch 拉伸）
        static Texture2D _ringTex;                      // 波纹环纹理（圣裁半球冲击波用）
        static Transform _parent;
        static ParticleFxRunner _runner;

        // 供视觉层取用的共享 additive 材质（圣裁/同死/受击粒子共用；防品红：URP Particles/Unlit）
        public static Material Mat()
        {
            EnsurePools();
            return _additiveMat;
        }

        // 供同死激光柱使用的激光条材质（URP Particles/Unlit additive + 长亮条纹理，Stretch 拉长）
        public static Material LaserMat()
        {
            EnsurePools();
            if (_laserMat != null) return _laserMat;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            _laserMat = new Material(sh);
            _laserMat.SetFloat("_Surface", 1f);
            _laserMat.SetFloat("_Blend", 0f);
            _laserMat.SetFloat("_SrcBlend", 1f);   // One
            _laserMat.SetFloat("_DstBlend", 1f);   // One -> additive
            _laserMat.SetFloat("_ZWrite", 0f);
            _laserMat.SetColor("_BaseColor", Color.white);
            _laserMat.SetTexture("_BaseMap", StreakTex());
            _laserMat.SetTexture("_MainTex", StreakTex());
            return _laserMat;
        }

        // 纹理访问器（软圆 / 激光条 / 波纹环），供各视觉层取用，避免重复生成
        public static Texture2D SoftTex()  { EnsureTex(); return _softTex; }
        public static Texture2D StreakTex() { EnsureTex(); return _streakTex; }
        public static Texture2D RingTex()  { EnsureTex(); return _ringTex; }

        // 单例白色 additive URP 粒子材质（防品红：URP Particles/Unlit，Src=One/Dst=One）+ 软圆纹理
        static Material EnsureMat()
        {
            if (_additiveMat != null) return _additiveMat;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null)
            {
                // 兜底：找不到粒子 shader 时用 URP/Unlit（本机已证实 find 得到），仍不品红
                sh = Shader.Find("Universal Render Pipeline/Unlit");
            }
            _additiveMat = new Material(sh);
            _additiveMat.SetFloat("_Surface", 1f);    // Transparent
            _additiveMat.SetFloat("_Blend", 0f);
            _additiveMat.SetFloat("_SrcBlend", 1f);   // One
            _additiveMat.SetFloat("_DstBlend", 1f);   // One  -> additive
            _additiveMat.SetFloat("_ZWrite", 0f);
            _additiveMat.SetColor("_BaseColor", Color.white);
            // ★共性根因修复：补软圆发光纹理（径向 falloff），粒子从"白硬块"变"软辉光"。
            //   URP 用 _BaseMap；双写 _MainTex 兼容 legacy 粒子 shader 属性名。颜色沿途由 startColor 乘入。
            if (SoftTex() != null)
            {
                _additiveMat.SetTexture("_BaseMap", _softTex);
                _additiveMat.SetTexture("_MainTex", _softTex);
            }
            return _additiveMat;
        }

        // 惰性生成三种运行时纹理（软圆 / 激光条 / 波纹环）
        static void EnsureTex()
        {
            if (_softTex != null) return;
            _softTex = MakeSoftCircle(128);
            _streakTex = MakeStreak(64, 256);
            _ringTex = MakeRing(256);
        }

        // GLSL 语义的 smoothstep（edge0/edge1 为边界，返回归一化 0..1）。
        //   Unity 的 Mathf.SmoothStep(from,to,t) 返回 [from,to] 区间值（非归一化），无法直接复刻 GLSL smoothstep 的
        //   "0~1 边沿"效果（Web 纹理/着色器使用 GLSL smoothstep）。此助手按 GLSL 定义复刻。
        static float Ss(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            t = t * t * (3f - 2f * t);
            return t;
        }

        // 软圆发光纹理：中心亮 → 边缘暗（径向高斯 falloff），additive(One,One) 用 RGB 亮度做软掩
        static Texture2D MakeSoftCircle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size - 0.5f;
                    float dy = (y + 0.5f) / size - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;   // 0 中心 → 1 圆边（角 1.414 截断）
                    float a = Mathf.Clamp01(Mathf.Exp(-r * r * 4f));
                    tex.SetPixel(x, y, new Color(a, a, a, a));
                }
            }
            tex.Apply(true);
            return tex;
        }

        // 激光条纹理：长亮条（Y 全长，X 细软边），供 Stretch 拉长成实体激光柱
        static Texture2D MakeStreak(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < h; y++)
            {
                float py = (y + 0.5f) / h;
                float gy = Ss(0f, 0.08f, py) * (1f - Ss(0.92f, 1f, py));   // 端部微圆（GLSL smoothstep）
                for (int x = 0; x < w; x++)
                {
                    float px = (x + 0.5f) / w;
                    float gx = 1f - Ss(0f, 0.5f, Mathf.Abs(px - 0.5f) * 2f);           // 横向软边
                    float a = Mathf.Clamp01(gx * gy);
                    tex.SetPixel(x, y, new Color(a, a, a, a));
                }
            }
            tex.Apply(true);
            return tex;
        }

        // 波纹环纹理（圣裁半球冲击波）：uv.x=d 局部半径 (0顶=0 .. 1缘)，编码【亮环 DOME_RING + 双频波纹 + 中心光斑 + 边缘淡出】
        //   亮度按 RGB 编码；圣裁 dome 材质 _BaseColor=金，乘此亮度 → 金波前。
        static Texture2D MakeRing(int size)
        {
            var tex = new Texture2D(size, 2, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int x = 0; x < size; x++)
            {
                float d = (x + 0.5f) / size;                         // 0..1（对位 Web ShaderMaterial d=length(vPos.xz)/1）
                float ring = 1f - Ss(0f, 0.03f, Mathf.Abs(d - 0.5f));          // DOME_RING 亮环（GLSL smoothstep）
                float ripple = 0.5f + 0.5f * Mathf.Sin(d * 30f) * 0.6f + 0.5f * Mathf.Sin(d * 55f) * 0.4f;  // 双频波纹
                float center = Ss(0.6f, 0f, d) * 0.35f;                        // 中心光斑（GLSL smoothstep 反边）
                float edge = Ss(0f, 0.02f, d) * (1f - Ss(0.85f, 1f, d));      // 边缘淡出
                float a = Mathf.Clamp01((ring * 1.8f + ripple * 0.8f + center) * edge);
                var c = new Color(a, a, a, a);
                tex.SetPixel(x, 0, c);
                tex.SetPixel(x, 1, c);
            }
            tex.Apply(true);
            return tex;
        }

        // 惰性建池（首次触发即预暖，免 Inspector/场景预挂）
        static void EnsurePools()
        {
            if (_burstPool != null) return;
            if (_parent == null)
            {
                var go = new GameObject("ParticleFxPool");
                _parent = go.transform;
                _runner = go.AddComponent<ParticleFxRunner>();
            }
            EnsureMat();
            _burstPool = new ObjectPool<ParticleSystem>(MakeBurstTemplate(), 3, _parent);
            _ringPool = new ObjectPool<ParticleSystem>(MakeRingTemplate(), 3, _parent);
        }

        // 球形爆发模板(Spawn shape, 一次性 burst, Billboard)
        static ParticleSystem MakeBurstTemplate()
        {
            var go = new GameObject("BurstTpl");
            var ps = go.AddComponent<ParticleSystem>();
            var m = ps.main;
            m.loop = false; m.playOnAwake = false;
            m.startLifetime = 1.2f; m.startSpeed = 6f; m.startSize = 0.8f; m.startColor = Color.white;
            m.gravityModifier = 0f;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)60) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.4f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (r == null) r = go.AddComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = _additiveMat;
            go.SetActive(false);
            return ps;
        }

        // 环形波前模板(Circle shape, 径向外扩, 颜色渐变 additive)
        static ParticleSystem MakeRingTemplate()
        {
            var go = new GameObject("RingTpl");
            var ps = go.AddComponent<ParticleSystem>();
            var m = ps.main;
            m.loop = false; m.playOnAwake = false;
            m.startLifetime = 0.8f; m.startSpeed = 9f; m.startSize = 1.2f; m.startColor = Color.white;
            m.gravityModifier = 0f;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)80) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.5f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (r == null) r = go.AddComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = _additiveMat;
            go.SetActive(false);
            return ps;
        }

        // 球形爆发（对照 DeathFXManager.BurstPuff，但走池化）
        public static void EmitBurst(Vector3 pos, Color color, int count, float speed, float size, float life)
        {
            EnsurePools();
            var ps = _burstPool.Get(pos, Quaternion.identity);
            var m = ps.main; m.startLifetime = life; m.startSpeed = speed; m.startSize = size; m.startColor = color;
            var em = ps.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.4f;
            ps.Play();
            RunRecycle(ps, life);
        }

        // 环形波前（对照 DeathFXManager.RingFx，但走池化）
        public static void EmitRing(Vector3 pos, Color fromColor, Color toColor, float life)
        {
            EnsurePools();
            var ps = _ringPool.Get(pos, Quaternion.identity);
            var m = ps.main; m.startLifetime = life; m.startColor = fromColor;
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(fromColor, 0f), new GradientColorKey(toColor, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            ps.Play();
            RunRecycle(ps, life);
        }

        // 播完自动回池（协程：等待 isPlaying 结束）
        static void RunRecycle(ParticleSystem ps, float life)
        {
            if (_runner == null) EnsurePools();
            if (_runner == null) { ReleaseDirect(ps, life); return; }
            _runner.StartCoroutine(RecycleRoutine(ps, life));
        }

        static void ReleaseDirect(ParticleSystem ps, float life)
        {
            if (ps == null) return;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.gameObject.SetActive(false);
            var sh = ps.shape;
            if (sh.shapeType == ParticleSystemShapeType.Circle) _ringPool.Release(ps);
            else _burstPool.Release(ps);
        }

        static IEnumerator RecycleRoutine(ParticleSystem ps, float life)
        {
            float t = 0f;
            while (t < life + 0.6f && ps != null && ps.isPlaying)
            {
                t += Time.deltaTime;
                yield return null;
            }
            ReleaseDirect(ps, life);
        }
    }

    // 池回收协程宿主（挂 Pool 父节点上，免各视觉类各开协程）
    public class ParticleFxRunner : MonoBehaviour { }
}

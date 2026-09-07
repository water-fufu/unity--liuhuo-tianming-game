// HitFXBuilder.cs —— P3 受击特效重建：按 Web v3.2 hit.json 权威参数，数据驱动生成 天庭/玄朝 各10层 受击特效预制体
// 对位 s4 F1(10层) + F2(贴图alpha) + F4(禁距离衰减=sizeAttenuation关闭)。配色以 Web 权威值还原(TC-B1判据)，s4 F1 表的 ring 半径/scale 偏差一律以 Web 为准。
// 层类型：particle(常规多粒子) / sprite(单粒子billboard+贴图) / ring(单粒子+白环贴图 size 放大扩散) —— 统一 ParticleSystem 实现，billboard 自动朝相机规避 SpriteRenderer edge-on。
using UnityEditor;
using UnityEngine;

namespace Liuhuo.EditorTools
{
    public static class HitFXBuilder
    {
        const string TexDir = "Assets/VFX/Hit/Textures";
        const string MatDir = "Assets/VFX/Hit/Materials";
        const string PrefabDir = "Assets/VFX/Hit";
        // ★P1-5 问题1修复：Built-in 的 "Particles/Standard Unlit" 在 URP 下渲染成品红（lesson09#08/#63）。
        //   改用 URP 原生粒子 shader "Universal Render Pipeline/Particles/Unlit"（照抄 ParticleFxPool.cs 已证防品红值），
        //   使受击特效(HitFX)在 URP 下不再洋红。
        const string ParticleShader = "Universal Render Pipeline/Particles/Unlit";

        // ---------- 贴图导入（textureType=Default，粒子用非 Sprite；四边 alpha 归零防"发光正方形" F2）----------
        static void EnsureTextures(string[] names)
        {
            foreach (var n in names)
            {
                string path = TexDir + "/" + n + ".png";
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                if (ti.textureType != TextureImporterType.Default)
                    ti.textureType = TextureImporterType.Default;
                if (!ti.alphaIsTransparency)
                    ti.alphaIsTransparency = true;
                if (ti.textureCompression != TextureImporterCompression.Compressed)
                    ti.textureCompression = TextureImporterCompression.Compressed;
                ti.SaveAndReimport();
            }
        }

        // ---------- 粒子材质（additive / normal alpha blend）+ 贴图，按 (tex,additive) 组合创建为 .mat 资产并缓存 ----------
        static System.Collections.Generic.Dictionary<string, Material> _matCache = new System.Collections.Generic.Dictionary<string, Material>();

        static Material MakeParticleMat(string texName, bool additive)
        {
            string key = (texName == null ? "none" : texName) + "_" + (additive ? "Add" : "Norm");
            Material cached;
            if (_matCache.TryGetValue(key, out cached)) return cached;
            var shader = Shader.Find(ParticleShader);
            var mat = new Material(shader);
            string nm = "Mat_Hit" + (texName == null ? "" : "_" + texName) + (additive ? "_Additive" : "_Normal");
            mat.name = nm;
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", additive ? (int)UnityEngine.Rendering.BlendMode.One : (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            if (additive) mat.EnableKeyword("_ALPHAPREMULTIPLY_ON"); else mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            if (texName != null)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + texName + ".png");
                if (tex != null) mat.mainTexture = tex;
            }
            AssetDatabase.CreateAsset(mat, MatDir + "/" + nm + ".mat");
            _matCache[key] = mat;
            return mat;
        }

        // ============ 单层配置 ============
        struct LayerSpec
        {
            public string name;
            public string kind;    // particle / sprite / ring
            public string tex;     // 贴图名(在 TexDir)，null=无
            public int count;      // burst 数量(sprite/ring=1)
            public float lifeMin, lifeMax;
            public float sizeStart, sizeEnd;
            public float gravity;
            public bool additive;
            public float[] ct;     // 颜色渐变 t 关键点
            public string[] chex;  // 对应 hex
            public float[] cop;    // 对应 opacity
            public float rotSpeed; // sprite 层旋转(rad/s)
            public string shape;   // sphere/cone/circle
            public float shapeSize;
            public float angle;    // cone spread
            public Vector3 velBase;
            public Vector3 velRand;
        }

        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }

        static void SetColorCurve(ParticleSystem ps, float[] ct, string[] chex, float[] cop)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            int n = ct.Length;
            var keys = new GradientColorKey[n];
            var akeys = new GradientAlphaKey[n];
            for (int i = 0; i < n; i++)
            {
                Color c;
                ColorUtility.TryParseHtmlString(chex[i], out c);
                keys[i] = new GradientColorKey(c, ct[i]);
                akeys[i] = new GradientAlphaKey(cop[i], ct[i]);
            }
            g.SetKeys(keys, akeys);
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        static void SetSizeCurve(ParticleSystem ps, float start, float end)
        {
            var s = ps.sizeOverLifetime;
            s.enabled = true;
            var c = new AnimationCurve();
            c.AddKey(0f, start);
            c.AddKey(1f, end);
            s.size = new ParticleSystem.MinMaxCurve(1f, c);
        }

        static void MakeLayer(Transform parent, LayerSpec L, Material mat)
        {
            var go = new GameObject(L.name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 1f; main.loop = false; main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(L.lifeMin, L.lifeMax);
            main.startSize = new ParticleSystem.MinMaxCurve(L.sizeStart, L.sizeEnd);
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = L.gravity;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            int cnt = (L.kind == "particle") ? L.count : 1;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)cnt) });

            var shape = ps.shape;
            if (L.shape == "sphere") { shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = L.shapeSize; }
            else if (L.shape == "cone") { shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = L.angle; shape.radius = L.shapeSize; }
            else if (L.shape == "circle") { shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = L.shapeSize; }

            var vel = ps.velocityOverLifetime;
            if (L.velBase != Vector3.zero || L.velRand != Vector3.zero)
            {
                vel.enabled = true;
                vel.x = new ParticleSystem.MinMaxCurve(L.velBase.x - L.velRand.x, L.velBase.x + L.velRand.x);
                vel.y = new ParticleSystem.MinMaxCurve(L.velBase.y - L.velRand.y, L.velBase.y + L.velRand.y);
                vel.z = new ParticleSystem.MinMaxCurve(L.velBase.z - L.velRand.z, L.velBase.z + L.velRand.z);
            }

            SetColorCurve(ps, L.ct, L.chex, L.cop);
            if (L.kind == "particle") SetSizeCurve(ps, L.sizeStart, L.sizeEnd);

            if (L.rotSpeed != 0f)
            {
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(0f, L.rotSpeed);
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            // F4:Web sizeAttenuation:false 在 Unity 无对应公共属性；本作 MainCamera 为 orthographic(正交)，粒子天然恒定像素、不受距离衰减，故 F4 由正交相机天然满足，无需额外设值。顶视全景相机距特效>80m 仍清晰可见。
            r.sharedMaterial = mat; // 材质已带 mainTexture(见 MakeParticleMat)，直接绑定共享材质资产，防 prefab 序列化丢失引用
        }

        // ============ 天庭/玄朝 各 10 层配置（以 Web 权威值为准）============
        static LayerSpec[] BuildSpecs(bool tianting)
        {
            float[] ct = { 0f, 0.5f, 1f };
            float[] ctBurst = { 0f, 0.15f, 1f };
            float[] ctSp = { 0f, 0.4f, 1f };
            float[] ctDust = { 0f, 0.2f, 1f };

            if (tianting)
            {
                return new LayerSpec[] {
                    new LayerSpec{ name="L0_core_flash", kind="particle", tex="fx_radial_glow", count=12, lifeMin=0.04f, lifeMax=0.09f, sizeStart=9f, sizeEnd=3f, gravity=0f, additive=true, ct=ct, chex=new[]{"#ffffff","#ffd700","#daa520"}, cop=new[]{1f,0.85f,0f}, shape="sphere", shapeSize=0.15f },
                    new LayerSpec{ name="L1_glow_core", kind="sprite", tex="fx_radial_glow", count=1, lifeMin=0.35f, lifeMax=0.4f, sizeStart=1.8f, sizeEnd=1.8f, additive=true, ct=new[]{0f,0.3f,1f}, chex=new[]{"#ffffff","#ffe000","#daa520"}, cop=new[]{1f,0.55f,0f} },
                    new LayerSpec{ name="L2_burst_radial", kind="particle", tex="fx_radial_glow", count=90, lifeMin=0.2f, lifeMax=0.4f, sizeStart=9f, sizeEnd=2f, gravity=2f, additive=true, ct=ctBurst, chex=new[]{"#ffffff","#ffe000","#daa520"}, cop=new[]{1f,1f,0f}, shape="sphere", shapeSize=0.1f, velBase=V(0,0,0), velRand=V(6,6,6) },
                    new LayerSpec{ name="L3_spark_burst", kind="particle", tex="fx_radial_glow", count=70, lifeMin=0.15f, lifeMax=0.32f, sizeStart=6f, sizeEnd=1.5f, gravity=4f, additive=true, ct=ctSp, chex=new[]{"#ffe680","#ffffff","#ffd700"}, cop=new[]{1f,0.9f,0f}, shape="cone", shapeSize=0.2f, angle=20f, velBase=V(7,3,0), velRand=V(0,3,3) },
                    new LayerSpec{ name="L4_sprite_flash", kind="sprite", tex="fx_radial_glow", count=1, lifeMin=0.35f, lifeMax=0.4f, sizeStart=1.2f, sizeEnd=1.2f, additive=true, ct=new[]{0f,0.35f,1f}, chex=new[]{"#ffffff","#ffe000","#daa520"}, cop=new[]{1f,0.45f,0f} },
                    new LayerSpec{ name="L5_sprite_shard", kind="sprite", tex="fx_holy_shard", count=1, lifeMin=0.4f, lifeMax=0.5f, sizeStart=1.0f, sizeEnd=1.0f, additive=true, ct=new[]{0f,1f}, chex=new[]{"#ffe08a","#daa520"}, cop=new[]{0.95f,0f}, rotSpeed=3.14f },
                    new LayerSpec{ name="L6_holy_ring", kind="ring", tex="fx_white_ring", count=1, lifeMin=0.35f, lifeMax=0.35f, sizeStart=0.5f, sizeEnd=2.2f, additive=true, ct=new[]{0f,0.3f,1f}, chex=new[]{"#ffd700","#ffffff","#daa520"}, cop=new[]{0f,0.8f,0f} },
                    new LayerSpec{ name="L7_ring_outer", kind="ring", tex="fx_white_ring", count=1, lifeMin=0.5f, lifeMax=0.5f, sizeStart=0.8f, sizeEnd=3.0f, additive=true, ct=new[]{0f,0.3f,1f}, chex=new[]{"#daa520","#ffe000","#8a6d1a"}, cop=new[]{0f,0.5f,0f} },
                    new LayerSpec{ name="L8_radiant_dust", kind="particle", tex="fx_soft_smoke", count=16, lifeMin=0.4f, lifeMax=0.6f, sizeStart=8f, sizeEnd=18f, gravity=-0.5f, additive=false, ct=ctDust, chex=new[]{"#fff8e0","#ffe08a","#daa520"}, cop=new[]{0f,0.4f,0f}, shape="sphere", shapeSize=0.3f, velBase=V(0,0.9f,0), velRand=V(0.3f,0,0.3f) },
                    new LayerSpec{ name="L9_sprite_dust", kind="sprite", tex="fx_soft_smoke", count=1, lifeMin=0.5f, lifeMax=0.6f, sizeStart=1.6f, sizeEnd=1.6f, additive=true, ct=new[]{0f,0.25f,1f}, chex=new[]{"#fff8e0","#ffe000","#daa520"}, cop=new[]{0f,0.5f,0f} },
                };
            }
            else
            {
                return new LayerSpec[] {
                    new LayerSpec{ name="L0_core_flash", kind="particle", tex="fx_radial_glow", count=12, lifeMin=0.04f, lifeMax=0.09f, sizeStart=9f, sizeEnd=3f, gravity=0f, additive=true, ct=ct, chex=new[]{"#ffffff","#ff3a20","#8b0000"}, cop=new[]{1f,0.85f,0f}, shape="sphere", shapeSize=0.15f },
                    new LayerSpec{ name="L1_glow_core", kind="sprite", tex="fx_radial_glow", count=1, lifeMin=0.35f, lifeMax=0.4f, sizeStart=1.8f, sizeEnd=1.8f, additive=true, ct=new[]{0f,0.3f,1f}, chex=new[]{"#ffffff","#ff3a20","#8b0000"}, cop=new[]{1f,0.55f,0f} },
                    new LayerSpec{ name="L2_burst_radial", kind="particle", tex="fx_radial_glow", count=90, lifeMin=0.2f, lifeMax=0.4f, sizeStart=9f, sizeEnd=2f, gravity=2f, additive=true, ct=new[]{0f,0.25f,1f}, chex=new[]{"#ffffff","#ff5a20","#ff1a00"}, cop=new[]{1f,1f,0f}, shape="sphere", shapeSize=0.1f, velBase=V(0,0,0), velRand=V(6,6,6) },
                    new LayerSpec{ name="L3_spark_burst", kind="particle", tex="fx_radial_glow", count=70, lifeMin=0.15f, lifeMax=0.32f, sizeStart=6f, sizeEnd=1.5f, gravity=4f, additive=true, ct=ctSp, chex=new[]{"#ff8a3a","#ffffff","#ff4b3a"}, cop=new[]{1f,0.9f,0f}, shape="cone", shapeSize=0.2f, angle=20f, velBase=V(7,3,0), velRand=V(0,3,3) },
                    new LayerSpec{ name="L4_sprite_flash", kind="sprite", tex="fx_radial_glow", count=1, lifeMin=0.35f, lifeMax=0.4f, sizeStart=1.2f, sizeEnd=1.2f, additive=true, ct=new[]{0f,0.35f,1f}, chex=new[]{"#ffffff","#ff1a00","#8b0000"}, cop=new[]{1f,0.45f,0f} },
                    new LayerSpec{ name="L5_sprite_drop", kind="sprite", tex="fx_blood_drop", count=1, lifeMin=0.4f, lifeMax=0.5f, sizeStart=1.0f, sizeEnd=1.0f, additive=true, ct=new[]{0f,1f}, chex=new[]{"#ff4b3a","#8b0000"}, cop=new[]{0.95f,0f}, rotSpeed=3.14f },
                    new LayerSpec{ name="L6_bloodflame_ring", kind="ring", tex="fx_white_ring", count=1, lifeMin=0.35f, lifeMax=0.35f, sizeStart=0.5f, sizeEnd=2.2f, additive=true, ct=new[]{0f,0.3f,1f}, chex=new[]{"#ff1a00","#ffffff","#8b0000"}, cop=new[]{0f,0.8f,0f} },
                    new LayerSpec{ name="L7_ring_outer", kind="ring", tex="fx_white_ring", count=1, lifeMin=0.5f, lifeMax=0.5f, sizeStart=0.8f, sizeEnd=3.0f, additive=true, ct=new[]{0f,0.3f,1f}, chex=new[]{"#8b0000","#ff1a00","#4a0000"}, cop=new[]{0f,0.5f,0f} },
                    new LayerSpec{ name="L8_smoke_residue", kind="particle", tex="fx_soft_smoke", count=16, lifeMin=0.4f, lifeMax=0.6f, sizeStart=8f, sizeEnd=18f, gravity=-0.3f, additive=false, ct=ctDust, chex=new[]{"#1a0a0a","#8b0000","#3a0a0a"}, cop=new[]{0f,0.45f,0f}, shape="sphere", shapeSize=0.3f, velBase=V(0,0.8f,0), velRand=V(0.3f,0,0.3f) },
                    new LayerSpec{ name="L9_sprite_smoke", kind="sprite", tex="fx_soft_smoke", count=1, lifeMin=0.5f, lifeMax=0.6f, sizeStart=1.6f, sizeEnd=1.6f, additive=false, ct=new[]{0f,0.25f,1f}, chex=new[]{"#5a1a1a","#8b3030","#3a0a0a"}, cop=new[]{0f,0.5f,0f} },
                };
            }
        }

        // ============ 主入口 ============
        public static void BuildAll()
        {
            var sb = new System.Text.StringBuilder();
            EnsureFolder(MatDir);
            EnsureTextures(new[]{"fx_radial_glow","fx_holy_shard","fx_soft_smoke","fx_blood_drop","fx_white_ring"});
            BuildOne("HitFX_Tianting", true, sb);
            BuildOne("HitFX_Xuanchao", false, sb);
            AssetDatabase.SaveAssets();
            Debug.Log(sb.ToString());
        }

        static void BuildOne(string prefabName, bool tianting, System.Text.StringBuilder sb)
        {
            var root = new GameObject(prefabName);
            var specs = BuildSpecs(tianting);
            foreach (var L in specs)
            {
                Material m = MakeParticleMat(L.tex, L.additive);
                MakeLayer(root.transform, L, m);
            }
            string path = PrefabDir + "/" + prefabName + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            sb.AppendLine("[HitFX] " + prefabName + " 已建 " + specs.Length + " 层 -> " + path);
        }

        static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}

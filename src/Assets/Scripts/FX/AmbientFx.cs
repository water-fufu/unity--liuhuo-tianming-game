// AmbientFx.cs —— 复刻原版 45° 火花氛围系统（对位 Web src3d/main.js L1683-1762 _buildSpark/_spawnSpark/_updateSpark）
// 原版参数(已破解源码实证): 池化 InstancedMesh SPARK_COUNT=60 / _sparkInterval=0.08(12.5/s)
//   _spawnSpark: life=2+rand*0.8, x=(rand-0.5)*300(±150 全宽), z=-(30+rand*280)(屏顶→中部),
//                y=2+rand*8(低空), vx=60+rand*30(+X 右), vz=60+rand*30(+Z 下坠) => 45°左上→右下
//   _updateSpark: vz+=40dt(加速), x+=vx dt, z+=vz dt, 淡入(0.2:0.4→1)/淡出(0.75:1→0),
//                billboard lookAt(camera), 回收 z>240||x>170||age≥life
//   视觉: 金红径向亮核(255,210,90→200,40,0), Plane1.8, Additive+depthWrite/depthTest:false+DoubleSide,
//         renderOrder=3, frustumCulled=false, scale 0→1→0
// u3d 实现(规则#11 说明: 原版为 InstancedMesh 特定视觉, 无现成 u3d 组件能精确还原, 用 ParticleSystem 最贴近原生,
//   复用项目已有 ParticleFxPool.Mat()(URP Particles/Unlit additive + 软圆发光纹理, 防品红), 零外部依赖):
//   战场中心 C=(5,0,71) 上空 y≈6, 粒子沿 (+X,+Z) 45° 飘, sizeOverLifetime 0→1→0, colorOverLifetime 金→红 fade,
//   Billboard renderMode(等效 lookAt(camera)), maxParticles=60, rate 12.5/s, 覆盖天庭(38.1)/玄朝(-28.7)基地。
using UnityEngine;

namespace Liuhuo.FX
{
    public class AmbientFx : MonoBehaviour
    {
        private static AmbientFx _inst;

        // 自举: 谁调用都保证实例存在(对位 UltController.EnsureCreated/BattleCamera.EnsureCreated), 免场景预挂
        public static AmbientFx EnsureCreated()
        {
            if (_inst == null) { var go = new GameObject("AmbientFx"); _inst = go.AddComponent<AmbientFx>(); }
            return _inst;
        }

        void Start() { Build(); }

        // 构建 45° 火花粒子系统(全程程序化, 无外部资源)
        void Build()
        {
            var ps = gameObject.AddComponent<ParticleSystem>();
            transform.position = new Vector3(95f, 50f, -105f);   // ★任务2：AmbientFx GameObject 位置按用户 Inspector 图 (95,50,-105)；发射区经 shape.position 补偿仍覆盖战场
            var m = ps.main;
            m.loop = true; m.playOnAwake = true;
            m.maxParticles = 60;                         // SPARK_COUNT=60
            m.startLifetime = 2.4f;                      // life 2~2.8 平均(原版 2.0+rand*0.8)
            m.startSpeed = 0f;                           // 速度全由 velocityOverLifetime 接管(确定性,与 shape 解耦)
            m.startSize = 1.8f;                          // Plane 1.8
            m.gravityModifier = 0f;                      // 原版无重力, 下坠靠 vz 分量
            m.simulationSpace = ParticleSystemSimulationSpace.World;

            // ★任务3/本轮根因修复：颜色要荧光黄。上轮中间帧 HDR(1.8,1.7,1.2)>1，Nographics 无 HDR 帧缓冲/无 bloom
            //   → 被 clamp 到 (1,1,1) 纯白，叠 normal blend 在亮背景上呈灰白 = 用户看到的"灰色根本不是荧光黄"。
            //   修复：全部色键 ≤1，中间帧"提亮"改用 R/G 顶格 + B 压低(保黄相，clamp 后仍黄，不发白)。
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] {
                    new GradientColorKey(new Color(1.0f, 1.0f, 1.0f), 0f),        // ★白光迸发(2026-09-07)：火花诞生瞬间白炽(白光),随粒子扩散向外发散
                    new GradientColorKey(new Color(1.0f, 0.99f, 0.80f), 0.5f),     // ★更浅黄：白光发散去主色, B 抬到 0.80(原0.63更浅, 仍保黄相不发白)
                    new GradientColorKey(new Color(1.0f, 0.97f, 0.70f), 1f),       // 尾段浅黄(保黄相)
                },
                new[] {
                    new GradientAlphaKey(0.4f, 0f),    // 淡入起点 0.4(对位 t<0.2:0.4→1)
                    new GradientAlphaKey(1f, 0.2f),    // 淡入终点 1
                    new GradientAlphaKey(1f, 0.75f),   // 淡出起点 1(对位 t>0.75:1→0)
                    new GradientAlphaKey(0f, 1f),      // 淡出终点 0
                });
            col.color = grad;

            // scale 0→1→0 淡入淡出(对位原版 scale 0→1→0).
            //   ★任务4：最大粒子体积减半 4.5→2.25(用户确认指 sizeOverLifetime 峰值)。u3d 俯视远距曾放大到 4.5 才可见，
            //   现用户要更小巧颗粒，峰值减半后尺寸更细但仍在俯视可辨。startSize 基础 1.8 不变，最终尺寸=1.8×曲线。
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(2.25f,
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.6f, 1f), new Keyframe(1f, 0f)));

            // 发射率 12.5/s (= _sparkInterval 0.08). ★本轮: 对应发射区范围扩大1/2 而增大 —— 12.5→18.75(×1.5).
            //   lifetime 2.4 时同屏≈18.75×2.4≈45 粒子 < maxParticles(60), 无需提升粒子上限.
            var em = ps.emission;
            em.rateOverTime = 18.75f;

            // 生成区: Box 以系统位置(95,50,-105)为中心(shape.position=0, 发射中心=对象位置, 保持上轮修正).
            //   ★本轮: 范围扩大 1/2(×1.5). 粒子速度 vx=-75(左)/vz=75(下沉) 整体往【左下方】飘散,
            //   故 Box X 140→210、Z 130→195 各扩大1.5倍, 中心仍锚系统位置(95,50,-105)不动, 左下方覆盖随之拓宽.
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(210f, 1f, 195f);
            // ★本轮修正：用户要求【火花粒子从火花系统位置刷新】，即系统位置(95,50,-105)就是发射中心。
            //   上一轮做 shape.position 补偿(-90,-44,176)让发射区回到战场中心(5,6,71)，结果粒子刷新点≠系统位置(95,50,-105)，被用户否决。
            //   现 shape.position 归零 -> 发射区中心 = 对象位置(95,50,-105)，粒子从该系统位置刷新（发射区 Box 140×130 以该点为中心摊开）。
            sh.position = Vector3.zero;

            // 45° 恒速下坠: vx=-75(+X 屏幕左), vz=75(+Z 下坠) —— 用户实测当前(75,75)视觉为【右上→左下】,目标【左上→右下】=水平镜像,故 vx 取反。
            var vol = ps.velocityOverLifetime;
            vol.enabled = true;
            vol.space = ParticleSystemSimulationSpace.World;
            vol.x = -75f; vol.y = 0f; vol.z = 75f;

            var r = gameObject.GetComponent<ParticleSystemRenderer>();
            if (r == null) r = gameObject.AddComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;   // billboard lookAt(camera) 等效
            // ★任务5(本轮)：火花被 mainmap(1) 遮挡根治。mainmap(1) 用 material.renderQueue=3000+secondRenderOrder=3002 + ZTest Always 置顶，
            //   火花 default renderQueue=3000 < 3002 而 mainmap 又 ZTest Always，两者深度都恒定通过 -> 绘制顺序由 renderQueue 决定，火花先画被 mainmap 盖住。
            //   修复：把火花 renderQueue 提到 mainmap 之上(=3100) 并保留 ZTest Always -> 火花后画、恒居最前，全场可见。比独立相机更可控、可生产验证。
            //   保留上一轮可见性修正：实心 normal alpha blend(SrcAlpha/OneMinusSrcAlpha) 替代 additive 亮背景失效(lesson#107)。clone 共享材质不污染圣裁/同死/受击。
            var baseMat = ParticleFxPool.Mat();
            var sparkMat = new Material(baseMat);
            sparkMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);            // 5  SrcAlpha
            sparkMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);    // 10 OneMinusSrcAlpha
            sparkMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);          // depthTest off 置顶
            sparkMat.renderQueue = 3100;                                                               // ★任务5: > mainmap 3002 -> 恒居最前
            r.sharedMaterial = sparkMat;
            r.sortingOrder = 999;
            ps.Play();
            var c0 = grad.colorKeys.Length > 0 ? grad.colorKeys[0].color : Color.white;
            var c1 = grad.colorKeys.Length > 1 ? grad.colorKeys[1].color : Color.white;
            Debug.Log($"[SparkProbe] AmbientFx 火花已建 pos={transform.position} shapeLocal=({sh.position.x},{sh.position.y},{sh.position.z}) shapeScale=({sh.scale.x},{sh.scale.y},{sh.scale.z}) rate={em.rateOverTime.constant} velocity=({vol.x.constant},{vol.y.constant},{vol.z.constant}) colKeys=({c0.r:F2},{c0.g:F2},{c0.b:F2}|{c1.r:F2},{c1.g:F2},{c1.b:F2}) blend=normal(ZTest=Always) renderQueue={sparkMat.renderQueue} sortingOrder={r.sortingOrder} material={sparkMat?.shader?.name}");
        }
    }
}

// DeathFXManager.cs —— 死亡/基地摧毁特效中枢（B4/D1/D3 程序化生成，不依赖缺失 PNG）
// O1 单例（对位 HitFXManager 自举模式）：静态 PlayDeath/PlayBaseDestroyed，EnsureCreated 免 Inspector。
// D1 士兵死亡：team0 天庭死→金白烟雾；team1 玄朝死→红黑烟雾（对位 Web death 烟雾）。
// D3 基地摧毁：team0 天庭基地被毁→金白爆裂；team1 玄朝基地被毁→红黑爆裂。
// 全程序化 ParticleSystem（无外部贴图/无序列帧），避免 B4 缺失 PNG 坑。
using System.Collections;
using UnityEngine;
using Liuhuo.FX;   // P1-2 池化收口：复用 ParticleFxPool 预建的球形爆发/环形波前池，避免每次 new

namespace Liuhuo.Core
{
    public class DeathFXManager : MonoBehaviour
    {
        public static DeathFXManager Instance;

        void Awake() { Instance = this; }

        void Start()
        {
            // D3：订阅基地摧毁事件（不改 BaseSystem.cs，外部挂 onDestroyed）
            foreach (var b in FindObjectsOfType<BaseSystem>())
            {
                int t = b.Team;
                b.onDestroyed?.AddListener(() => PlayBaseDestroyed(t));   // ?. 防御：onDestroyed 为 null 时跳过，防 Start NRE 中断订阅循环（BaseSystem L36 已用 ?.Invoke）
            }
        }

        // public：契约2/Y1——GameBootstrap.Start 战斗开始即 EnsureCreated，保证基地摧毁订阅早于爆裂
        public static void EnsureCreated()
        {
            if (Instance == null)
            {
                var go = new GameObject("DeathFXManager");
                go.AddComponent<DeathFXManager>();
            }
        }

        // T2-3 重载：重开后基地是 BaseModelFactory 重建的新实例，旧订阅随旧基地销毁失效，
        //   须重新 FindObjectsOfType 订阅 onDestroyed（对位 Web _resetBattle 后基地保留，此处 u3d 重建需重挂）
        public static void Rescan()
        {
            EnsureCreated();
            foreach (var b in FindObjectsOfType<BaseSystem>())
            {
                if (b.onDestroyed == null) continue;
                b.onDestroyed.RemoveAllListeners();   // 清退旧监听，防重复累积
                int t = b.Team;
                b.onDestroyed.AddListener(() => PlayBaseDestroyed(t));
            }
            Debug.Log("[DeathFXManager] Rescan 已重订阅基地摧毁事件(重载)");
        }

        // 士兵死亡烟雾（Soldier.Die 调用，team0 金白 / team1 红黑）
        public static void PlayDeath(Vector3 pos, int team)
        {
            EnsureCreated();
            Instance.StartCoroutine(Instance.DeathFx(pos, team));
        }

        // 基地摧毁爆裂（BaseSystem.onDestroyed 订阅，team0 金白 / team1 红黑）
        public static void PlayBaseDestroyed(int team)
        {
            EnsureCreated();
            Vector3 pos = Instance.transform.position;
            foreach (var b in FindObjectsOfType<BaseSystem>())
                if (b.Team == team) { pos = b.transform.position; break; }
            Instance.StartCoroutine(Instance.BaseDestroyFx(pos, team));
        }

        // 烟雾：一次 burst 上飘渐隐（Billboard，无色贴图，用 startColor 赋色）
        IEnumerator DeathFx(Vector3 pos, int team)
        {
            Color c = team == 0 ? new Color(1f, 0.84f, 0f, 0.9f) : new Color(1f, 0.27f, 0.27f, 0.9f);
            BurstPuff(pos + Vector3.up * 0.6f, c, 22, 2.2f, 0.5f, 1.6f);
            yield return new WaitForSeconds(2f);
        }

        // 爆裂：大 burst + 多球扩散
        IEnumerator BaseDestroyFx(Vector3 pos, int team)
        {
            Color c = team == 0 ? new Color(1f, 0.84f, 0f, 1f) : new Color(1f, 0.27f, 0.27f, 1f);
            BurstPuff(pos + Vector3.up * 1f, c, 60, 6f, 1.2f, 2.5f);
            // R3-E4 基地摧毁 ring 层（文部 TC-E4：半径0.5→6.0 金#ffdd55→橙#ff8800 additive）+ 屏幕震动 0.8/0.5s
            RingFx(pos + Vector3.up * 1f, team);
            StartCoroutine(ShakeFx(0.8f, 0.5f));
            yield return new WaitForSeconds(3f);
        }

        // 环形扩散冲击波（程序化 ParticleSystem Shape=Circle，外扩渐隐）
        // P1-2 池化收口：原实现每次 new GameObject("BaseRing") + Destroy → 改走 ParticleFxPool 预建的环形波前池，
        //   内部 ObjectPool<ParticleSystem> 复用 + RecycleRoutine 自动回池。保留原始分阵营起色(金#ffdd55 / 橙#ff8800)→橙渐隐。
        void RingFx(Vector3 pos, int team)
        {
            Color from = team == 0 ? new Color(1f, 0.87f, 0.33f, 1f) : new Color(1f, 0.53f, 0f, 1f);
            Color to = new Color(1f, 0.53f, 0f, 1f);   // 渐隐目标橙#ff8800
            ParticleFxPool.EmitRing(pos, from, to, 0.8f);
        }

        // 屏幕震动（主相机 position + small noise，可并入现有 shake 体系）
        IEnumerator ShakeFx(float intensity, float duration)
        {
            var cam = Camera.main;
            if (cam == null) yield break;
            Vector3 origin = cam.transform.position;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                cam.transform.position = origin + new Vector3(
                    Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f,
                    Mathf.PerlinNoise(0f, Time.time * 25f) - 0.5f,
                    0f) * intensity;
                yield return null;
            }
            cam.transform.position = origin;
        }

        // 程序化 ParticleSystem 瞬发粒子（无贴图/序列帧）
        // P1-2 池化收口：原实现每次 new GameObject("Puff") + Destroy → 高频战斗产生 GC 与对象开销。
        //   现改走 ParticleFxPool 预建的球形爆发池（ObjectPool<ParticleSystem>，预创建 3 套），
        //   用毕由内部 RecycleRoutine 自动回池复用，不再每次运行时 new。颜色/数量/速度/尺寸/寿命原样透传。
        void BurstPuff(Vector3 pos, Color c, int count, float speed, float size, float life)
        {
            ParticleFxPool.EmitBurst(pos, c, count, speed, size, life);
        }
    }
}

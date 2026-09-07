// HitFXManager.cs —— 受击特效触发中枢（T3：黑块根因=旧预制体粒子 size/scale 未对齐 hit.json，渲染成超大色块糊脸）
// 对位 Web hit 特效 playGlobal：Soldier.TakeDamage 一行触发，覆盖单体 + AOE(F6)。
// 阵营交叉配色（对位 hitfx-demo PlayTianting/PlayXuanchao）：天庭兵(team0)受击→深红#FF3030；玄朝兵(team1)受击→金#FFD700。
// ★T3 根治：改用 u3d-hitfx-demo 纯程序化 HitFx（纹理/材质/几何全部代码生成，零预制体/零 Resources.Load），
//   HitFx 自播自清理（Destroy after lifespan~0.7s），彻底治掉"旧预制体超大纯色块糊脸"（用户点名"黑块是没替换的受击特效"）。
using UnityEngine;
using Liuhuo.FX;

namespace Liuhuo.Core
{
    public class HitFXManager : MonoBehaviour
    {
        public static HitFXManager Instance;

        void Awake() { Instance = this; }

        // 惰性建单例（Soldier.TakeDamage 调 Play 时若场景未挂本组件则自动建，免 Inspector）
        static void EnsureCreated()
        {
            if (Instance == null)
            {
                var go = new GameObject("HitFXManager");
                go.AddComponent<HitFXManager>();
            }
        }

        static bool _hitCaptured;   // 工部二 §14.6-1：受击截图限频单次

        // victimTeam 0(天庭/蓝)受击 -> 深红#FF3030; 1(玄朝/红)受击 -> 金#FFD700（对位 hitfx-demo）
        public static void Play(int victimTeam, Vector3 pos)
        {
            EnsureCreated();
            if (Instance == null) return;
            var main = victimTeam == 0 ? new Color(1f, 0.188f, 0.188f, 1f) : new Color(1f, 0.84f, 0f, 1f);
            var light = victimTeam == 0 ? new Color(1f, 0.5f, 0.375f, 1f) : new Color(1f, 0.94f, 0.63f, 1f);
            var go = new GameObject("HitFx");
            go.transform.position = pos;   // HitFx.Init 内部自建子物体并居 +1up（命中点），自播自清理
            go.AddComponent<HitFx>().Init(main, light);
            Debug.Log($"[HitFX] victimTeam={victimTeam} fx={(victimTeam == 0 ? "tianting_hit" : "xuanchao_hit")} procedural=HitFx(T3) main=#{ColorUtility.ToHtmlStringRGB(main)}");
            // 工部二 §14.6-1：受击截图（限频单次，取峰值帧）——E3 证据（受击特效 Player 运行时可见非静默）
            if (!_hitCaptured)
            {
                _hitCaptured = true;
                Instance.StartCoroutine(CaptureLater());
            }
        }

        static System.Collections.IEnumerator CaptureLater()
        {
            yield return new WaitForSeconds(0.4f);
            string hpath = System.IO.Path.Combine(Application.persistentDataPath, "hit_fx.png");
            ScreenCapture.CaptureScreenshot(hpath);
            Debug.Log($"[Capture] 已存图: {hpath} (受击特效, T3 HitFx)");
        }
    }
}

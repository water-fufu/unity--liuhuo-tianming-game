// BattleCamera.cs —— 近景相机（P6）：orthoSize=20 俯视近景，跟随战斗中心，供 TC-F2(近景清晰)/TC-C4(近景5帧差异) 用
// 运行时自举（静态 EnsureCreated，免 Inspector 拖拽）：反射调 SetEnabled(true) 建近景相机并覆盖视角；false 回退 Main Camera 全景。
// 对齐 s3 方案 P6：Main Camera 保持 ortho55；本近景相机 orthoSize=20 战斗期跟随士兵平均位置。
using UnityEngine;
using Liuhuo.FX;   // B4 震动消费侧：UltController.GetShake/DecayShake

namespace Liuhuo.Core
{
    public class BattleCamera : MonoBehaviour
    {
        public static BattleCamera Instance;

        [Header("近景参数")]
        [Tooltip("近景正交尺寸（s3=20，对照 Main Camera ortho55）")]
        public float orthoSize = 28f;
        [Tooltip("跟随中心平滑系数，越大跟越紧")]
        public float followLerp = 8f;

        private Camera _close;      // 近景相机（新建，非 Main Camera）
        private Camera _main;       // 全景 Main Camera（ortho55）
        private const float Height = 80f;   // 俯视高度，对位 Main Camera pos.y=80

        void Awake() { Instance = this; }

        void Start()
        {
            _main = Camera.main;
            // 建近景相机：俯视 ortho20，默认禁用，SetEnabled(true) 启用
            var go = new GameObject("BattleCamera_Closeup");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, Height, 0f);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // 俯视（x轴转90）
            _close = go.AddComponent<Camera>();
            _close.orthographic = true;
            _close.orthographicSize = orthoSize;
            _close.clearFlags = CameraClearFlags.SolidColor;
            _close.backgroundColor = new Color(0.05f, 0.05f, 0.05f, 1f);
            _close.depth = (_main != null) ? _main.depth + 1f : 1f;   // 保证覆盖 Main Camera
            _close.enabled = false;                                   // 默认全景
        }

        void Update()
        {
            // B4（屏幕震动消费侧，对位 Web 相机 shake）：独立于 enable 状态消费（UltController AddShake 触发圣裁/同死震动）。
            //   不依赖 _close.enabled——GameBootstrap 确保本实例存在，Update 恒消费，超时无等待。
            ApplyShake();

            if (_close == null || !_close.enabled) return;
            // 跟随战斗中心 = 活跃士兵平均位置（无士兵则保持原位，不漂移）
            var c = ComputeCombatCenter();
            var p = _close.transform.position;
            p.x = Mathf.Lerp(p.x, c.x, followLerp * Time.deltaTime);
            p.z = Mathf.Lerp(p.z, c.z, followLerp * Time.deltaTime);
            _close.transform.position = p;
        }

        // B4 震动消费：读 UltController.GetShake() 对当前启用相机施加随机位移后衰减（带空判防 null）
        private void ApplyShake()
        {
            if (_main == null && _close == null) _main = Camera.main;
            var ult = UltController.Instance;
            if (ult == null) return;
            float sh = ult.GetShake();
            if (sh <= 0f) return;
            Vector3 shake = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * (sh * 0.6f);
            if (_close != null && _close.enabled) _close.transform.position += shake;
            else if (_main != null && _main.enabled) _main.transform.position += shake;
            else if (_main != null) _main.transform.position += shake;   // 全景默认启用，兜底
            ult.DecayShake(Time.deltaTime);
        }

        Vector3 ComputeCombatCenter()
        {
            var list = SoldierFactory.ActiveSoldiers;
            if (list == null || list.Count == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var s in list)
            {
                if (s == null) continue;
                sum += s.transform.position; n++;
            }
            return (n > 0) ? sum / n : Vector3.zero;
        }

        // ===== 反射/自测入口 =====

        // 启用近景（TC-F2/C4）：自举 + 覆盖 Main Camera
        public static void SetEnabled(bool on)
        {
            EnsureCreated();
            if (Instance == null || Instance._close == null) return;
            Instance._close.enabled = on;
            if (on && Instance._main != null) Instance._main.enabled = false;   // 停全景，避免双相机叠影
            else if (on == false && Instance._main != null) Instance._main.enabled = true;  // 回退全景
        }

        // 切换近景/全景（调试）
        public static void Toggle()
        {
            if (Instance == null || Instance._close == null) return;
            SetEnabled(!Instance._close.enabled);
        }

        // 自举：静态方法可运行时建组件（反射调 SetEnabled 时无需场景预挂）
        // public：GameBootstrap.Start 跨类调用（B4 震动消费侧），原 private 致 CS0122。
        public static void EnsureCreated()
        {
            if (Instance != null) return;
            var go = new GameObject("BattleCamera");
            go.AddComponent<BattleCamera>();
        }
    }
}

// Bullet.cs —— 弹体实体（士兵开火生成，池化，命中结算）
// 对位 Web 士兵子弹对象：沿 dir 飞，超射程回池，命中士兵/基地走 CombatSystem.ResolveHit（F2 伤害分道）。
using UnityEngine;

namespace Liuhuo.Core
{
    [RequireComponent(typeof(Collider))]
    public class Bullet : MonoBehaviour
    {
        public int team;              // 发射阵营 0=天庭(蓝)/1=玄朝(红)
        public float soldierDamage;   // 对兵伤害（Flatten 后的 weapon.damage）
        public float baseDamage;      // 对基地伤害（F2 分道）
        public float aoeRadius;       // AOE 半径（玄朝=0 单体）
        public float aoeFalloff;      // AOE 衰减系数

        private float _speed;         // 弹速 m/s
        private float _range;         // 最大射程 m（到射程回池）
        private Vector3 _dir;         // 飞行方向
        public Vector3 Dir => _dir;   // s5 v3.2 击退来向（CombatSystem.ResolveHit 传 -Dir 给 TakeDamage）
        private float _traveled;      // 已飞行距离
        private SoldierFactory _pool; // 回池引用（注入）
        private bool _spent;          // 防止重复命中回调
        private Rigidbody _rb;        // R1 物理组件：MovePosition 驱动（防隧穿，勿用 transform 位移）

        // 由 CombatSystem.FireBullet 从池取出后调用
        public void Launch(int shooterTeam, WeaponConfig w, Vector3 dir)
        {
            team = shooterTeam;
            soldierDamage = w.GetSoldierDamage();
            baseDamage = w.GetBaseDamage();
            aoeRadius = w.aoeRadius;
            aoeFalloff = w.aoeDamageFalloff;
            _speed = w.projectileSpeed;
            _range = w.attackRange;
            _dir = dir;
            _traveled = 0f;
            _spent = false;
            // R1 物理驱动初始化（防隧穿）：Dynamic+ContinuousDynamic+velocity，物理引擎做连续扫掠。
            // Kinematic(transform手动/MovePosition)都不走CCD扫掠→高速弹穿士兵；Dynamic由物理积分运动才触发连续碰撞。
            if (_rb != null)
            {
                _rb.isKinematic = false;
                _rb.useGravity = false;
                _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                _rb.velocity = _dir.normalized * _speed;
            }
            // R3-E3 子弹拖尾阵营色：天庭=金#ffd700→透明 / 玄朝=红#dc143c→透明（TrailRenderer 组件 S5_Builder 预制体上静态挂了）
            var trail = GetComponent<TrailRenderer>();
            if (trail != null)
            {
                Color tc = shooterTeam == 0 ? new Color(1f, 0.84f, 0f, 1f) : new Color(0.86f, 0.08f, 0.23f, 1f);
                trail.startColor = new Color(tc.r, tc.g, tc.b, 0.85f);
                trail.endColor = new Color(tc.r, tc.g, tc.b, 0f);
                var tg = new Gradient();
                tg.SetKeys(new[] { new GradientColorKey(tc, 0f), new GradientColorKey(tc, 1f) },
                           new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = tg;
            }
        }

        public void SetPool(SoldierFactory pool) { _pool = pool; }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            EnsureTrailMaterial();   // 品红根治：[MagentaProbe] 实证 prefab 序列化丢拖尾材质→null→URP 品红，运行时兜底
        }

        // 品红根治（V7 一票否决）：Bullet(Clone) TrailRenderer.material==null → URP 品红。
        // [MagentaProbe] 实证（60+ 个 Bullet slot0=[NULL]）：SaveAsPrefabAsset 丢运行时 new 的拖尾材质引用 → 加载后 null。
        // 运行时兜底：material/shader null 时设 URP 粒子 additive（阵营色由 Launch 的 startColor/endColor 驱动），只修的这一个。
        private void EnsureTrailMaterial()
        {
            var trail = GetComponent<TrailRenderer>();
            if (trail == null) return;
            // ★品红根治关键修正：TrailRenderer 的 .material getter 会实例化掩盖 null（返回默认内置材质），
            //   MagentaProbe 用的是 .sharedMaterials（暴露真 sharedMaterial=null → 品红）。故必须检查/设置 .sharedMaterial。
            var m = trail.sharedMaterial;
            if (m != null && m.shader != null) return;
            Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (s == null) s = Shader.Find("Legacy Shaders/Particles/Additive");
            if (s == null) s = Shader.Find("Sprites/Default");
            if (s != null)
            {
                var nm = new Material(s);
                if (nm.HasProperty("_BaseColor")) nm.SetColor("_BaseColor", Color.white);
                if (nm.HasProperty("_Color")) nm.SetColor("_Color", Color.white);
                nm.renderQueue = 3000;
                trail.sharedMaterial = nm;   // 设共享材质（持久，MagentaProbe 可见）；阵营色由 Launch 的 startColor/endColor 驱动，材质白色 additive 可共享
                Debug.Log($"[Bullet] 拖尾 sharedMaterial 兜底 shader={s.name} 品红消除");
            }
            else
            {
                Debug.LogWarning("[Bullet] 无可用拖尾 shader(URP Particles/Unlit + Legacy Additive + Sprites Default 均缺)，拖尾仍可能品红");
            }
        }

        // R1 防隧穿（运动由 rb.velocity 物理积分驱动，见 Launch）。这里只做射程计数。
        private void Update()
        {
            if (_spent) return;
            _traveled += _speed * Time.deltaTime;
            if (_traveled >= _range) Release();
        }

        // 命中入口（SoldierTag/BaseSystem 同物体 Collider 触发）
        private void OnTriggerEnter(Collider other)
        {
            if (_spent) return;
            CombatSystem.ResolveHit(this, other);
        }

        public void Release()
        {
            if (_spent) return;
            _spent = true;
            if (_pool != null) _pool.ReleaseBullet(this);
        }
    }
}

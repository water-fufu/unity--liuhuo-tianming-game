// Soldier.cs —— 士兵行为组件（移动/转向/攻击/两态动画/受击闪白/死亡）
// 对位 Web SoldierRenderManager（走位/开火/阵亡），两态为 Idle/Walk（G4 事实声明，fighting 复用 walk、dead 冻结）。血量/身份委托同物体 SoldierTag。
using System.Collections;
using UnityEngine;
using Liuhuo.Audio;   // P6(F5) 音频接线：士兵受击调 PlayHitSfx

namespace Liuhuo.Core
{
    public enum SoldierState { Idle, Move, Attack, Dead }

    [RequireComponent(typeof(SoldierTag))]
    public class Soldier : MonoBehaviour
    {
        [Header("引用")]
        [HideInInspector] public new SoldierTag tag;   // 同物体身份/血量（new 消除 Component.tag 遮蔽）
        public WeaponConfig weapon;
        public int team = 0;                   // 0=天庭(蓝)/1=玄朝(红)，与 tag.team 同步

        [Header("移动/寻敌")]
        [Tooltip("移动速度 m/s")]
        public float moveSpeed = 10f;
        [Tooltip("攻击范围 m")]
        public float attackRange = 20f;
        [Tooltip("攻击间隔 s")]
        public float attackInterval = 1.2f;
        [Tooltip("最大转向速度 rad/s")]
        public float maxTurnSpeed = 6f;
        [Tooltip("受击闪白时长 s (s5 v3.2, 文部 §七 P0)")]
        public float hitFlashTime = 0.15f;   // S4：对齐原版 150ms
        [Tooltip("受击击退距离 m (s5 v3.2 工部二 §13.2-③ 瞬时事件)")]
        public float knockbackDist = 0.2f;
        [Tooltip("击退态时长 s (此内跳过 AI Move, 防被下一帧位移覆盖)")]
        public float knockbackTime = 0.1f;
        [Range(0.85f, 1.15f)]
        [Tooltip("攻距随机 ±15%（对位 soldierAI.js Phase16：`_getAttackRange*attackRangeMul`）")]
        public float attackRangeMul = 1f;

        [Header("运行时引用(由 Factory 注入)")]
        [HideInInspector] public BaseSystem enemyBase;   // 敌方基地(AI 保底目标)
        [HideInInspector] public SoldierFactory factory;  // 回池引用

        public SoldierState state = SoldierState.Move;
        private Animator _anim;
        private float _lastAttack = -999f;
        private Coroutine _flash;
        private SoldierHalo _halo;   // 任务4：士兵脚下阵营色呼吸光环（独立跟随物体）
        // ★T1(S4)：角度制朝向 yaw(对位 dist10 currentYaw/targetYaw + aimOffset 补偿模型正脸轴)
        private float targetYaw;
        private float currentYaw;
        private const float AimOffset = +90f;  // ★T1用户指令(2026-09-07)：士兵模型旋转180度、瞄准方向(攻击/移动yaw)不变。前身-90(正脸+X→目标)基础上+180 → +90(模型绕Y转180°,视觉翻转,弹道dir仍=aim-origin瞄向目标)。
                                               //   前身(2026-09-06) = -90：士兵模型视觉正脸=+X(铁证=同一 tianting_soldier_q.fbx 与 Web dist10 md5一致(6e96f5f1...),
                                               //   Web SoldierRenderManager.js:71 SOLDIER_AIM_OFFSET=-PI/2 + v103用户实测"两队正脸=+X"→aimOffset=-90)。现按用户要求在此基础旋转180度。
                                               //   轴映射(+X→-90 / +Z→0 / -X→+90 / -Z→180)：-90+180=+90。若视觉翻转方向反了,此处回改±180即可。

        // 有效攻距/攻速：读 weaponConfig 优先，自身字段兜底（对位 soldierAI.js L196-201：天庭=25/3.0、玄朝=20/0.2）
        public float EffectiveAttackRange => ((weapon != null && weapon.attackRange > 0f) ? weapon.attackRange : attackRange) * attackRangeMul;
        public float EffectiveAttackCooldown => (weapon != null && weapon.attackCooldown > 0f) ? weapon.attackCooldown : attackInterval;

        public bool CanAttack => state != SoldierState.Dead && (Time.time - _lastAttack) >= EffectiveAttackCooldown;

        private void Awake()
        {
            _anim = GetComponent<Animator>();
            if (tag == null) tag = GetComponent<SoldierTag>();
            attackRangeMul = Random.Range(0.85f, 1.15f);   // 对位 soldierAI.js Phase16：攻距随机 ±15%（每兵个体差异）
            EnsureHitCollider();   // V1/C4 根治：士兵预制体无 Collider(迁移遗漏)，子弹永不命中
        }

        // V1/C4 根治：士兵预制体缺 Collider（实测 ResolveHit hasSoldierTag 恒 0 = 子弹从没命中士兵/基地，只撞子弹），
        // 子弹(Rigidbody+Trigger)飞过士兵无物理检测直接穿过。运行时补非阻挡 Trigger 胶囊，尺寸对齐模型 Bounds
        // (center y≈0.499 / size x0.607 y0.998 z0.674)。isTrigger=true 不阻碍士兵移动（无 Rigidbody 不受物理阻挡）。
        private void EnsureHitCollider()
        {
            if (GetComponent<Collider>() != null) return;   // 防御：已有碰撞体不重复加
            var cap = gameObject.AddComponent<CapsuleCollider>();
            cap.radius = 0.3f;
            cap.height = 1.0f;
            cap.center = new Vector3(0f, 0.5f, 0f);
            cap.isTrigger = true;
        }

        public void Init(int id, int t, WeaponConfig w, BaseSystem baseRef, SoldierFactory f)
        {
            tag = tag != null ? tag : GetComponent<SoldierTag>();
            if (tag != null)
            {
                tag.soldierId = id; tag.team = t;
                tag.hp = tag.maxHp; tag.isAlive = true;
                team = t;
            }
            weapon = w; enemyBase = baseRef; factory = f;
            state = SoldierState.Move;
            SetState(SoldierState.Move);
            // ★T1(S4)：初始化朝向角(防池化复用残留)；smooth 会收敛到目标
            currentYaw = (team == 0) ? 90f : -90f;
            targetYaw = currentYaw;
            EnsureHalo();   // 任务4：绑定士兵脚下阵营色呼吸光环（池化复活时刷新引用）
        }

        // 任务4：确保士兵有独立跟随的阵营色呼吸光环。首次创建，池化复活复用（只刷新引用）。
        private void EnsureHalo()
        {
            if (_halo == null)
            {
                var parent = factory != null ? factory.transform : null;
                _halo = SoldierHalo.Create(this, parent);
            }
            else
            {
                _halo.Bind(this);
            }
        }

        // 两态动画（G4）：Move/Attack 均走 walk，Idle/Dead 停
        public void SetState(SoldierState s)
        {
            if (state == s) return;
            state = s;
            if (_anim != null && _anim.parameters.Length > 0)
            {
                bool moving = s == SoldierState.Move || s == SoldierState.Attack;
                _anim.SetBool("Moving", moving);
            }
        }

        // 击退态计时（工部二 §13.2-③：瞬时事件 + 0.1s 击退态，防被下一帧 AI Move 覆盖）
        private float _knockbackUntil = -999f;

        // 由 SoldierAI 每帧调用（已算好方向），负责位移+朝向；击退态跳过 AI 位移
        public void MoveUpdate(float dt, Vector3 moveDir)
        {
            if (state == SoldierState.Dead) return;
            if (Time.time < _knockbackUntil) { SetState(SoldierState.Move); return; }   // 击退态：跳过 AI 位移
            if (moveDir.sqrMagnitude > 0.0001f)
            {
                transform.position += moveDir.normalized * moveSpeed * dt;
                // ★T1(S4)：移动时更新瞄准方向角(朝向由 LateUpdate 统一平滑+施加 aimOffset)，替代原 Slerp+transform.forward
                targetYaw = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            }
            SetState(SoldierState.Move);
        }

        // 攻击：枪口闪(占位) + 发弹（命中结算在 Bullet/CombatSystem）
        public void Attack(Transform tgt)
        {
            if (state == SoldierState.Dead || tgt == null) return;
            if (Time.time - _lastAttack < EffectiveAttackCooldown) return;
            _lastAttack = Time.time;
            SetState(SoldierState.Attack);   // 两态：Attack 复用 walk
            MuzzleFlash();
            // ★任务2：攻击时正面朝向瞄准目标（对位 soldierAI.js targetYaw=atan2(dx,dz)，绕Y旋转使正面+Z朝目标）。
            //   原 u3d 只在 MoveUpdate 转向移动方向，站桩攻击保持上一朝向 → 侧身瞄准；此处补朝向，正面对敌。
            Vector3 toTgt = tgt.position - transform.position;
            toTgt.y = 0f;
            if (toTgt.sqrMagnitude > 1e-4f) targetYaw = Mathf.Atan2(toTgt.x, toTgt.z) * Mathf.Rad2Deg;   // ★T1(S4)：瞄准方向 yaw 角(替代瞬时 transform.forward)
            Vector3 origin = transform.position + transform.forward * 1.2f + Vector3.up * 0.9f;
            // 瞄准敌兵躯干中心(非脚底)：否则子弹自 0.9m 枪口俯冲打地面，远距对射僵持到不了 winTeam（第五轮命中对位）
            Vector3 aim = tgt.position + Vector3.up * 1.0f;
            Vector3 dir = aim - origin;
            if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
            CombatSystem.FireBullet(origin, team, weapon, dir.normalized);
        }

        // ★T1(S4)：逐帧平滑逼近 targetYaw + 施加 aimOffset 渲染(模型正脸对齐 currentYaw 方向)。
        //   死亡态跳过(由 DieFx 控制倒地下坠，防覆盖)；击退态正常(保持当前朝向)。
        private void LateUpdate()
        {
            if (state == SoldierState.Dead) return;
            currentYaw = Mathf.MoveTowardsAngle(currentYaw, targetYaw, maxTurnSpeed * Mathf.Rad2Deg * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, currentYaw + AimOffset, 0f);
        }

        private void MuzzleFlash()
        {
            // 阶段2 接 VFXManager；Phase1 占位。可在此挂 LineRenderer/粒子。
        }

        public void TakeDamage(float dmg, Vector3 hitDir = default)
        {
            if (state == SoldierState.Dead || tag == null) return;
            tag.hp = Mathf.Max(0f, tag.hp - dmg);
            // ★任务3：取消额外红/金"字体"受击特效（伤害飘字 FloatingText.Show）——用户要求移除；
            //   保留 F6 受击特效 HitFXManager.Play（用户指定要添加的程序化粒子受击特效，非字体）。
            HitFXManager.Play(team, transform.position + Vector3.up * 1.2f);
            // P6(F5) 接线：士兵受击 -> 占位音
            if (AudioController.Instance != null) AudioController.Instance.PlayHitSfx(transform.position + Vector3.up * 1.2f);
            // 受击闪红（文部 §七 P0，G4 白→红）——HitFlash 内改 _Color=红
            if (_flash == null) _flash = StartCoroutine(HitFlash());   // ★S4：防重入(密集命中不打断首闪, 防 old 被污染成红)
            // 击退瞬时事件（工部二 §13.2-③）：沿子弹来向反向一次性位移 + 0.1s 击退态（跳 AI Move，防被覆盖）
            if (hitDir.sqrMagnitude > 1e-6f)
            {
                transform.position += -hitDir.normalized * knockbackDist;
                _knockbackUntil = Time.time + knockbackTime;
            }
            if (tag.hp <= 0f) Die();
        }

        // 死亡：击杀广播 + 死亡动画（倒地/消散）播完才回池（R3-E1）
        // fbx 无死亡 clip、Animator 无 motion 建不成 Death 态 → 改代码驱动倒地+下坠+压扁，0.5s 后回池。
        public void Die()
        {
            if (state == SoldierState.Dead) return;
            SetState(SoldierState.Dead);
            if (tag != null) tag.isAlive = false;
            if (_halo != null) _halo.gameObject.SetActive(false);   // 任务4：死亡隐藏士兵脚下光环
            CombatSystem.NotifySoldierKilled(this);
            if (_anim != null) _anim.SetBool("Moving", false);  // 死亡时停 walk
            // D1 最小侵入（轴8 允许）：死亡烟雾（team0 金白/1 红黑）
            DeathFXManager.PlayDeath(transform.position, team);
            // P1-4 接线：士兵死亡 -> death.wav（对位 Web death.wav；参照 PlayHitSfx L145 触发写法，一行接入不高频）
            if (AudioController.Instance != null) AudioController.Instance.PlayDeathSfx(transform.position);
            // R3-E1 死亡动画：倒地(forward)→90° + 压扁 scale.y→0.1 + 下坠，0.5s 后回池（延迟）
            StartCoroutine(DieFx());
        }

        // 代码驱动死亡动画（弃 Animator Death 态，规避无 clip）；增强 0.8s + 颜色 fade（R2/契约2，对位 soldier_death.json com_yanwu 消散）。
        // 回池由 factory 复位 transform；fade 用材质实例色（不动 sharedMaterial），结束复位 opage 防池化残留透明。
        private IEnumerator DieFx()
        {
            const float dieDur = 0.8f;   // R2：0.5s→0.8s（对位 soldier_death 消散时长）
            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;
            Vector3 startScale = transform.localScale;
            Vector3 targetPos = startPos + Vector3.up * -0.5f;
            Quaternion targetRot = Quaternion.Euler(90f, startRot.eulerAngles.y, 0f);   // 倒下前倾
            Vector3 targetScale = new Vector3(startScale.x * 0.1f, startScale.y * 0.1f, startScale.z * 0.1f); // 压扁+坍缩消散

            // 捕获可 fade 渲染器的基色（材质实例，一次性捕获防逐帧 alloc）
            var rends = GetComponentsInChildren<Renderer>();
            Color[] baseCol = new Color[rends.Length];
            bool[] canFade = new bool[rends.Length];
            for (int i = 0; i < rends.Length; i++)
            {
                var mat = rends[i].material;   // 实例化，勿碰 sharedMaterial
                canFade[i] = mat.HasProperty("_BaseColor");
                if (canFade[i]) baseCol[i] = mat.GetColor("_BaseColor");
            }

            float t = 0f;
            while (t < dieDur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dieDur);
                transform.position = Vector3.Lerp(startPos, targetPos, k);
                transform.rotation = Quaternion.Slerp(startRot, targetRot, k);
                transform.localScale = Vector3.Lerp(startScale, targetScale, k);
                // fade：后 50% 淡出到透明（不透明材质颜色 alpha 无视觉，但坍缩 scale 已表达消散）
                float fade = k < 0.5f ? 1f : Mathf.Clamp01(1f - (k - 0.5f) / 0.5f);
                for (int i = 0; i < rends.Length; i++)
                {
                    if (!canFade[i]) continue;
                    rends[i].material.SetColor("_BaseColor", new Color(baseCol[i].r, baseCol[i].g, baseCol[i].b, baseCol[i].a * fade));
                }
                yield return null;
            }
            // 复位不透明，防池化复用残留透明态（Y3 回归）
            for (int i = 0; i < rends.Length; i++)
                if (canFade[i]) rends[i].material.SetColor("_BaseColor", baseCol[i]);
            if (factory != null) factory.NotifyDeath(this);   // 播完才回池
            else gameObject.SetActive(false);
        }

        private IEnumerator HitFlash()
        {
            var rends = GetComponentsInChildren<Renderer>();
            Color old = Color.clear; bool saved = false;
            foreach (var r in rends)
            {
                if (r.material.HasProperty("_BaseColor"))
                {
                    if (!saved) { old = r.material.GetColor("_BaseColor"); saved = true; }
                    r.material.SetColor("_BaseColor", new Color(2f, 2f, 2f, 1f));   // ★S4：受击闪白(贴图×2 泛白,对齐原版)；若 bloom 开过曝刺眼回退 Color.white(1,1,1)
                }
            }
            yield return new WaitForSeconds(hitFlashTime);
            foreach (var r in rends)
                if (saved && r.material.HasProperty("_BaseColor"))
                    r.material.SetColor("_BaseColor", old);
            _flash = null;   // ★S4：结束置空，供下次受击重新触发(配合 if(_flash==null) 防重入+保颜色每次恢复白)
        }
    }
}

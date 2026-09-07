// CombatSystem.cs —— 战斗结算中枢（静态，跨士兵/子弹/基地的单一结算出口）
// 对位 Web 战斗结算逻辑：伤害分道（对兵=damage / 对基地=baseDamage，F2）+ AOE 衰减 + 击杀/基地摧毁广播。
using System;
using UnityEngine;

namespace Liuhuo.Core
{
    public static class CombatSystem
    {
        // 击杀回调：param=阵亡士兵（SoldierFactory 订阅做计数/回收）
        public static event Action<Soldier> OnSoldierKilled;
        // 基地摧毁回调：param=被摧毁阵营(0=天庭/1=玄朝)。GameManager 订阅做胜负判定。
        public static event Action<int> OnBaseDestroyed;

        // 士兵池引用（SoldierFactory Awake 注入，避免静态直接依赖场景对象）
        private static SoldierFactory _factory;

        public static void RegisterFactory(SoldierFactory f) { _factory = f; }

        // 发弹：从池取一颗弹，沿 dir 飞（Soldier.Attack 调用）
        public static void FireBullet(Vector3 origin, int team, WeaponConfig w, Vector3 dir)
        {
            if (_factory == null) return;
            Bullet b = _factory.GetBullet(team);
            if (b == null) return;
            b.transform.position = origin;                                        // R1 关键修复：子弹从枪口出生（origin 传入却从未使用，致子弹从工厂(0,0,0)起飞错位）
            b.transform.rotation = Quaternion.LookRotation(dir.normalized);      // 朝向目标（同步弹体朝向，修正池复用漂移）
            b.Launch(team, w, dir.normalized);
        }

        // 命中结算：士兵取对兵伤害(+AOE)/基地取 baseDamage（F2 分道）
        // 士兵伤害统一走 Soldier.TakeDamage → Die()（唯一死亡路径，保证回池+广播）
        public static void ResolveHit(Bullet b, Collider c)
        {
            // G3 兜底命中计数：士兵已加 CapsuleCollider 仍零命中时，靠此 log 定位子弹到底触发了谁的 Collider
            // S5 R4 块P1-7 C1 数值探针：log 带伤害数值，供 player.log 与 Web 数值对齐（A对兵伤害/B基地baseDamage）
            Debug.Log($"[ResolveHit] bullet={b.name} team={b.team} dmg={b.soldierDamage} aoe={b.aoeRadius} baseDmg={b.baseDamage} hitCol={c.gameObject.name} hasSoldierTag={(c.GetComponentInParent<SoldierTag>() != null)}");
            // A4（P0-6 建筑避让，对位 BuildingManager L197 建议）：士兵被楼宇挡住不应吃到子弹伤害（基地是主目标不拦，保持 Web 语义）
            if (c.GetComponentInParent<SoldierTag>() != null &&
                BuildingManager.isBulletBlocked(b.transform.position, c.gameObject.transform.position))
            {
                b.Release();
                return;
            }
            var soldierTag = c.GetComponentInParent<SoldierTag>();
            if (soldierTag != null && soldierTag.isAlive)
            {
                // 敌我判定：打自己人无效（对位 Web 阵营隔离）
                if (soldierTag.team != b.team)
                {
                    Soldier s = soldierTag.GetComponent<Soldier>();
                    // 有行为层走 Soldier.TakeDamage（触发 Die→回池）；无则回退直接扣血
                    // s5 v3.2 击退：传子弹飞行方向的反向（沿来向击退），非零才触发位移
                    if (s != null) s.TakeDamage(b.soldierDamage, b.Dir);
                    else ApplySingleTargetDamage(soldierTag, b);
                    b.Release();   // S5 R4 self-hit修复（工部二◎1）：仅敌我命中才吞弹（原 L56 移入敌我分支）
                }
                // team== 己方命中：跳伤害 + 不 Release → 弹继续穿透飞向敌方；必须 return 防掉到 base 分支 L67 再次吞弹
                return;
            }

            var baseSys = c.GetComponentInParent<BaseSystem>();
            if (baseSys != null && !baseSys.IsDestroyed)
            {
                // F2：基地只用 baseDamage
                baseSys.ApplyHit(b.soldierDamage, b.baseDamage);
                if (baseSys.IsDestroyed) OnBaseDestroyed?.Invoke(baseSys.Team);
            }
            b.Release();
        }

        // 无 Soldier 行为层时的兜底：直接扣血量（AOE 溅射仍走此处）
        private static void ApplySingleTargetDamage(SoldierTag target, Bullet b)
        {
            target.TakeDamage(b.soldierDamage);
            ApplyAOESplash(target, b);
        }

        // AOE：半径内对敌兵溅射（falloff 衰减）
        private static void ApplyAOESplash(SoldierTag epicenter, Bullet b)
        {
            if (b.aoeRadius <= 0f) return;
            var colliders = Physics.OverlapSphere(epicenter.transform.position, b.aoeRadius);
            foreach (var col in colliders)
            {
                var t2 = col.GetComponentInParent<SoldierTag>();
                if (t2 == null || t2 == epicenter || !t2.isAlive || t2.team == b.team) continue;
                float dmg = b.soldierDamage * b.aoeFalloff;
                Soldier s2 = t2.GetComponent<Soldier>();
                Vector3 aoeDir = (t2.transform.position - epicenter.transform.position);   // 击退远离爆心
                if (s2 != null) s2.TakeDamage(dmg, aoeDir);
                else t2.TakeDamage(dmg);
            }
        }

        // 击杀广播出口（Soldier.Die 调用；SoldierTag 兜底路径也走此）
        public static void NotifySoldierKilled(Soldier s)
        {
            OnSoldierKilled?.Invoke(s);
        }
    }
}

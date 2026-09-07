// SoldierAI.cs —— 士兵 AI 决策（寻敌三档、分离力、卡住恢复）
// 对位 Web 士兵 AI：1)攻击范围内最近敌兵 2)全图最近敌兵 3)冲敌基地。含分离力(sepRadius=5/sepDist=2/sepWeight=3)。
using UnityEngine;

namespace Liuhuo.Core
{
    [RequireComponent(typeof(Soldier))]
    public class SoldierAI : MonoBehaviour
    {
        [Header("引用")]
        public Soldier soldier;

        [Header("分离力(对位 Web sep*)")]
        public float sepRadius = 5f;      // 同桌分离检测半径
        public float sepDistance = 2f;    // 期望保持距离
        public float sepWeight = 1.2f;    // 分离力权重(C4修复R1: 3->1.2 防密集区分离力主导压制推进)

        [Header("卡住恢复")]
        public float stillTime = 1.5f;    // 判定为卡住的时长
        public float stuckForce = 6f;     // 卡住时的推开力

        [Header("攻基地加成(契约5，对位 soldierAI.js L389 getBaseHitRadius)")]
        [Tooltip("士兵在基地 Collider 外缘即可开火，防挤进中心停滞；仅攻基地分支生效，不破互歼")]
        public float baseHitR = 8f;

        private Vector3 _lastPos;
        private float _stillTimer;
        private Transform _target;                 // 第五轮 P1-3：索敌目标缓存（限频重搜）
        private float _searchTimer = 0f;           // 索敌限频计时
        private const float SearchInterval = 0.3f; // 对位 soldierAI.js nextSearchTime+第五轮限频 0.3s

        private void Awake()
        {
            soldier = GetComponent<Soldier>();
            _lastPos = transform.position;
        }

        private void Update()
        {
            if (soldier == null || soldier.state == SoldierState.Dead) return;

            // 目标失效重获取：缓存目标死了/被摧毁/超出攻距×1.5 -> 立即重搜（G3 防追打尸体）
            if (_target != null && !IsTargetValid(_target))
            {
                Debug.Log($"[AI] T{(soldier.tag != null ? soldier.tag.soldierId : -1)} 目标丢失，强制重搜");
                _target = null;
            }

            // 索敌限频：每 SearchInterval 秒全量重搜，其余帧用缓存目标（O(n²)->O(n²/0.3+1)）
            _searchTimer -= Time.deltaTime;
            if (_searchTimer <= 0f || _target == null)
            {
                _searchTimer = SearchInterval;
                _target = PickTarget();
            }

            if (_target != null)
            {
                float dist = Vector3.Distance(transform.position, _target.position);
                // 契约5：攻敌基地时加 baseHitR（对位 soldierAI.js L391 dist<=attackRange*mul+baseHitR），
                // 士兵在基地 Collider 外缘即可开火，防"挤不进中心停滞"（#54 设计使然的攻距内停射保留）
                float reach = soldier.EffectiveAttackRange;
                if (soldier.enemyBase != null && _target == soldier.enemyBase.transform) reach += baseHitR;
                if (dist <= reach)
                {
                    soldier.Attack(_target);   // 攻距内：攻击
                    return;
                }
            }

            // 未到攻距：朝目标移动 + 分离力
            // C4修复R1：目标方向推进分量×2.0 压制加法型分离力(原模1)，防密集区分离力主导致净moveDir≈0 卡死；
            // 配合 sepWeight 3->1.2。RVO/ORCA 修正型治本留 Phase-C(工部二认可)。
            Vector3 moveDir = _target != null
                ? (_target.position - transform.position).normalized * 2.0f
                : Vector3.zero;
            // B 方案·攻基地僵局修复(S5 debugger 定位)：target==enemyBase 时跳过分离力+建筑避让。
            // 根因：攻基地判距 = dist(兵,baseRoot) <= attackRange*mul+baseHitR(≈29~37)，但密集残阵下
            // SeparationForce+avoidDirection 会把 moveDir 压到≈0(Soldier.cs 只在 >0.0001 才移) → 士兵停在
            // 基地外围(z≈41, 距中心41>37)打不到 → 基地HP冻结(实测玄朝卡8200)。基地 isTrigger 不阻挡物理、
            // 且不在 BuildingBounds(非楼宇) → 二者皆无必要，攻基地纯朝基地推进即可进入判定域开火。
            if (soldier.enemyBase != null && _target == soldier.enemyBase.transform)
            {
                moveDir.y = 0f;
            }
            else
            {
                moveDir += SeparationForce();
                // A4（P0-6 建筑避让，对位 BuildingManager L193 建议）：移动方向避开最近建筑（内置 XZ 拍平/归一化）
                moveDir = BuildingManager.avoidDirection(transform.position, moveDir);
                moveDir.y = 0f;
            }

            DetectStuck(ref moveDir);

            soldier.MoveUpdate(Time.deltaTime, moveDir);
            _lastPos = transform.position;
        }

        // 目标有效性守卫：基地未摧毁 / 士兵存活（G3 防缓存尸体）
        private bool IsTargetValid(Transform t)
        {
            if (t == null) return false;
            if (soldier.enemyBase != null && t == soldier.enemyBase.transform) return !soldier.enemyBase.IsDestroyed;
            var st = t.GetComponent<SoldierTag>();
            return st != null && st.isAlive;
        }

        // 寻敌三档：攻距内最近敌兵→全图最近敌兵→null(去敌基地)
        // 注意：ActiveSoldiers 是 List<Soldier>(元素=Soldier)，非 SoldierTag
        private Transform PickTarget()
        {
            Soldier nearestInRange = null; float nearestInRangeDist = float.MaxValue;
            Soldier nearestAny = null; float nearestAnyDist = float.MaxValue;
            var list = SoldierFactory.ActiveSoldiers;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s == null || s.tag == null || !s.tag.isAlive || s.team == soldier.team) continue;
                float d = Vector3.Distance(transform.position, s.transform.position);
                if (d <= soldier.EffectiveAttackRange && d < nearestInRangeDist)
                { nearestInRangeDist = d; nearestInRange = s; }
                if (d < nearestAnyDist) { nearestAnyDist = d; nearestAny = s; }
            }
            if (nearestInRange != null) return nearestInRange.transform;
            if (nearestAny != null) return nearestAny.transform;
            // 无活敌：冲敌基地
            if (soldier.enemyBase != null && !soldier.enemyBase.IsDestroyed)
                return soldier.enemyBase.transform;
            return null;
        }

        // 简单分离力：最近同桌
        private Vector3 SeparationForce()
        {
            Vector3 sep = Vector3.zero;
            var list = SoldierFactory.ActiveSoldiers;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s == null || s.tag == null || !s.tag.isAlive || s.team != soldier.team) continue;
                float d = Vector3.Distance(transform.position, s.transform.position);
                if (d < sepRadius && d > 0.001f)
                {
                    Vector3 away = (transform.position - s.transform.position).normalized;
                    sep += away * (sepDistance / d);
                }
            }
            return sep * sepWeight;
        }

        // 卡住检测：位移过小即记时，超时加推开力
        private void DetectStuck(ref Vector3 moveDir)
        {
            float moved = Vector3.SqrMagnitude(transform.position - _lastPos);
            if (moved < 0.01f) _stillTimer += Time.deltaTime;
            else _stillTimer = 0f;
            if (_stillTimer > stillTime)
            {
                moveDir += new Vector3(Random.value - 0.5f, 0f, Random.value - 0.5f).normalized * stuckForce;
                _stillTimer = 0f; // 重置一次推开
            }
        }
    }
}

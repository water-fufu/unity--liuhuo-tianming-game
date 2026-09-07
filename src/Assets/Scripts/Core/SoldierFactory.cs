// SoldierFactory.cs —— 士兵/子弹对象池 + 召唤中枢（对位 Web SummonZone/SoldierPool）
// G1: 上限50且无召唤冷却（只判 soldierCount<50）；G3: 召唤区 center X=±30/Z=±35, 20×50（简化红蓝各1区）。
using System.Collections.Generic;
using UnityEngine;

namespace Liuhuo.Core
{
    public class SoldierFactory : MonoBehaviour
    {
        [Header("预制体")]
        public Soldier bluePrefab;   // 天庭(蓝)士兵
        public Soldier redPrefab;    // 玄朝(红)士兵
        [Header("武器配置(可在 Inspector 挂 ScriptableObject)")]
        public WeaponConfig blueWeapon;
        public WeaponConfig redWeapon;

        [Header("召唤区(G3: center X=±30/Z=±35, 20×50)")]
        public Vector3 blueZoneCenter = new Vector3(30f, 0f, 35f);
        public Vector3 redZoneCenter = new Vector3(-30f, 0f, 35f);
        public float zoneSizeX = 20f;
        public float zoneSizeZ = 50f;

        [Header("上限(G1: 50, 无冷却)")]
        public int maxSoldiers = 50;

        [Header("基地引用(由 GameManager 注入)")]
        public BaseSystem blueBase;   // 天庭
        public BaseSystem redBase;    // 玄朝

        // 士兵池：蓝/红 各一份
        private readonly Stack<Soldier> _bluePool = new Stack<Soldier>();
        private readonly Stack<Soldier> _redPool = new Stack<Soldier>();
        private readonly List<Soldier> _active = new List<Soldier>();
        private int _idCounter = 0;

        // 可供遍历的活动士兵（SoldierAI 寻敌用）
        public static List<Soldier> ActiveSoldiers { get; private set; } = new List<Soldier>();

        // 弹体池
        private readonly Stack<Bullet> _bulletPool = new Stack<Bullet>();
        [Header("弹体预制体")] public Bullet bulletPrefab;

        private void Awake()
        {
            ActiveSoldiers = _active;
            CombatSystem.RegisterFactory(this);
            if (blueWeapon == null) blueWeapon = ScriptableObject.CreateInstance<WeaponConfig>();
            if (redWeapon == null) redWeapon = WeaponConfig.CreateMachineGun();
        }

        // 召唤士兵：G1 上限50无冷却；G3 在指定区随机点出生
        public Soldier Summon(int team)
        {
            int count = GetAliveCount(team);
            if (count >= maxSoldiers) return null;   // G1
            Soldier prefab = team == 0 ? bluePrefab : redPrefab;
            if (prefab == null) return null;
            WeaponConfig w = team == 0 ? blueWeapon : redWeapon;

            Soldier s = PopOrInstantiate(team == 0 ? _bluePool : _redPool, prefab, team);
            Vector3 pos = RandomSpawnInZone(team);
            s.Init(_idCounter++, team, w, team == 0 ? redBase : blueBase, this);
            s.transform.SetPositionAndRotation(pos, Quaternion.identity);
            if (!_active.Contains(s)) _active.Add(s);
            return s;
        }

        // 指定出生点（调试/固定测试用）
        public Soldier SummonAt(int team, Vector3 pos)
        {
            int count = GetAliveCount(team);
            if (count >= maxSoldiers) return null;
            Soldier prefab = team == 0 ? bluePrefab : redPrefab;
            if (prefab == null) return null;
            WeaponConfig w = team == 0 ? blueWeapon : redWeapon;
            Soldier s = PopOrInstantiate(team == 0 ? _bluePool : _redPool, prefab, team);
            s.Init(_idCounter++, team, w, team == 0 ? redBase : blueBase, this);
            s.transform.SetPositionAndRotation(pos, Quaternion.identity);
            if (!_active.Contains(s)) _active.Add(s);
            return s;
        }

        private Soldier PopOrInstantiate(Stack<Soldier> pool, Soldier prefab, int team)
        {
            Soldier s = pool.Count > 0 ? pool.Pop() : Instantiate(prefab, transform);
            // R3-E1 复用复位：死亡动画改了 rotation/scale，出池须还原防「二次死亡矮子」残留
            s.transform.localRotation = Quaternion.identity;
            // 还原到 prefab 的 localScale(对位 dist10 士兵 3.5，S5_Builder.BuildSoldier 已设)，而非 Vector3.one；
            // 否则出池把士兵锁回 scale=1 → 视觉小黑点，破坏 dist10 士兵占比。
            s.transform.localScale = prefab.transform.localScale;
            s.gameObject.SetActive(true);
            s.tag = s.GetComponent<SoldierTag>();
            return s;
        }

        private Vector3 RandomSpawnInZone(int team)
        {
            Vector3 center = (team == 0 ? blueZoneCenter : redZoneCenter);
            float x = center.x + Random.Range(-zoneSizeX * 0.5f, zoneSizeX * 0.5f);
            float z = center.z + Random.Range(-zoneSizeZ * 0.5f, zoneSizeZ * 0.5f);
            return new Vector3(x, 0f, z);
        }

        // 死亡回池入口（Soldier.Die 调用）
        public void NotifyDeath(Soldier s)
        {
            if (s.team == 0) _bluePool.Push(s); else _redPool.Push(s);
            _active.Remove(s);
            s.gameObject.SetActive(false);
        }

        public int GetAliveCount(int team)
        {
            int n = 0;
            for (int i = 0; i < _active.Count; i++)
                if (_active[i] != null && _active[i].tag != null && _active[i].tag.isAlive && _active[i].team == team) n++;
            return n;
        }

        // 弹体：池取/回收
        public Bullet GetBullet(int team)
        {
            Bullet b = _bulletPool.Count > 0 ? _bulletPool.Pop() : Instantiate(bulletPrefab, transform);
            b.SetPool(this);
            b.gameObject.SetActive(true);
            return b;
        }

        public void ReleaseBullet(Bullet b)
        {
            b.gameObject.SetActive(false);
            if (!_bulletPool.Contains(b)) _bulletPool.Push(b);
        }

        // T2-3 重载：清空全部士兵/弹体对象与池（对位 Web _resetBattle 清空士兵数组 + 弹体回收）
        /// <summary>重开时清空所有活动士兵/弹体对象与池，重开后经 PopOrInstantiate 重建。</summary>
        public void ClearAll()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
                if (_active[i] != null) Destroy(_active[i].gameObject);
            _active.Clear();
            foreach (var s in _bluePool) if (s != null) Destroy(s.gameObject);
            foreach (var s in _redPool) if (s != null) Destroy(s.gameObject);
            _bluePool.Clear(); _redPool.Clear();
            foreach (var b in _bulletPool) if (b != null) Destroy(b.gameObject);
            _bulletPool.Clear();
            // ★T3(S4)：光环残留根治——士兵被 Destroy 但其光环 parented 到 factory.transform(非士兵物体)，
            //   随士兵销毁不消失，重开后仍残留到下一轮；此处显式销毁全部光环物体。
            foreach (var h in transform.GetComponentsInChildren<SoldierHalo>(true))
                if (h != null) Destroy(h.gameObject);
            Debug.Log("[SoldierFactory] ClearAll 已完成 (士兵/弹体/光环全清, 重载)");
        }
    }
}

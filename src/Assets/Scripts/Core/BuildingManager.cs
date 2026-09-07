// BuildingManager.cs —— 建筑避让（P0-6）：6 栋占位建筑 + 4 个静态查询接口
// 对位 Web bodyBlocking：士兵躲避建筑而非穿墙。建筑由纯代码 Cube 生成并注册静态 Bounds 表。
// 命名空间/风格与 SoldierAI.cs / SoldierFactory.cs / BaseSystem.cs 保持一致（namespace Liuhuo.Core）。
using System.Collections.Generic;
using UnityEngine;

namespace Liuhuo.Core
{
    /// <summary>
    /// 建筑避让管理器。
    /// 通过 Create() 建 6 栋占位建筑（双塔 X=±60 / 双祭坛 X=±30 / 双中心 X=±20），
    /// 每栋注册一个 Bounds 到静态表 BuildingBounds；提供 4 个静态查询接口供 SoldierAI/弹体使用。
    /// </summary>
    public class BuildingManager : MonoBehaviour
    {
        /// <summary>静态建筑包围盒表（XZ 足印 + Y 高度）。其他系统可只读遍历。</summary>
        public static readonly List<Bounds> BuildingBounds = new List<Bounds>();

        // ============================================================
        // 公共入口：Create —— 建 6 栋建筑并注册 Bounds
        // ============================================================
        /// <summary>
        /// 创建 6 栋占位建筑并注册包围盒。返回本管理器实例（挂在根 GameObject 上）。
        /// 调用方（如 GameManager/战局初始化）在场景运行时调用一次即可。
        /// </summary>
        public static BuildingManager Create()
        {
            // 重复创建不累积：先清空旧的包围盒表
            BuildingBounds.Clear();

            GameObject root = new GameObject("BuildingManager");
            BuildingManager mgr = root.AddComponent<BuildingManager>();
            mgr.BuildAll(root.transform);
            return mgr;
        }

        // ============================================================
        // 建筑几何数据（位置/尺寸/颜色），全部立于地面（Y 底部=0）
        // ============================================================
        private void BuildAll(Transform parent)
        {
            // ★T2（第七轮 用户十项任务 第2项）：地图上仅保留双方基地模型，其他建筑全部删除。
            //   故不再 BuildCube 生成 6 栋占位建筑（双塔/双祭坛/双中心）。BuildingBounds 保持空表，
            //   下游 isBulletBlocked/avoidDirection/findNearestBlocked 对空表安全：
            //   - isBulletBlocked：for 循环 0 次 → 恒 false（无建筑挡子弹）
            //   - findNearestBlocked：0 次 → 返回 default(Bounds)，IsValid=false
            //   - avoidDirection：IsValid(nearest)=false → 直接 return desiredDir（士兵直线走）
            //   弹体命中路径（CombatSystem.ResolveHit）对 base 分支本就走 BaseSystem，不依赖本表。
            //   BuildCube/MakeMaterial 保留备用（后续若重建建筑可复用）。
        }

        /// <summary>建一栋 Cube 占位建筑（自带 BoxCollider，含实体碰撞），并注册其 Bounds。</summary>
        private void BuildCube(string name, Vector3 pos, Vector3 scale, Color color, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;

            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = MakeMaterial(color);

            // 注册 Bounds（center=pos，size=scale，与 Cube 实际 AABB 一致）
            BuildingBounds.Add(new Bounds(pos, scale));
        }

        /// <summary>生成纯色材质：优先 URP Lit，回退 Standard/Unlit-Color。</summary>
        private static Material MakeMaterial(Color c)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            Material mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            else mat.color = c;
            return mat;
        }

        // ============================================================
        // 4 个静态查询接口
        // ============================================================

        /// <summary>
        /// 该点是否被任意建筑挡住（仅看 XZ 足印，忽略 Y——士兵行走于地面）。
        /// </summary>
        public static bool isBlocked(Vector3 pos)
        {
            for (int i = 0; i < BuildingBounds.Count; i++)
                if (InsideXZ(BuildingBounds[i], pos)) return true;
            return false;
        }

        /// <summary>
        /// 找到离 pos 最近的建筑包围盒（用 Bounds.SqrDistance 度量，点在内部返回该盒）。
        /// 无建筑时报 default(Bounds)（size 为零，用 IsValid 判断）。
        /// </summary>
        public static Bounds findNearestBlocked(Vector3 pos)
        {
            Bounds best = default(Bounds);
            float bestSqr = float.MaxValue;
            bool found = false;
            for (int i = 0; i < BuildingBounds.Count; i++)
            {
                float d = BuildingBounds[i].SqrDistance(pos);
                if (d < bestSqr) { bestSqr = d; best = BuildingBounds[i]; found = true; }
            }
            return found ? best : default(Bounds);
        }

        /// <summary>
        /// 返回避开建筑后的移动方向。desiredDir 为原始目标方向，若朝向/处于建筑则施加背离建筑的推离分量。
        /// 结果已归一并拍平 Y。
        /// </summary>
        public static Vector3 avoidDirection(Vector3 pos, Vector3 desiredDir)
        {
            Bounds nearest = findNearestBlocked(pos);
            if (!IsValid(nearest)) return desiredDir;

            Vector3 center = nearest.center;
            Vector3 away = pos - center;
            away.y = 0f;
            Vector3 desired = desiredDir;
            desired.y = 0f;

            // 处于建筑内 → 强推离；否则温和推离
            bool blocked = isBlocked(pos);
            float strength = blocked ? 1.0f : 0.5f;

            // 若 desiredDir 指向建筑中心，额外增强推离（防迎面撞墙）
            Vector3 toCenter = center - pos;
            toCenter.y = 0f;
            if (desired.sqrMagnitude > 1e-4f && toCenter.sqrMagnitude > 1e-4f)
            {
                float facing = Vector3.Dot(desired.normalized, toCenter.normalized);
                if (facing > 0f) strength += facing;
            }

            Vector3 result = desired + away.normalized * strength;
            result.y = 0f;
            if (result.sqrMagnitude < 1e-6f) return desiredDir;
            return result.normalized;
        }

        /// <summary>
        /// 线段 from→to 是否被任一楼宇阻挡。用 Bounds.IntersectRay 判断射线进入建筑，且命中点落在线段范围内。
        /// 用于弹体命中判定前剔除被建筑挡住的子弹。
        /// </summary>
        public static bool isBulletBlocked(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            float maxDist = dir.magnitude;
            if (maxDist < 1e-5f) return isBlocked(from);   // 零长线段退化为点判定

            Ray ray = new Ray(from, dir.normalized);
            for (int i = 0; i < BuildingBounds.Count; i++)
            {
                if (BuildingBounds[i].IntersectRay(ray, out float dist) && dist <= maxDist + 0.01f)
                    return true;
            }
            return false;
        }

        // ============================================================
        // 内部帮助
        // ============================================================
        private static bool InsideXZ(Bounds b, Vector3 p)
        {
            return p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z;
        }

        private static bool IsValid(Bounds b)
        {
            return b.size.sqrMagnitude > 0.001f;   // default(Bounds) size 为零 → 无效
        }

        // ============================================================
        // SoldierAI 集成点（TODO：接入时把下方代码放对位 moveDir 组装后）
        // ------------------------------------------------------------
        // SoldierAI.Update() 中，在分离力组合 + moveDir.y=0 之后、DetectStuck 之前调用：
        //
        //   // P0-6 建筑避让：移动方向避开最近建筑
        //   moveDir = BuildingManager.avoidDirection(transform.position, moveDir);
        //
        // 说明：avoidDirection 内部已做 XZ 拍平与归一化，直接覆盖 moveDir 即可；
        // 弹体侧若需拦截被建筑挡住的子弹，命中结算前调用
        //   if (BuildingManager.isBulletBlocked(from, to)) return;  // 子弹被楼宇挡下。
        // ============================================================
    }
}

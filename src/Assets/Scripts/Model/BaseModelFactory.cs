// BaseModelFactory.cs —— 基地模型工厂（对位 3D 基地外观）
// ★用户需求(2026-09-06)：用桌面两份 3D 模型 zip(天庭/玄朝) 替换原 Primitive 组合基地。
//   - 天庭基地(team=0)：Assets/Resources/Bases/TianTingBase/天庭3d模型.fbx
//   - 玄朝基地(team=1)：Assets/Resources/Bases/XuanChaoBase/玄朝基地3d模型.fbx
// 模型 FBX 自带 *_basecolor.jpg 贴图(Unity 按相邻 .fbm 目录自动匹配)。
// 两基地均：挂 BaseSystem 并设 hp/team；受击(onDamaged)时模型发光材质朝白色脉冲(闪白)。
// 碰撞：根挂 trigger BoxCollider 供子弹 OnTriggerEnter 命中结算；子物体 Collider 不处理(统一走根 trigger)。
// 公共入口 CreateBase(int team, Vector3 pos) 返回挂好 BaseSystem 的根，供主会话 GameBootstrap 接线 blueBase/redBase。
//
// 碰撞说明：基地要能被子弹命中并结算伤害，须满足 Bullet.OnTriggerEnter(Collider)→CombatSystem.ResolveHit
//   → c.GetComponentInParent<BaseSystem>()（见 Bullet.cs / CombatSystem.cs）。因此根物体放一个 trigger BoxCollider
//   与 BaseSystem，子物体 Primitive 的自动 Collider 全部移除(碰撞统一走根 trigger)，避免子弹重复/误触子碰撞体。
using System.Collections.Generic;
using UnityEngine;
using Liuhuo.Core;

namespace Liuhuo.Model
{
    public static class BaseModelFactory
    {
        // ================= 公共入口 =================
        /// <summary>创建基地并返回挂好 BaseSystem 的根 GameObject 上的组件（供 GameBootstrap 接线 blueBase/redBase）。</summary>
        /// <param name="team">0=天庭(蓝) / 1=玄朝(红)</param>
        /// <param name="pos">基地根位置</param>
        public static BaseSystem CreateBase(int team, Vector3 pos)
        {
            return CreateBase(team, pos, Quaternion.identity, Vector3.one);
        }

        // ★任务1(2026-09-06)：按用户 Inspector 参数精确摆放基地（Position/Rotation/Scale）。
        //   root 持 position(用户 Position)；模型的 rotation/scale 交子物体(LoadFbxBase)；root 保持轴对齐以便 BoxCollider 轴对齐近似。
        //   pos=用户 Position(天庭 38.1,0,71.4 / 玄朝 -28.7,0,71.3)；rot=用户 Rotation(天庭 0,-178.387,0 / 玄朝 0.5,-182.3,-0.03)；
        //   scale=用户 Scale(双方均 40,40,40)。
        public static BaseSystem CreateBase(int team, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var root = new GameObject(team == 0 ? "TianTingBase" : "XuanChaoBase");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.identity;   // root 轴对齐，模型旋转交 LoadFbxBase

            var baseSys = root.AddComponent<BaseSystem>();
            baseSys.SetTeam(team);          // BaseSystem.team 为 private[SerializeField]，只能走 SetTeam(int)
            baseSys.hp = baseSys.maxHp;     // 显式设 hp(默认 10000)，对齐 BaseSystem 字段

            var flash = root.AddComponent<BaseHitFlash>();
            flash.Bind(baseSys);

            // 根触发碰撞体：让子弹 OnTriggerEnter 命中本基地（CombatSystem.ResolveHit 用 GetComponentInParent<BaseSystem> 找到根）
            var rootCol = root.AddComponent<BoxCollider>();
            rootCol.isTrigger = true;
            rootCol.center = new Vector3(0f, 4f, 0f);
            rootCol.size = new Vector3(14f, 10f, 14f);

            // 基地外观
            if (team == 0) BuildTianTing(root.transform, flash, rot, scale);
            else BuildXuanChao(root.transform, flash, rot, scale);

            return baseSys;
        }

        /// <summary>便捷重载：省略 pos，用默认位置（天庭 -40,0,0 / 玄朝 +40,0,0）。</summary>
        public static BaseSystem CreateBase(int team)
        {
            return CreateBase(team, team == 0 ? new Vector3(-40f, 0f, 0f) : new Vector3(40f, 0f, 0f));
        }

        // ================= 天庭(蓝) 基地模型 =================
        private static void BuildTianTing(Transform parent, BaseHitFlash flash, Quaternion rot, Vector3 scale)
        {
            // ★用户：用桌面 3D 模型 zip 替换 Primitive 基地。FBX 自带 _basecolor.jpg 贴图，Unity 按相邻 .fbm 自动匹配。
            LoadFbxBase(parent, flash, "Bases/TianTingBase/天庭3d模型", rot, scale);
        }

        // ================= 玄朝(红) 基地模型 =================
        private static void BuildXuanChao(Transform parent, BaseHitFlash flash, Quaternion rot, Vector3 scale)
        {
            LoadFbxBase(parent, flash, "Bases/XuanChaoBase/玄朝基地3d模型", rot, scale);
        }

        // ================= 通用加载 =================
        /// <summary>用 Resources.Load 加载 FBX 基地模型并挂到 root 下：应用用户 Inspector Transform + 居中贴地 + 遍历材质登记受击闪白。</summary>
        /// <param name="parent">基地根(已挂 BaseSystem/trigger BoxCollider)。</param>
        /// <param name="flash">BaseHitFlash，用于登记模型发光材质实现受击闪白。</param>
        /// <param name="resPath">Resources 内路径(不含扩展名)，如 "Bases/TianTingBase/天庭3d模型"。</param>
        /// <param name="rot">用户 Rotation(相对 root，因 root 无旋转故=世界旋转)。</param>
        /// <param name="scale">用户 Scale。</param>
        private static void LoadFbxBase(Transform parent, BaseHitFlash flash, string resPath, Quaternion rot, Vector3 scale)
        {
            var model = Resources.Load<GameObject>(resPath);
            if (model == null)
            {
                Debug.LogError($"[BaseModelFactory] 未加载到基地模型: {resPath}");
                return;
            }

            var addit = (GameObject)Object.Instantiate(model, parent);
            addit.name = model.name;

            // ★任务1：按用户 Inspector 参数应用模型 Transform（替换原归一化缩放 targetMax=11）。
            addit.transform.localScale = scale;              // 用户 Scale(如 40,40,40)
            addit.transform.localRotation = rot;             // 用户 Rotation(如 Y-178.387)
            addit.transform.localPosition = Vector3.zero;    // 位置由 root 持(用户 Position)

            // 居中+贴地：模型几何中心对齐 root 原点(x,z) 且底部贴 root 底面(y)。
            // root 无旋转，故 world bounds 可直接换算；scale 后模型可能偏离局部原点，此处校正。
            Bounds b = new Bounds();
            bool hasBounds = false;
            foreach (var r in addit.GetComponentsInChildren<Renderer>(true))
            {
                if (hasBounds) b.Encapsulate(r.bounds);
                else { b = r.bounds; hasBounds = true; }
            }
            if (hasBounds)
            {
                Vector3 center = b.center - parent.position;   // 相对 root 的几何中心
                float lift = parent.position.y - b.min.y;       // 底部抬到 root 底面
                addit.transform.localPosition = new Vector3(-center.x, lift, -center.z);
                // 注：lift(世界 y) 直接赋给 local y——root 无旋转/无缩放(Scale1) 时 world=local+pos 等价；
                //   用户旋转仅绕 Y(天庭)或轻微俯仰(玄朝 0.5°)，不影响 y 分量，误差可忽略。
            }

            // 登记模型发光材质到 BaseHitFlash，实现受击闪白(用户确认保留闪白逻辑)
            if (flash != null)
            {
                foreach (var r in addit.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m != null && m.HasProperty("_BaseColor") || (m != null && m.HasProperty("_Color")))
                            flash.AddGlow(m);
                    }
                }
            }
        }
    }

    // ================= 受击闪白组件(挂根, 订阅 BaseSystem.onDamaged) =================
    /// <summary>
    /// 基地受击闪白：订阅 BaseSystem.onDamaged，把发光件主色朝白色脉冲再回落。
    /// 用主色颜色脉冲(而非 MaterialPropertyBlock)保证跨 URP/Lit、URP/Unlit、Standard 均生效且无需改现有 shader。
    /// </summary>
    public class BaseHitFlash : MonoBehaviour
    {
        private BaseSystem _base;
        private readonly List<Material> _glowMats = new List<Material>();
        private readonly List<Color> _baseColors = new List<Color>();
        private readonly List<string> _colorProps = new List<string>();   // 每材质主色属性名(_BaseColor/_Color)
        private float _flash = 0f;
        private static readonly Color FlashColor = Color.white;

        /// <summary>绑定 BaseSystem 并订阅受击(需在 AddComponent 后立即调用)。</summary>
        public void Bind(BaseSystem baseSys)
        {
            _base = baseSys;
            if (_base != null && _base.onDamaged != null) _base.onDamaged.AddListener(OnDamaged);
        }

        /// <summary>登记发光件：受击时按主色脉冲闪白。</summary>
        public void AddGlow(Material mat)
        {
            if (mat == null) return;
            string prop = mat.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
            _glowMats.Add(mat);
            _colorProps.Add(prop);
            _baseColors.Add(mat.GetColor(prop));
        }

        private void OnDamaged(float _) { _flash = 1f; }

        private void Update()
        {
            if (_flash <= 0f) return;
            _flash -= Time.deltaTime * 6f;   // 衰减速度(约 0.17s 回落)
            float t = Mathf.Clamp01(_flash);
            for (int i = 0; i < _glowMats.Count; i++)
            {
                if (_glowMats[i] == null) continue;
                _glowMats[i].SetColor(_colorProps[i], Color.Lerp(_baseColors[i], FlashColor, t));
            }
            if (_flash <= 0f)
            {
                // 归位复原
                for (int i = 0; i < _glowMats.Count; i++)
                {
                    if (_glowMats[i] != null) _glowMats[i].SetColor(_colorProps[i], _baseColors[i]);
                }
            }
        }

        private void OnDestroy()
        {
            if (_base != null && _base.onDamaged != null) _base.onDamaged.RemoveListener(OnDamaged);
        }
    }
}

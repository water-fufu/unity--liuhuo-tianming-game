// VFXConfig.cs —— 数据驱动特效配置（对位 Web src3d/fx/EffectLoader + effects/*.json 契约层）
// 延续 v3.2 受击特效"数据驱动"架构：改特效免重编译，用 ScriptableObject 承载 hit.json 等价物。
// S4 §9 交付物#7：照抄 v3.2 现状（9 层 / 阵营交叉绑定 / direction 注入）。
// 注：具体层数值需从 v3.2 hit.json 导入，本文件为通用数据容器 + 阵营交叉工厂。
using System.Collections.Generic;
using UnityEngine;

namespace Liuhuo.FX
{
    // 特效层参数（对位 Web hit.json 每层：blending/lifetime/spread/size/color/count/type）
    [System.Serializable]
    public class VFXLayer
    {
        public enum LayerType { Particle, Sprite, Ring, Light }
        public LayerType type = LayerType.Particle;
        public string blending = "additive";   // additive/normal（对位 Web blend）
        public float lifetime = 1f;            // 层生命周期 s（对位 Web lifetime）
        public float spread = 0f;              // 扩散角（对位 Web spread；v3 裁决 spread:0 非 360——受击特效应线性/小扩散，非 360 球状）
                                               // 注：两个 Hit 预制体(Resources/VFX/Hit)的粒子锥形 shape 已全部落地 spread:0，此处同步默认值使契约默认与运行时实际一致
        public Vector2 size = Vector2.one;     // 尺寸（对位 Web size.start/size.end）
        public Color color = Color.white;      // 主色（对位 Web color）
        public int count = 10;                 // 粒子数（对位 Web count）
        public float opacity = 1f;             // 透明度
        public bool sizeAttenuation = false;   // v3.2 治本：false 走恒定像素（防 80m 距离剔除致 2-5px）
        public string texture;                 // 贴图路径（对位 Web ../common/ 跨阵营）
        public string rotationSpeed;           // sprite 层旋转速度（EffectLoader 新字段）
    }

    // 阵营交叉绑定（对位 v3.2：天庭兵(blue)受击→玄朝黑红 xuanchao_hit；玄朝兵(red)受击→天庭白金 tianting_hit）
    [System.Serializable]
    public class VFXFactionBind
    {
        public string tiantingHitFx = "xuanchao_hit";  // 天庭(blue)受击→玄朝黑红
        public string xuanchaoHitFx = "tianting_hit";  // 玄朝(red)受击→天庭白金
    }

    [CreateAssetMenu(fileName = "VFXConfig", menuName = "Liuhuo/VFXConfig")]
    public class VFXConfig : ScriptableObject
    {
        public string vfxId = "xuanchao_hit";   // 命名空间（对位 hit.json 顶层 name）
        public string fullName = "玄朝受击";
        public List<VFXLayer> layers = new List<VFXLayer>();

        [Header("阵营交叉（v3.2 治本）")] public VFXFactionBind factionBind = new VFXFactionBind();

        // 阵营交叉解析：给定士兵 team，返回应播放的特效 id
        public string ResolveHitFxForTeam(int team)
        {
            // team 0=天庭(blue) 受击 → 玄朝黑红；team 1=玄朝(red) 受击 → 天庭白金
            return team == 0 ? factionBind.tiantingHitFx : factionBind.xuanchaoHitFx;
        }
    }
}

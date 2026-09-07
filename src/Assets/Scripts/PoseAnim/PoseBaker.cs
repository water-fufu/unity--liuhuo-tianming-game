// PoseBaker.cs —— 姿势烘焙器（B1-T1）
// 对位 Web PoseAnimManager._mergeFBXGeometry / _normalizeGroups / _moveSoldierToPose。
// 现实约束：本机无 Web 的 SpecialOps_POSES 16 姿态 FBX，本机士兵仅 1 个带骨骼 FBX + walk 剪辑。
// 故退化为「用 walk 剪辑 SampleAnimation 在 4 个时间点采样 → SkinnedMeshRenderer.BakeMesh 生成静态姿势 Mesh」。
// 姿势集按 SoldierState 4 态划分：Idle(0)/Move(1)/Attack(2)/Dead(3)，每态 1 帧 → 渲染层 4 次 instanced 绘制。
using UnityEngine;

namespace Liuhuo.Core
{
    public static class PoseBaker
    {
        public const int POSE_COUNT = 4;                       // SoldierState {Idle, Move, Attack, Dead} 4 态
        public static readonly string[] PoseNames = { "Idle", "Move", "Attack", "Dead" };
        // walk 剪辑采样帧点（@30fps）：Idle=第0帧 / Move=第6帧 / Attack=第20帧 / Dead=第30帧
        static readonly int[] SampleFrames = { 0, 6, 20, 30 };

        /// <summary>从士兵预制体烘焙 POSE_COUNT 个姿势 Mesh。失败返回 null 项，调用方须判空。</summary>
        public static Mesh[] Bake(Soldier prefab)
        {
            Mesh[] meshes = new Mesh[POSE_COUNT];
            if (prefab == null)
            {
                Debug.LogError("[PoseBaker] prefab 为空，无法烘焙");
                return meshes;
            }

            GameObject tmp = Object.Instantiate(prefab.gameObject);
            tmp.name = "PoseBaker_Tmp";
            tmp.SetActive(true);
            try
            {
                SkinnedMeshRenderer smr = tmp.GetComponentInChildren<SkinnedMeshRenderer>();
                Animator anim = tmp.GetComponentInChildren<Animator>();
                AnimationClip walk = FindWalkClip(anim);
                if (smr == null || smr.sharedMesh == null)
                {
                    Debug.LogError("[PoseBaker] 士兵模型无 SkinnedMeshRenderer（非蒙皮？），B1 无效，回退原渲染路径");
                    return meshes;
                }

                for (int i = 0; i < POSE_COUNT; i++)
                {
                    try
                    {
                        if (walk != null) walk.SampleAnimation(tmp, SampleFrames[i] / 30f);
                        Mesh m = new Mesh();
                        m.name = "SoldierPose_" + PoseNames[i];
                        smr.BakeMesh(m);
                        meshes[i] = m;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError("[PoseBaker] 烘焙姿势 " + PoseNames[i] + " 失败: " + e.Message);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(tmp);
            }
            return meshes;
        }

        static AnimationClip FindWalkClip(Animator anim)
        {
            if (anim == null || anim.runtimeAnimatorController == null) return null;
            foreach (var c in anim.runtimeAnimatorController.animationClips)
                if (c != null && (c.name.IndexOf("walk", System.StringComparison.OrdinalIgnoreCase) >= 0
                                  || c.name.IndexOf("move", System.StringComparison.OrdinalIgnoreCase) >= 0))
                    return c;
            var clips = anim.runtimeAnimatorController.animationClips;
            return (clips != null && clips.Length > 0) ? clips[0] : null;
        }
    }
}

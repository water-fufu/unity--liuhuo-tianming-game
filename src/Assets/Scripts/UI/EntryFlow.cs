// EntryFlow.cs —— 入口链转场（对位 src3d/ui/StartScreen.js + LevelSelect.js + Transition.js 三模块，F1）
// 逻辑映射（非纯视觉）：
//   StartCollision()  = Transition.startCollision 炮弹对撞转场 1.5s（金弹右→左 / 红弹左→右 → 中央爆炸 → 白光）
//   StartRip()        = Transition.startRip 上下撕裂转场 1.2s（两半屏遮罩 + 锯齿 + 错位滑开）
//   阵营：team 'blue'=天庭金#ffd700 左侧 / team 'red'=玄朝红#ff4444 右侧（照抄 weaponConfig）
using UnityEngine;

namespace Liuhuo.UI
{
    // 转场配置常量（照抄 Transition.js CONFIG + StartScreen.js CONFIG）
    [System.Serializable] public struct EntryFlowConfig
    {
        public float collisionDur;   // 炮弹对撞总时长 1.5s
        public float chargeDur;      // 蓄力 0.2s
        public float flightDur;      // 双弹飞行 0.6s
        public float explodeAt;      // 对撞爆炸 0.8s
        public float burstDur;       // 爆炸闪光 0.35s
        public float ripDur;         // 撕裂转场 1.2s
        public float watchDogMs;     // 兜底看门狗（保证异常仍跳转）
    }

    public class TransitionSystem : MonoBehaviour
    {
        public EntryFlowConfig cfg = new EntryFlowConfig
        {
            collisionDur = 1.5f, chargeDur = 0.2f, flightDur = 0.6f,
            explodeAt = 0.8f, burstDur = 0.35f, ripDur = 1.2f, watchDogMs = 1700f
        };

        private bool _active;   // 转场进行中（锁交互，防重复触发）

        // 对位 Transition.startCollision(el, onComplete)
        public void StartCollision(System.Action onComplete)
        {
            if (_active) return;
            _active = true;
            // TODO: 阶段3 做 Canvas/UGUI 炮弹对撞动画 + 回调 onComplete
            // 时序 cfg.chargeDur -> flightDur -> explodeAt 爆炸 -> 冲击波 -> 白光(1.12s) -> onComplete
            Invoke(nameof(DoneCollision), cfg.collisionDur);
        }
        void DoneCollision() { _active = false; /* TODO: onComplete */ }

        // 对位 Transition.startRip(el, onComplete)：上下撕裂转场
        public void StartRip(System.Action onComplete)
        {
            if (_active) return;
            _active = true;
            // TODO: 阶段3 两半屏遮罩 + 锯齿 clip-path(UIShader) + 错位滑开 + onComplete
            Invoke(nameof(DoneRip), cfg.ripDur);
        }
        void DoneRip() { _active = false; /* TODO: onComplete */ }

        public bool IsActive => _active;
    }

    // 对位 StartScreen.js：对峙动画 + 环境粒子 + 开始按钮
    public class StartScreen : MonoBehaviour
    {
        public bool active = false;
        // 对峙动效：呼吸浮动 3px / 枪口脉动 0.5s（GSAP 对位 DOTween/UGUI）
        // 环境粒子 42 个尘点（对位 Canvas 尘光粒子）
        public void OnStartBtnPress()
        {
            // 点击蓄力 0.3s -> 触发炮弹对撞转场（交给 TransitionSystem）
            // 挂载引用的 TransitionSystem.StartCollision(goToLevelSelect)
        }
    }

    // 对位 LevelSelect.js：选关卡片 + 撕裂转场
    public class LevelSelect : MonoBehaviour
    {
        public void OnCardClick(string targetLevel)
        {
            // 撕裂转场 1.2s 后跳转 index（交给 TransitionSystem.StartRip）
            // 关联 GameManager.SetState(GameState.Battle)
        }
    }
}

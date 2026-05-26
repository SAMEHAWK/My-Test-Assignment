# C8 双骨架 Ragdoll 重构方案

| 项 | 内容 |
|----|------|
| 阶段 | C8 |
| 目标 | 将 Ragdoll 从 `CharacterControllerRoot` 子模块中拆出，改为双骨架物理表现系统 |
| 状态 | 已执行至 C8.6（主路径纯双骨架） |
| 关联代码 | `CharacterControllerRoot`、`CharacterRagdollSystem`、`CharacterAnimationPresenter`、`CharacterRecoveryFlow`、`CharacterControllerDebug` |

---

## 一、问题背景

当前 C7 仍然是**单骨架局部物理**：

- `VisualModel` 的同一套 Mixamo 骨骼既被 `Animator` 写入，又挂有 `Rigidbody` / `Collider` / `CharacterJoint`。
- 重击时局部骨骼切为 dynamic，父骨/邻近骨骼仍由动画驱动。
- 为避免脱节，C7 已经把动态窗口缩短，并改成旋转融合回动画，但受击部位甩动仍不明显。

这说明当前问题已经不是单个参数问题，而是架构限制：

1. **同一骨骼控制权冲突**：Animator 与 PhysX 都想控制 Transform。
2. **模块职责混杂**：`RagdollModule` 同时负责物理、选骨、恢复姿态、起身锚点；历史实现中 `AnimationModule` 也曾直接依赖 `RagdollModule.RecoveryPoseSnapshot`。
3. **Root 过重**：`CharacterControllerRoot` 直接持有 `Rigidbody[]`，还负责收集 ragdoll 刚体、对齐恢复锚点、相机跟随锚点。
4. **扩展困难**：未来若要支持不同角色、不同 ragdoll 链、不同受击策略，会继续把字段塞进 Root。

---

## 二、重构目标

### 2.1 功能目标

- 重击时受击部位有更明显、更稳定的局部物理甩动。
- 击倒时可全身 ragdoll，并保持现有沉降、起身、相机跟随能力。
- 起身仍可做 pose match，避免从 ragdoll 姿态瞬间跳到 GetUp 动画。
- 后续可扩展不同角色、不同骨骼映射、不同受击链配置。

### 2.2 架构目标

- `CharacterControllerRoot` 只负责玩法状态、输入许可、平衡值、模块调度。
- `AnimationModule` 只负责 Animator 表现与动画事件，不直接知道物理刚体。
- `RecoveryModule` 只负责起身流程状态，不直接采样 ragdoll 骨骼。
- 新增独立 `RagdollSystem`，集中负责双骨架映射、物理骨架、姿态同步和 ragdoll 生命周期。
- 调试面板通过 Root 的调试 API 发起受击，不直接操作 ragdoll 内部。

---

## 三、推荐方案：动画主导 + 隐藏物理骨架

本项目不建议一步到位做完整 Active Ragdoll（持续用 joint drive 追动画），因为调参成本高，风险大。推荐先做更可控的双骨架方案：

> **可见动画骨架负责日常表现，隐藏物理骨架负责受击模拟；受击时把物理姿态融合回可见骨架。**

### 3.1 Player 层级建议

```text
Player
├── CharacterControllerRoot
├── PlayerInputReader
├── CharacterControllerDebug
├── UnityEngine.CharacterController
├── VisualModel
│   ├── Animator
│   └── Armature / mixamorig:*        // 可见动画骨架
└── RagdollRig
    └── Armature / mixamorig:*        // 隐藏物理骨架，结构与 VisualModel 对齐
        └── Rigidbody / Collider / Joint
```

### 3.2 骨架职责

| 骨架 | 是否可见 | 驱动来源 | 主要职责 |
|------|----------|----------|----------|
| `VisualModel` | 是 | Animator + ragdoll 回写 | 玩家最终看到的角色姿态 |
| `RagdollRig` | 否 | PhysX / 姿态同步 | 重击局部模拟、击倒全身模拟、沉降与恢复姿态来源 |

### 3.3 运行模式

| 模式 | VisualModel | RagdollRig | 说明 |
|------|-------------|------------|------|
| `Animated` | Animator 驱动 | Kinematic，跟随 Visual | 日常移动、轻击、装备动画 |
| `PartialRagdoll` | Animator + 局部物理覆盖 | 命中链 dynamic | 重击局部甩动 |
| `FullRagdoll` | 跟随 RagdollRig | 全身 dynamic | 击倒、强制击倒 |
| `PoseMatching` | 从 ragdoll 姿态混到 GetUp 首帧 | Kinematic 或冻结 | 起身前姿态匹配 |
| `Recovering` | Animator 播 GetUp | Kinematic，跟随 Visual | 起身动画阶段 |

---

## 四、模块边界设计

### 4.1 `CharacterControllerRoot`

保留职责：

- 持有 `CharacterStateMachine`。
- 处理 `ReceiveHit(HitContext)`。
- 处理输入许可、状态切换、平衡值重置。
- 决定何时进入 `HeavyStagger`、`Knockdown`、`ForcedKnockdown`、`Recovering`。

移除职责：

- 不再收集 `Rigidbody[]`。
- 不再直接配置 ragdoll 半径、solver、局部冲量倍率。
- 不再直接从 ragdoll body 采样恢复姿态。
- 不再在 Root 中维护 ragdoll 搜索根。

保留或新增的依赖：

```csharp
[SerializeField] RagdollSystem ragdollSystem;
```

Root 只调用外观 API：

```csharp
ragdollSystem.PlayHeavyReaction(hitContext);
ragdollSystem.EnterFullRagdoll(hitContext);
ragdollSystem.TickVisualPoseWriteback();
ragdollSystem.IsSettled;
ragdollSystem.CaptureRecoveryPose();
ragdollSystem.CaptureRecoveryAnchor();
ragdollSystem.ReturnToAnimated();
```

### 4.2 `AnimationModule`

保留职责：

- `Speed`、`Moving`、`Equipped` 等 Animator 参数。
- 轻击 Flinch layer / 程序化弯曲。
- HeavyStagger 动画表现。
- 武器装备/收回 overlay。
- GetUp 动画播放、动画事件结束通知。

调整职责：

- PoseMatch 的输入类型使用独立数据类型 `CharacterPoseSnapshot`，不依赖旧 `RagdollModule` 嵌套类型。
- 不直接引用 `RagdollModule` 类型。
- 不直接操作物理骨架。

### 4.3 `RecoveryModule`

保留职责：

- 记录当前起身类型。
- 记录恢复计时。
- 接收动画事件 `NotifyGetUpFinished()`。
- 提供 `IsComplete`。

移除职责：

- 不直接通过 `spineTransform` / `torsoFacingTransform` 判断仰俯。

调整为：

- 仰俯判定交给 `RagdollSystem.EvaluateGetUpType()`。
- `RecoveryModule.BeginRecovery(getUpType, duration)` 只接受结果，不负责采样姿态。

### 4.4 新增 `RagdollSystem`

建议位置：

```text
Assets/Scripts/Ragdoll/
  RagdollSystem.cs
  RagdollBoneMap.cs
  RagdollPoseSnapshot.cs
  RagdollAnchor.cs
  RagdollChainDefinition.cs
  RagdollChainCatalog.cs
  RagdollSystemConfig.cs
```

职责：

- 维护 Visual 骨骼和 Physics 骨骼映射。
- 初始化 ragdoll 刚体状态。
- `Animated` 模式下把 Visual 姿态同步到 Physics。
- `PartialRagdoll` 模式下启用命中链 dynamic，并把物理姿态融合回 Visual。
- `FullRagdoll` 模式下启用全身 dynamic，并把全身物理姿态回写到 Visual。
- 检测沉降。
- 捕获恢复姿态和恢复锚点。
- 暴露调试信息。

### 4.5 `CharacterControllerDebug`

保留职责：

- 显示状态、Balance、调试方向、接触点。
- 调 Root 的 Debug API。

调整职责：

- `RagdollSettled` 仍从 `root.IsRagdollSettled` 读取。
- 可新增显示：
  - `RagdollMode`
  - `ActiveChain`
  - `MappedBoneCount`
- 不直接持有或操作 `RagdollSystem`。

---

## 五、核心数据结构

### 5.1 `RagdollBoneMap`

```csharp
public sealed class RagdollBoneMap
{
    public HumanBodyBones HumanoidBone;
    public Transform VisualBone;
    public Transform PhysicsBone;
    public Rigidbody Body;
    public Joint Joint;
}
```

用途：

- 建立双骨架一一对应关系。
- 支持按 Humanoid 部位、骨骼名或 Transform 查找。
- 后续可扩展到非 Humanoid 名称映射。

### 5.2 `RagdollPoseSnapshot`

```csharp
public readonly struct RagdollPoseSnapshot
{
    public readonly Transform[] VisualBones;
    public readonly Quaternion[] LocalRotations;
}
```

用途：

- 起身前把物理终态转换为可见骨架局部旋转。
- 输入给 `AnimationModule` 做 PoseMatch。

### 5.3 `RagdollAnchor`

```csharp
public readonly struct RagdollAnchor
{
    public readonly Vector3 HipsWorldPosition;
    public readonly Vector3 FacingForward;
    public readonly bool IsValid;
}
```

用途：

- Root 对齐位置。
- 相机跟随。
- GetUp 朝向判断。

### 5.4 `RagdollChainCatalog`

建议做成 ScriptableObject：

```text
RagdollChainCatalog
├── Head
├── Chest
├── LeftArm
├── RightArm
├── LeftLeg
└── RightLeg
```

每条链包含：

- `HumanBodyBones[] bones`
- `float writebackWeight`
- `float impulseMultiplier`
- `bool includeChildren`

---

## 六、关键流程

### 6.1 日常动画同步

状态：`Locomotion`、`WeaponEquipPlayback`、轻击 overlay。

流程：

1. `Animator` 正常更新 `VisualModel`。
2. `RagdollSystem.LateUpdate()` 把 Visual 骨骼姿态复制到 Physics 骨骼。
3. Physics 刚体保持 `isKinematic = true`。

目的：

- 让隐藏物理骨架始终处在当前动画姿态。
- 重击或击倒发生时，物理骨架从正确姿态开始模拟。

### 6.2 重击局部 Ragdoll

状态：`HeavyStagger`。

流程：

1. Root 接收 `HitContext.Heavy`，扣 Balance 后进入 `HeavyStagger`。
2. `AnimationModule` 播放 HeavyStagger 动画。
3. Root 调用 `ragdollSystem.PlayHeavyReaction(hitContext)`。
4. `RagdollSystem` 根据 `ContactPoint` 和 `RagdollChainCatalog` 找到命中链。
5. 命中链 physics bodies 切 dynamic，其余 physics bodies 保持 kinematic 并继续跟随 Visual。
6. 对命中链施加冲量。
7. 每帧将命中链 Physics 姿态融合回对应 Visual 骨骼。
8. 达到 `heavyReactionDuration` 后，Physics 链切回 kinematic，回到 Animated 模式。

关键点：

- Animator 不再和 Rigidbody 抢同一根骨骼。
- 物理甩动可以更大胆，因为它发生在隐藏骨架上。
- 可见骨架只接收旋转/位置融合结果。

### 6.3 全身击倒

状态：`Knockdown` / `ForcedKnockdown`。

流程：

1. Root 进入击倒状态。
2. `AnimationModule` 停止或暂停 Animator 驱动。
3. `RagdollSystem.EnterFullRagdoll(hitContext)`。
4. 全部 physics bodies 切 dynamic。
5. 每帧把 Physics 姿态回写到 Visual。
6. Root 等待 `ragdollSystem.IsSettled`。

### 6.4 起身恢复

状态：`Recovering`。

流程：

1. `RagdollSystem.IsSettled == true`。
2. Root 读取：
   - `RagdollPoseSnapshot recoveryPose`
   - `RagdollAnchor recoveryAnchor`
   - `RecoveryGetUpType getUpType`
3. Root 对齐 Player 根位置。
4. `RagdollSystem.ReturnToAnimated()`，Physics 切 kinematic。
5. `AnimationModule.SetPendingRecoveryPoseSnapshot(recoveryPose)`。
6. `AnimationModule.BeginRecoveryPlayback(getUpType)`。
7. GetUp 动画事件触发后，Root 回到 `Locomotion`。

---

## 七、建议 API

### 7.1 `RagdollSystem`

```csharp
public sealed class RagdollSystem : MonoBehaviour
{
    public RagdollMode Mode { get; }
    public bool IsSettled { get; }
    public Transform CameraFollowAnchor { get; }

    public void Initialize();
    public void SyncPhysicsToVisualImmediate();
    public void PlayHeavyReaction(in HitContext hitContext);
    public void EnterFullRagdoll(in HitContext hitContext);
    public void ReturnToAnimated();
    public void Tick(float deltaTime);
    public void FixedTick(float fixedDeltaTime);
    public void LateTick(float deltaTime);

    public RagdollPoseSnapshot CaptureRecoveryPose();
    public RagdollAnchor CaptureRecoveryAnchor();
    public RecoveryGetUpType EvaluateGetUpType();
}
```

### 7.2 `RagdollMode`

```csharp
public enum RagdollMode
{
    Animated,
    PartialRagdoll,
    FullRagdoll,
    PoseMatching,
    Recovering
}
```

### 7.3 Root 调用顺序

```text
Update:
  HSM Tick
  Combat Tick
  Locomotion Tick
  Animation Tick
  RagdollSystem.Tick
  State transition checks

FixedUpdate:
  RagdollSystem.FixedTick

LateUpdate:
  Animation procedural writes
  RagdollSystem.LateTick
```

注意：

- 若某帧需要把 Physics 姿态覆盖到 Visual，`RagdollSystem.LateTick` 应在 Animator 更新后执行。
- 轻击脊柱弯曲和重击物理回写都写 Visual 骨骼，需在设计上规定优先级：重击/击倒物理回写优先于轻击 overlay。

---

## 八、现有模块是否需要改动

| 模块 | 是否需要改 | 原因 | 建议 |
|------|------------|------|------|
| `CharacterStateMachine` | 基本不需要 | 状态划分仍合理 | 保留 |
| `CharacterStateCapabilities` | 暂不需要 | `HeavyStagger` 可移动、击倒不可受击仍合理 | 保留 |
| `CombatModule` | 不需要 | 只管 Balance，边界清晰 | 保留 |
| `LocomotionModule` | 小改 | 击倒时仍由能力表禁止移动 | 只确保不依赖 ragdoll |
| `AnimationModule` | 已开始处理 | P1 已改用 `CharacterPoseSnapshot` | 后续重命名为 `CharacterAnimationPresenter` 并继续瘦身 |
| `RecoveryModule` | 需要 | 仰俯判定应来自物理骨架 | 只保留恢复计时与完成事件 |
| `RagdollModule` | 需要替换 | 当前职责过多且单骨架受限 | 废弃或迁移为 `RagdollSystem` 内部逻辑 |
| `CharacterControllerRoot` | 需要 | 当前直接持有 ragdoll bodies 和恢复锚点逻辑 | 只依赖 `RagdollSystem` 外观 |
| `CharacterControllerDebug` | 小改 | 显示信息需要从 Root/RagdollSystem 间接获取 | 保持只调 Root API |
| `CameraFollowTargetDriver` | 小改 | 目前通过 Root 获取 Humanoid hips | 改为优先跟随 `RagdollSystem.CameraFollowAnchor` |

---

## 九、迁移步骤

### C8.1 抽离接口，不改 Prefab

目标：

- 先降低 Root 和旧 `RagdollModule` 的耦合。

操作：

1. 新建 `RagdollSystem` 外观类。
2. 暂时让它内部包一层旧 `RagdollModule` 逻辑。
3. Root 从直接构造 `RagdollModule` 改为引用 `RagdollSystem`。
4. 编译通过后，再进入双骨架。

验收：

- 现有轻击、重击、击倒、起身行为不变。
- Root 不再序列化 `ragdollBodies`。

### C8.2 搭建双骨架映射

目标：

- 建立 `VisualModel` 和 `RagdollRig` 的映射。

操作：

1. 在 Player 预制体下复制一套模型骨架为 `RagdollRig`。
2. 隐藏 `RagdollRig` 的 SkinnedMeshRenderer 或不包含 Mesh。
3. 给 `RagdollRig` 骨骼配置 Rigidbody/Collider/Joint。
4. 新建 `RagdollBoneMapper` 自动匹配同名骨骼。
5. Play 时打印映射数量和缺失骨骼。

验收：

- `MappedBoneCount` 覆盖 Hips、Spine、Chest、Head、双臂、双腿。
- `Animated` 模式下 Physics 骨架能稳定跟随 Visual。

### C8.3 迁移重击局部反应

目标：

- 先解决当前最痛的重击局部甩动问题。

实现落点（当前代码）：

- `CharacterRagdollSystem.PlayHeavyReaction(...)`：重击入口，切换局部动态刚体并施加冲量。
- `CharacterRagdollSystem.SelectHeavyPartialBindings(...)`：局部受影响骨骼选择。
- `CharacterRagdollSystem.TrySelectByChainCatalog(...)`：优先按 `RagdollChainCatalog` / `RagdollChainDefinition` 选链。
- `CharacterRagdollSystem.ApplyHeavyPartialPoseLateUpdate()`：Physics 姿态回写到 `VisualModel`。
- `CharacterControllerDebug` + Gizmos：可视化冲量点、方向、主受击骨和受影响骨骼。

实现流程（当前行为）：

1. Root 收到 `Heavy Hit` 后进入 `HeavyStagger`，转发给 `CharacterRagdollSystem`。
2. `PlayHeavyReaction` 中仅将命中链对应 `RagdollRig` 刚体切为 dynamic，其余保持 kinematic。
3. 命中链优先由 `RagdollChainCatalog` 决定；未命中配置时回退 `AutoSubtree`。
4. 链传播支持 `SelfOnly / Children / ParentAndChildren`，并按父/子层级深度与衰减计算权重。
5. 冲量按主骨、次级骨和链传播权重分配到 `RagdollRig` 刚体。
6. 动态窗口结束后切回 kinematic，并在 `ApplyHeavyPartialPoseLateUpdate` 中回融姿态到 `VisualModel`。
7. `HeavyStagger` 结束后按状态机回到 `Locomotion`。

配置与调试：

- 链配置资产：`RagdollChainCatalog`（可自动生成默认 6 链模板）。
- 链定义：`RagdollChainDefinition`（关键词、传播模式、深度、衰减、冲量与回写权重）。
- 系统参数：`RagdollSystemConfig`（局部重击半径、最大骨骼数、动态保持时长、solver、回融时长/曲线等）。
- 调试可观测项：
  - 面板：`RagdollMode`、`RagdollChain`、`MappedBoneCount`
  - Gizmos：冲量位置/方向、主受击骨、受影响骨集合

验收：

- 右臂/左臂/胸口受击有明显甩动。
- 不再出现单骨架脱节。
- HeavyStagger 结束后正常回到移动。

当前待优化（不影响本阶段主验收）：

- 链权重标签（骨骼名 + 实际权重）目前仅内部计算，Scene 文本标注可后续补充。
- `RagdollSystemConfig` 已接入代码并在 `Player.prefab` 完成默认绑定；后续主要是按角色手感持续校准参数。

### C8.4 迁移全身击倒与起身

目标：

- 全身 ragdoll 和 Recovery 都使用双骨架。

操作：

1. `EnterFullRagdoll` 全身 dynamic。
2. Visual 每帧跟随 Physics。
3. `CaptureRecoveryPose` 从 Physics 转换到 Visual。
4. `EvaluateGetUpType` 使用 Physics torso。
5. Root 对齐位置后进入 Recovering。

验收：

- Force KO Light/Heavy 后全身倒地。
- 沉降后能按仰/俯播放正确起身动画。
- 起身不明显瞬移，不沉地。

### C8.5 清理旧结构

目标：

- 完成模块边界收敛。

操作：

1. 删除或停止使用旧 `RagdollModule`。
2. 删除 Root 中旧 ragdoll 字段。
3. `AnimationModule` 不再引用 `RagdollModule` 类型。
4. 更新文档 `角色控制器架构设计.md` 和 `架构设计.md`。

当前实现状态（2026-05，已落地）：

- `CharacterRagdollSystem` 已改为**仅双骨架后端**：
  - 移除 `Allow Legacy Fallback` 与全部单骨架回退分支。
  - `InitializeRuntime` 双骨架初始化失败时直接进入 `Unavailable`，不再兜底旧链路。
- `CharacterControllerRoot` 已去除 legacy 字段与收集逻辑：
  - 删除 `ragdollSearchRoot` / `ragdollBodies` / `autoCollectRagdollIfEmpty`。
  - 删除 `CollectRagdollBodiesFromChildren` 与对应 ContextMenu。
- `CharacterControllerRootEditor` 已移除“收集 Ragdoll Rigidbody”按钮，改为双骨架引用检查提示。
- `CharacterControllerDebug` 的后端展示已收口为 `Dual / Unavailable` 语义。
- `CharacterRagdollSystem.InitializeRuntime(...)` 已去除对 `CharacterControllerConfig` / `CharacterAnimationConfig` 的 Ragdoll 参数回退依赖。
- `Ragdoll` 参数来源已统一为 `RagdollSystemConfig`（缺失时仅用内置默认值并输出提示）。

### C8.6 移除旧链路（已执行）

目标：

- 完全删除单骨架 `RagdollModule` 与 legacy 回退代码，固定主路径为双骨架。

执行内容：

1. 删除 `Assets/Scripts/Character/Modules/RagdollModule.cs` 及 `.meta`。
2. 删除 `CharacterRagdollSystem` 中 legacy 字段、构造、状态分发与配置开关。
3. 删除 Root 侧旧 ragdoll 刚体数组及自动收集逻辑，`InitializeRuntime` 不再接收 `Rigidbody[]`。
4. 同步清理编辑器脚本与调试文案（不再出现 `LegacyFallback`）。
5. 更新 `Assembly-CSharp.csproj`，移除 `RagdollModule.cs` 编译项。

当前风险与约束：

- 双骨架引用缺失时，后端将直接为 `Unavailable`（无旧链路兜底）。
- 需在 Prefab 上保证 `Visual Root / Physics Root / Animator / RagdollSystemConfig / RagdollChainCatalog` 和骨骼同名映射有效。

### C8.7 击倒朝向与起身对齐修复（已执行）

目标：

- 修复 FullRagdoll 期间 visual 与 ragdoll 朝向/位置不一致。
- 修复 Recovering 起始帧因锚点偏差导致的明显瞬移感。

执行内容：

1. `CharacterRagdollSystem.WritebackFullRagdollPoseToVisual()` 从“局部坐标回写”改为“世界坐标回写（`SetPositionAndRotation`）”。
2. `CharacterRagdollSystem.CaptureDualRecoveryAnchor()` 的 `FacingForward` 计算改为“优先 hips->head 平面轴向（hips 指向 head），失败后回退 torso.forward / torso.up / hips.forward / root.forward”。
3. `CharacterControllerRoot.ResolveRecoveryForward()` 固定为单一路径轴向策略：`Front=hips->head`，`Back=head->hips`。
4. 新增地面投影辅助逻辑，保证恢复朝向在骨骼近似竖直时仍可稳定输出。
5. `CharacterControllerRoot` 新增“轴向强制对齐阈值”：当采用 hips-head 轴策略且当前朝向与恢复锚点夹角超过阈值时，即使关闭 `preAlignRotationBeforeRecovering` 也会在 Recovering 前执行朝向对齐，避免侧向倒地回到原 Player 朝向。
6. `CharacterRagdollSystem.CaptureDualRecoveryPose()` 的 PoseMatch 骨骼采样排除 `Hips/Pelvis`，避免恢复阶段把整体朝向骨强行对齐到动画首帧导致 VisualModel 原地转圈。
7. 重击链路修复 `AutoSubtree` 误触发：
   - 链解析不再只看“最近刚体”单点，而是按距离从近到远尝试命中 `RagdollChainCatalog`，命中即作为主骨；
   - 增加 `hips` 链定义（`hips/pelvis` 关键词）；
   - 骨名匹配加入归一化（去符号+小写），提升对 `mixamorig:` 等命名前缀/分隔符的兼容性。
8. 主骨候选选择由“首命中返回”改为“评分选择”：
   - 近邻候选（前 16 个）中按接触点距离评分；
   - `hips` 链施加轻微惩罚，避免腿部命中时被髋部抢占导致 debug 显示主骨为 `hips`。
9. Debug 面板接触点扩展：新增左右 `Elbow/Wrist/Knee/Ankle`，并在 `GetDebugContactPoint` 中补充 Humanoid 骨骼映射与回退点坐标。
10. 参数归属清理（Root/Config 收口）：
   - `CharacterControllerRoot` 移除 `placeholderSettleDuration/knockdownMinDuration/settleSpeedThreshold/faceUpDotThreshold`；
   - `CharacterRagdollSystem.InitializeRuntime` 不再接收 Root 侧 Ragdoll 回退参数，统一在系统内与 `RagdollSystemConfig` 决定；
   - `CharacterControllerConfig` 删除未使用的 Heavy Partial Ragdoll 字段组；
   - `CharacterAnimationConfig` 删除未使用的 `heavyPartialBlendBackDuration/Curve`；
   - `CharacterRecoveryFlow` 移除无效构造参数，保持模块职责单一。
11. 重击链传播修复（父链不生效）：
   - `TrySelectByChainCatalog` 改为先收集候选再排序，不再受 `_dualBindings` 原始顺序影响；
   - 对父链候选不再叠加距离衰减，避免“配置允许父链但被距离权重+阈值二次过滤”；
   - 同权重下优先父链，再按距离排序，保证 `maxBodies` 截断时父链仍可进入。

验收：

- 全身倒地阶段，Visual 姿态与 Physics 姿态保持同向，不再出现“ragdoll 倒向 A，visual 倒向 B”。
- 进入 Recovering 时，视觉起身位置与倒地落点连续，明显瞬移显著减少。
- 仰/俯起身判定与朝向对齐不互相干扰（`EvaluateGetUpType` 保持现有逻辑，朝向由 hips-head 轴按前/后起身规则映射）。

---

## 十、需你在 Unity 编辑器中操作

**需你在编辑器中操作**（双骨架搭建无法完全靠代码完成）：

1. **打开 Player 预制体**
   - 位置：Project 窗口双击 `Assets/Prefabs/Character/Player.prefab`。
   - 目的：进入 Prefab Mode，避免只改场景实例。
   - 预期：Hierarchy 顶部显示 Player 预制体内容。

2. **创建 RagdollRig 根节点**
   - 位置：Hierarchy 中选中 `Player`。
   - 操作：菜单 `GameObject > Create Empty`，命名为 `RagdollRig`。
   - 预期：`Player/RagdollRig` 出现在层级中。

3. **复制骨架**
   - 对象：`Player/VisualModel/Armature` 或当前 Animator 下的骨架根节点。
   - 操作：复制整套骨架到 `Player/RagdollRig` 下。
   - 预期：`RagdollRig` 下有一套与 Visual 骨架同名或可匹配的骨骼。
   - 注意：复制后不需要可见 Mesh；若复制到了 SkinnedMeshRenderer，应禁用或移除该 Renderer。

4. **配置物理骨架**
   - 对象：`Player/RagdollRig` 下的 Hips、Spine、Head、UpperArm、ForeArm、UpperLeg、Leg 等关键骨骼。
   - 操作：添加 `Rigidbody`、`Collider`、`CharacterJoint` 或 `ConfigurableJoint`。
   - 预期：RagdollRig 可以单独组成完整物理链。
   - 注意：建议先用 Unity Ragdoll Wizard 生成，再手调碰撞体和关节限制。

5. **禁用物理骨架显示**
   - 对象：`RagdollRig` 下所有 Renderer。
   - 操作：Inspector 取消勾选 Renderer，或删除复制出来的 Mesh Renderer。
   - 预期：Game 视图中只看到 `VisualModel`。

6. **挂载 CharacterRagdollSystem**
   - 对象：`Player` 根节点。
   - 操作：Inspector 点击 `Add Component`，搜索并添加 `CharacterRagdollSystem`。
   - 填写引用：
     - `Visual Root`：拖入 `VisualModel` 或 Animator 所在物体。
     - `Physics Root`：拖入 `RagdollRig`。
     - `Animator`：拖入 VisualModel 上的 Animator。
     - `RagdollSystemConfig`：拖入 `RagdollSystemConfig` 资产（建议与场景实例一致）。
     - `RagdollChainCatalog`：拖入链配置资产。
   - 预期：Play 时 Console 输出骨骼映射数量。

7. **保存预制体**
   - 操作：Prefab Mode 顶部点击保存，或使用 `Ctrl+S` 保存。
   - 预期：退出 Prefab Mode 后改动仍保留。

---

## 十一、风险与应对

| 风险 | 影响 | 应对 |
|------|------|------|
| 双骨架骨骼名不一致 | 映射失败 | 优先用 Humanoid bone；失败时用同名匹配；最后手动列表补齐 |
| RagdollRig 复制了 Mesh | 画面重影 | 禁用或删除 RagdollRig Renderer |
| Physics 骨架与 Visual 初始姿态不一致 | 受击瞬间跳动 | Awake 时强制 `SyncPhysicsToVisualImmediate()` |
| Joint 方向/限制不合理 | 物理扭曲 | 先只启用上肢链验证，再扩展全身 |
| Recovery 从 Physics 到 Visual 姿态转换错误 | 起身抽动 | PoseMatch 只融合关键骨骼，并加单骨角度 clamp |
| Root 重构影响现有功能 | 回归风险 | 分阶段迁移，每阶段保持可 Play 验收 |

---

## 十二、验收标准

### 阶段验收

- C8.1：现有单骨架行为不退化，Root 不再直接持有 `Rigidbody[]`。
- C8.2：双骨架映射成功，Animated 模式下隐藏物理骨架跟随可见骨架。
- C8.3：重击局部甩动明显，不脱节，HeavyStagger 能恢复。
- C8.4：全身击倒、沉降、起身完整闭环。
- C8.5：旧 `RagdollModule` 不再被 Root/AnimationModule 直接依赖。
- C8.6：legacy 单骨架链路已删除，后端固定为 `Dual / Unavailable`。

### 最终验收

- Player 架构边界清晰：
  - Root 管状态。
  - Combat 管平衡。
  - Locomotion 管移动。
  - Animation 管 Animator。
  - RagdollSystem 管双骨架物理。
  - Recovery 管起身流程状态。
- 重击局部物理明显且稳定。
- 击倒和起身不破坏现有调试流程。
- 新角色接入时主要配置 `RagdollSystemConfig`、骨骼映射和链定义，不需要改 Root。

---

## 十三、建议结论

建议 C8 按“先抽接口，再上双骨架，再迁移重击，再迁移击倒”的顺序做。不要直接大改全部流程，否则一旦起身或击倒同时出问题，很难定位。

优先级：

1. `RagdollSystem` 外观与模块边界。
2. 双骨架 Visual ↔ Physics 映射。
3. 重击局部反应迁移。
4. 全身击倒与恢复迁移。
5. 文档与旧字段清理。

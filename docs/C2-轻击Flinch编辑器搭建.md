# C2 轻击 Flinch — 编辑器搭建与验收

| 项 | 内容 |
|----|------|
| 阶段 | C2 |
| 引擎 | Unity **6000.3.13f1** |
| 关联代码 | `AnimationModule`、`CharacterControllerRoot` |

---

## 一、实现概要

轻击 = **Additive 动画融合 + 程序化脊柱/头（可选颈）弯曲**：

1. **动画**：`FlinchLayer`（Additive）+ `HitBlendX` / `HitBlendZ` 2D 混合 + `FlinchWeight` 约 **0.3s** 淡出。  
2. **弯曲方向**：在 **Torso Facing** 骨骼（建议 `Spine2`）的当前朝向上计算反冲；拔刀后胸口偏右前方时仍与打击侧一致。  
3. **施加骨骼**：`Spine`（脊柱）、`Head`（头，独立角度）、`Neck`（可选，Config 默认关）。  
4. **时机**：`LateUpdate` 在 Animator 之后；每帧在当前动画姿态上叠加弯曲。  
5. **玩法**：不切 `LightFlinch` HSM；不 Abort 拔刀；可移动、Locomotion 可 E。

---

## 二、Animator Setup

菜单 **`Active Ragdoll` → `Setup Player Flinch Layer (C2)`**。

---

## 三、Inspector（CharacterControllerRoot）

| 字段 | 推荐 Hierarchy 路径 |
|------|---------------------|
| **Spine Transform** | `.../mixamorig:Spine`（脊柱施加） |
| **Torso Facing Transform** | `.../mixamorig:Spine2`（胸口朝向参考） |
| **Head Recoil Transform** | `.../mixamorig:Head` |
| **Neck Recoil Transform**（可选） | `.../mixamorig:Neck` |
| **Config** | `DefaultCharacterControllerConfig` |

未填 **Torso Facing** 时回退为 **Spine Transform**。

---

## 四、Config（轻击相关）

| 字段 | 默认 | 说明 |
|------|------|------|
| `lightFlinchDuration` | 0.3 | overlay / 弯曲淡出 |
| `lightFlinchSpineRecoilEnabled` | true | 脊柱弯曲 |
| `lightFlinchSpinePitchDegrees` / `Roll` | 8 / 6 | 脊柱 |
| `lightFlinchNeckRecoilEnabled` | false | 颈部（需拖 Neck 骨） |
| `lightFlinchNeckPitchDegrees` / `Roll` | 4 / 3 | 颈部 |
| `lightFlinchHeadRecoilEnabled` | true | 头部 |
| `lightFlinchHeadPitchDegrees` / `Roll` | 5 / 4 | 头部 |
| `lightFlinchSpineRecoilCurve` | 空 | 脊柱/颈/头共用强度曲线 |
| `lightFlinchRootKnockbackDistance` | 0 | 根 CC 后撤；0=不滑步 |

---

## 五、需你在编辑器中操作

1. **打开场景**  
   - Project 双击玩家场景；Hierarchy 选中带 `CharacterControllerRoot` 的 Player。

2. **绑定骨骼引用**  
   - Inspector → **Recovery / 起身** 分组：  
     - **Spine Transform** → 模型下 `mixamorig:Spine`  
     - **Torso Facing Transform** → `mixamorig:Spine2`  
     - **Head Recoil Transform** → `mixamorig:Head`  
     - **Neck Recoil Transform**（若要用颈弯）→ `mixamorig:Neck`  
   - 预期：四个槽均显示对应 Transform 名称（Neck 可留空）。

3. **保存**  
   - `Ctrl+S` 保存场景；若改 Prefab 需 **Apply**。

---

## 六、360°受击浮标调试（窗口滑动条）

需你在编辑器中操作：

1. **创建独立 Helper（可视化）**  
   - 菜单 `GameObject > Create Empty`（游戏对象 > 创建空对象），命名 `HitDirectionDebugHelper`。  
   - Inspector `Add Component`：添加 `Hit Direction Debug Helper` 脚本。  
   - 预期：Hierarchy 出现独立 helper，不挂在 Player 子层级（仅画箭头/射线，不负责输入）。

2. **绑定目标与箭头（可选）**  
   - 将玩家根物体（带 `CharacterControllerRoot`）拖到 `Target Root`。  
   - 若需要模型箭头：创建一个 `3D Object > Cone`，拖到 `Arrow Visual`。  
   - 预期：Play 后 helper 会围绕玩家更新位置与朝向（由调试窗口角度驱动）。

3. **使用 Character Debug 窗口调角度**  
   - 在 Player 上挂 `CharacterControllerDebug`。  
   - 打开 `Use 360 incoming`，拖动 `Yaw` 滑动条（0~360°）。  
   - 浮窗 `F / B / L / R` 可一键跳到主方向（F=0, B=180, L=270, R=90），再用滑条微调。  
   - 需要切换箭头来源语义时勾选 `Invert source bearing`。  
   - 接触点可在窗口 `Head/Chest/LArm/RArm/LLeg/RLeg` 选择，或按快捷键 `F1~F6` 切换。  
   - 点击窗口按钮 `Light Hit / Heavy Hit` 触发轻/重受击。  
   - `Force KO Light` / `Force KO Heavy` 为两种强制击倒，且可分别调节冲量滑条（模拟不同怪物攻击冲量）。

4. **可视化核对**  
   - `showGizmoArrow = true`：Scene 中显示青线（玩家→来源）与红线（受击来向）。  
   - `drawRuntimeRays = true`：Play 时持续画调试线，便于录屏对比。
   - 若箭头不动，先看浮窗是否显示 `Helper: Bound`；若是 `Helper: Missing`，请把 `HitDirectionDebugHelper` 拖到 `CharacterControllerDebug.directionHelper`。
   - 浮窗会显示 `Dir(xz)` 与 `RagdollSettled`，用于 C3/C5 方向与倒地链路观察。

---

## 七、验收

1. **未拔刀**：Debug 轻击 1–4（前后左右）— 弯曲沿胸口反方向。  
2. **拔刀站立**：同上 — 与拔刀后胸口朝向一致（不再按 Root 朝向错位）。  
3. **360°连续测试**：拖动窗口 `Yaw` 滑动条，任意角度触发 Light/Heavy，方向应连续无四向跳变。  
4. **清空 Flinch 片段**：仍应有脊柱/头程序化弯曲。  
5. **击倒/滚石**：全身 Ragdoll 仍正常；`Force KO Light` 小幅倒塌、`Force KO Heavy` 明显飞出。  
6. **冲量覆盖**：提高 `ForceKO Heavy Impulse` 会明显增加飞出距离；降低 `ForceKO Light Impulse` 则更接近原地倒塌。

---

## 八、与击倒 Ragdoll 的区别

| | 轻击 C2 | 击倒 |
|--|---------|------|
| 驱动 | Animator + 骨 localRotation | 全身 RB + AddForce |
| HSM | overlay | Knockdown 链 |

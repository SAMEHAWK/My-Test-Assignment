# My Test Assignment — 主动式布娃娃

[English](README.md) · **简体中文**

Unity 开发者测试任务：为人形角色实现 **主动式布娃娃（Active Ragdoll）** 系统，包含 **受击反应** 与 **平衡值** 机制；在动画与物理驱动状态之间平滑过渡。

**参考游戏：** *Exanima*、*Hellish Quart*、*No Rest for the Wicked*（俯视角镜头风格）。

## 前言

本项目由 AI 协作制作，IDE 为 Cursor，AI 模型为 Cursor Auto 模式提供的 AI 模型以及 Deepseek V4 pro。游戏为俯视角 3D。

## 环境要求

| 项目 | 说明 |
|------|------|
| Unity | **6000.3.13f1 (LTS)** — 必须使用此版本 |
| 测试平台 | Windows |

## 快速开始

1. 克隆本仓库。
2. Unity Hub 用 **6000.3.13f1** 打开项目。
3. 打开 `Assets/Scenes/SampleScene.unity`。
4. **播放** — 场景含两组假人与滚石：第一组测受击，第二组测击倒，滚石路径测即时击倒。

首次打开导入资源可能需要数分钟。

## 操作

| 操作 | 输入 |
|------|------|
| 移动 | `W` `A` `S` `D` |
| 装备 / 收刀 | `E` |
| 轻攻击 | 鼠标左键 |
| 重攻击 | 鼠标右键 |

俯视角；玩家可主动攻击假人，也会受到假人与滚石的伤害。

输入配置：`Assets/InputSystem_Actions.inputactions`（若做移动端可扩展虚拟摇杆）。

## 任务概述

### 玩家角色

- 移动：WASD
- 动画：**待机（Idle）**、**奔跑（Run）**、**起身（仰卧/俯卧）**
- **平衡值 6 点**；**1.5–2 秒**未受击开始恢复
  - 轻击 **−1**
  - 重击 **−2**
  - 归零 → **击倒**，之后平衡值回满

### 场景分区

| 区域 | 用途 |
|------|------|
| **第一组** | 受击测试 — 2 个原地假人，定时攻击 |
| **第二组** | 击倒测试 — 2 个假人连续攻击；与第一组距离足够远 |
| **滚石** | 球体沿**循环路径**往返；碰到玩家 → **无视平衡值立即击倒**；力方向 = 碰撞瞬间滚石运动方向 |

**第一组**

- 假人 1 — 轻击
- 假人 2 — 重击

**第二组**

- 假人 3 — 连续轻击
- 假人 4 — 连续重击

### 机制摘要

| 类型 | 要点 |
|------|------|
| **轻击** | 上半身朝受击方向（前/后/左/右）晃动；与当前动画融合；双脚站立、无布娃娃、无位移；**0.3 秒内**回待机 |
| **重击** | 受击肢体链局部布娃娃（例：右肩 → 右臂+上躯干物理，下半身仍动画）；冲量沿受击方向；其余部位踉跄动画；**约 0.5–1 秒**融合回动画 |
| **击倒** | 全身布娃娃；继承最后一击的力与方向；轻击击倒=原地蜷缩；重击击倒=沿方向飞出；力加在**接触点**；自然沉降，无预设倒地动画 |
| **起身** | 布娃娃静止后：脊柱朝上与世界向上点积判断仰/俯；姿态匹配混合到对应起身动画；回到待机 |

平衡伤害、恢复时间、冲量等可在观感允许范围内微调。

## 功能清单

### 核心系统

- [x] 主动式布娃娃（动画 ↔ 物理切换，双骨架架构）
- [x] 轻击反应（四向上半身，&lt; 0.3 s 恢复）
- [x] 重击（局部布娃娃 + 踉跄 + 融合回动画）
- [x] 平衡值（6 点、恢复、归零击倒、击倒后重置）
- [x] 全身击倒 + 接触点施力
- [x] 起身（姿态检测 + 匹配混合 + 待机）

### 玩家与场景

- [x] 玩家移动（WASD）
- [x] Idle / Run 动画
- [x] 俯视角相机
- [x] 第一组假人（定时轻/重击）
- [x] 第二组假人（连续轻/重击），与第一组隔开
- [x] 滚石循环路径 + 碰撞即时击倒
- [x] 玩家主动攻击（轻攻击 / 重攻击，含根运动与命中判定）
- [x] 武器装备 / 收刀系统（含动态动画层切换）

## 项目结构

```
Assets/
  Scenes/                  # 主测试场景
  Models/                  # 角色模型 / animation_pack.fbx / 提取的动画片段
  Prefabs/                 # Player 预制体
  Scripts/                 # 核心角色系统脚本
    Character/             # 角色控制器、状态机、子模块
      Config/              # ScriptableObject 配置定义
      Debug/               # 调试工具（受击注入、平衡值显示）
      Editor/              # 编辑器扩展
      Modules/             # 动画播放控制器（装备、攻击）
    Ragdoll/               # 布娃娃系统（链定义、骨骼映射、配置）
    Combat/                # 战斗系统（受击盒、武器命中扫描）
    Gameplay/              # 游戏玩法（滚石击倒）
    Camera/                # 相机跟随
    UI/                    # UI 工具
  Animator/                # Animator Controller
  Configs/                 # ScriptableObject 配置资产
  Settings/                # URP 渲染管线配置
```

## 架构概要

角色控制器采用 **层次状态机（HSM）+ 模块组合** 架构：

- **3 父态 / 9 子态**：`Grounded`（Locomotion / WeaponEquipPlayback / AttackPlayback）、`HitReaction`（LightFlinch / HeavyStagger）、`Incapacitated`（Knockdown / ForcedKnockdown / Recovering）
- **双骨架布娃娃**：VisualModel（动画骨架）+ RagdollRig（隐藏物理骨架），物理姿态世界空间回写
- **5 个子模块**：CharacterMotor（移动）、CharacterCombat（平衡值）、CharacterAnimationPresenter（动画）、CharacterRagdollSystem（布娃娃）、CharacterRecoveryFlow（起身）
- **配置驱动**：移动/平衡/动画/布娃娃参数均通过 ScriptableObject 配置

详见 `docs/` 目录下的架构文档。

## 第三方资源

| 资源 | 路径 | 说明 |
|------|------|------|
| 角色 / 动画 | `Assets/Models/` | 出题方提供 |
| 大剑动画集 | `Assets/MassiveGreatSword_AnimSet/` | 第三方动画资源 |
| Unity 包 | `Packages/manifest.json` | URP、Input System、Cinemachine 等 |

## 作者

**王燿增** — [916821412@qq.com](mailto:916821412@qq.com)
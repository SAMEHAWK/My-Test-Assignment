# AGENTS.md — 项目协作规则

本文档供 AI 助手（Codex、Cursor 等）在本仓库中工作时遵循。与用户补充的规则冲突时，以用户最新指示为准。

## 沟通语言

- **必须使用简体中文**回答用户（说明、计划、总结、提问）。
- 代码标识符、提交信息、对外 README 主文档（`README.md`）仍可使用英文，除非用户另有要求。

## 代码注释

- 所有新增或修改的代码注释采用 **中英双语**。
- 建议格式：先中文说明意图，下一行或同行附英文；或 `/// 中文 <br/> English`（按语言习惯选用一种并保持一致）。
- 避免无注释的复杂逻辑；避免仅写一种语言（除非用户豁免某文件）。

**示例：**

```csharp
// 扣除平衡值并在归零时触发击倒
// Deduct balance and trigger knockdown when it reaches zero
_balance -= damage;
```

## 工作流程

用户提出需求时，**不得立即大改代码**，须按以下顺序：

1. **阅读相关代码** — 搜索并阅读与需求有关的脚本、场景、预制体、配置（`Assets/`、`docs/` 等）。
2. **制定计划** — 用中文写出：目标、现状、拟改文件、步骤、风险/待确认点。
3. **供用户确认** — 将计划呈现给用户，等待明确同意（或按用户修改意见调整计划）。
4. **再执行** — 用户确认后再实现；若用户说「直接做」可视为已确认。

小例外（无需单独计划，但仍用中文说明）：用户明确说「直接改」「不用计划」；或仅修正错别字/单行明显笔误且用户已指明文件与内容。

## Unity 编辑器操作说明

凡**无法仅靠改代码与文本资源完成**、需要用户在 **Unity 编辑器** 内手动执行的操作，AI **必须**用中文给出**详细、可逐步照做**的说明，不得用「在编辑器里配置一下」「挂上组件即可」等模糊表述带过。

### 何时必须写编辑器步骤

包括但不限于：

- 创建 / 修改 **Prefab**、场景层级、组件引用拖拽
- **Animator Controller**、动画片段、Avatar、Layer 权重
- **Ragdoll** 生成、刚体 / 碰撞体 / 关节、Layer / Physics 矩阵
- **Input System** 中 `Input Actions` 的生成 C# 类、绑定、Player Input 组件
- 导入 **FBX**（Rig、Animation、Materials）设置
- **ScriptableObject** 资产创建与字段填写
- 场景摆放（假人、滚石路径、玩家出生点）、Build Settings
- Project Settings（Input、Physics、Tags/Layers）— 仅当确需改动时

若 AI 已改 `.unity` / `.prefab` YAML，仍须说明用户打开 Unity 后**如何验证**（播放、Inspector 中应看到什么）。

### 说明格式要求

每条操作说明建议包含以下要素（可按任务增减，但不可省略关键项）：

| 要素 | 要求 |
|------|------|
| **目的** | 这一步为什么要做 |
| **位置** | 菜单完整路径（中英对照或中文说明 + 英文菜单名），例如：`GameObject > Create Empty`（游戏对象 > 创建空对象） |
| **对象** | 选中哪个 Hierarchy 节点 / 哪个 Prefab / 哪个 Project 路径下的资产 |
| **操作** | 点击什么、拖拽什么到哪里、填什么数值或引用 |
| **预期结果** | Inspector / Scene 视图中应出现什么变化 |
| **注意** | 常见报错、需 Apply Prefab、需保存场景等 |

推荐用**有序列表**分步编写；步骤较多时可按「一、准备工作」「二、Prefab 配置」分段。

**示例（片段）：**

```markdown
1. **打开场景**  
   - 在 Project 窗口双击 `Assets/Scenes/SampleScene.unity`。  
   - 预期：Hierarchy 中出现场景内已有对象。

2. **创建玩家根节点**  
   - 菜单 `GameObject > Create Empty`，命名为 `Player`。  
   - 将 `Player` 的 Position 设为 `(0, 0, 0)`。

3. **添加角色控制器组件**  
   - 选中 `Player`，Inspector 点击 `Add Component`，搜索并添加 `Character Controller Root`。  
   - 将 `Assets/Prefabs/...` 下的 Animator 所在子物体拖入对应引用槽；若槽位为空，运行时会报 …
```

### 与代码交付的配合

- 新增脚本后：说明需 **Add Component** 到哪个对象、**SerializeField** 引用应对应 Hierarchy 中哪一项。
- 依赖 `.inputactions` 生成 C# 时：写明在 Input Actions 资产上勾选 **Generate C# Class** 及 **Apply** / 重新编译的时机。
- 涉及多层子物体（骨骼、Ragdoll）：用**层级路径**描述，如 `Player/Model/Hips/Spine`。
- 若存在多种做法，写明**推荐方案**及理由；备选方案可折叠简述。

### 禁止与原则

- **禁止**只给结论不给步骤（如「配置 Animator 即可」）。
- **禁止**假设用户熟悉 Unity 快捷键或隐藏菜单。
- 菜单名以 Unity 6 默认英文界面为准；若项目使用中文界面，可同时标注中文菜单名。
- AI 不能代替用户在本地点击执行时，须在步骤前注明：**「需你在编辑器中操作」**。
- 改 `ProjectSettings` 或全局物理/输入设置前，须在计划中说明**影响范围**并等待用户确认。

## 文档目录 `docs/`

- 路径：`docs/`
- 用途：存放 **供 AI 阅读的文档**、**开发计划**、任务拆解、设计笔记等（如 `Unity_测试任务.pdf`、`架构设计.md`、迭代计划）。
- 系统架构：**`docs/架构设计.md`** — 实现主动式布娃娃前须阅读。
- 角色控制器：**`docs/角色控制器架构设计.md`** — 玩家核心逻辑，优先实现。
- 开始任务前，若 `docs/` 内有与当前需求相关的计划或说明，**应先阅读**再制定方案。
- 用户要求记录计划时，可将经确认的计划写入 `docs/`（文件名由用户指定或 AI 提议后确认）。

## Unity 引擎版本

本仓库 **必须使用** 以下编辑器版本（与题面一致，不得按其他版本 API 或习惯编写）：

| 项 | 值 |
|----|-----|
| 版本 | **Unity 6000.3.13f1 (LTS)** |
| 修订 | `8c4f11e4fb20`（见 `ProjectSettings/ProjectVersion.txt`） |
| 代际 | Unity **6** |

- 版本以 `ProjectSettings/ProjectVersion.txt` 为准；若与用户口头不一致，以该文件为准并提醒用户。
- **禁止**擅自升级/降级项目 Unity 版本或批量修改 `ProjectSettings` 中的版本相关项，除非用户明确要求。
- 选用 API、包功能、菜单路径时，须符合 **Unity 6 / 6000.3** 文档；避免使用已废弃（Obsolete）的旧版写法，避免使用本版本不存在的预览 API。

## 代码规范（依引擎版本）

代码风格与 API 使用须 **依照 Unity 6000.3.13f1 对应规范**，包括但不限于：

- **C# / Unity 脚本**：遵循 Unity 6 官方 C# 约定（`MonoBehaviour` 生命周期、`SerializeField`、避免每帧 `GetComponent`、物理与动画系统与当前引擎行为一致）。
- **命名**：类型/公共成员 PascalCase；私有字段 camelCase 或 `_camelCase`（与现有脚本保持一致）；`Assets/` 下资源命名与团队已有资源一致。
- **输入**：本项目使用 **Input System**（`com.unity.inputsystem`），勿默认采用旧 Input Manager，除非用户指定。
- **渲染**：本项目为 **URP**（`com.unity.render-pipelines.universal`），勿引入 Built-in 管线专属写法。
- **包与模块**：以 `Packages/manifest.json` 已锁定依赖为准；新增 Package 须先列入计划并经用户确认。
- 不确定 API 是否存在于 6000.3 时，查阅 [Unity 6 脚本 API](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/) 或说明依据后再写。

实现前阅读 `Assets/` 内已有脚本的写法，**新代码与现有风格对齐**，不引入与 Unity 6 不匹配的第三方模式（除非用户要求）。

## 角色控制器结构

- **单一 MB**：`CharacterControllerRoot`（`[RequireComponent(typeof(UnityEngine.CharacterController))]`）。
- **子模块**：`LocomotionModule`、`AnimationModule`、`CombatModule`、`RagdollModule`、`RecoveryModule` 为 **普通 C# 类**，在 `InitializeModules()` 中 `new` 构造，**不要**作为 MonoBehaviour 挂到 Player。
- **Inspector**：引用集中在 Root 的 `[Header]` 分组（Config / Locomotion / Animation / Ragdoll / Recovery）。
- **输入 / 调试**：`PlayerInputReader`、`CharacterControllerDebug` 仍为独立 MB；输入调用 `Root.SetMoveInput()`。
- 编辑器步骤：[`docs/C1-角色控制器编辑器搭建.md`](docs/C1-角色控制器编辑器搭建.md)。

## 项目上下文（简要）

- 任务：主动式布娃娃测试；题面 `docs/Unity_测试任务.pdf`。
- 人类可读说明：`README.md`（英）、`README.zh-CN.md`（中）。
- 玩法脚本优先放在 `Assets/Scripts/`；勿提交 `Library/`、`Temp/`、`Logs/`（见 `.gitignore`）。

---

*以下区块随协作追加规则，请保持条目清晰。*

### 用户追加规则

<!-- 在此下方继续添加 -->

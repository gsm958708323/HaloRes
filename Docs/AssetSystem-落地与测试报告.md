# Unity 资源管理框架：文档落地与测试报告

> 来源文档：`C:\Users\Halo\Desktop\Unity资源管理框架方案选型与约束分析.md`（37914 行 / 1.16 MB / 15 个课程快照）
> 目标工程：`D:\Work\Blog\HaloRes2`（Unity 6000.0.32f1，URP）

---

## 1. 落地结果总览

| 项目 | 数量 | 说明 |
|---|---|---|
| 从文档提取的代码段 | 105 | 88 个唯一路径 + 17 个跨课程重复版本 |
| 整理后的框架文件 | 71 个 `.cs` / 17,460 行 | 原 15 个课程目录共 118 个 `.cs` / 29,389 行 |
| 汇编定义 asmdef | 6 | Runtime / Simulation / Demos / Editor / 2 测试程序集 |
| 全链路代码 | 3 | 布局契约、运行器、内容构建器 |
| 交互演示代码 | 2 | 控制器（状态机 + UI + 3D 视口）、uGUI 构建工具 |
| 测试用例 | 37 | EditMode 7、PlayMode 30 |

编译结果：**0 error**；测试结果：**37/37 通过**。

## 2. 目录结构（整理后：只剩一份最终版）

```
Assets/Game/AssetSystem/
├─ Runtime/                             asmdef: Game.AssetSystem（运行时，唯一真相源）
│  ├─ Core/                             MainThreadPump（有序队列 + 预算策略）/ PumpBudget / MainThreadAssetTask
│  ├─ Loading/                          可取消加载任务 + 共享加载注册表（3.2/3.3 最终版）
│  ├─ Scheduling/                       优先级 + 双预算调度器（3.6），唯一持有优先级配额的一层
│  ├─ Storage/                          文件事务 / Hash 校验 / active 指针 / 启动恢复 / PatchStagingStore
│  ├─ Patch/                            PatchTypes（唯一清单）/ IPatchTransport / PatchApplyController / PatchActivationService
│  ├─ Bundles/                          BundleRuntime：引用计数 + 依赖租约 + 快照
│  ├─ Assets/                           AssetCache：Handle / Scope / 加载任务
│  ├─ Memory/                           AssetMemoryPolicy + AssetMemoryEvaluator：先资产后 Bundle
│  ├─ Diagnostics/                      运行时快照
│  ├─ Content/                          ActiveContentContext：当前激活版本长什么样（只描述，不运行）
│  ├─ Release/                          5.2 发布加固：加载门禁 / 安全点 / 版本清理 / 真实载荷
│  ├─ Simulation/                       asmdef: Game.AssetSystem.Simulation（唯一一套模拟后端）
│  └─ Demos/                            asmdef: Game.AssetSystem.Demos（场景入口，不能反向被 runtime 引用）
├─ Editor/                              asmdef: Game.AssetSystem.Editor
│  ├─ Scanning/                         扫描 + 分组规则 + Key 校验
│  ├─ Manifest/                         依赖图 + Manifest 构建
│  ├─ Building/                         AssetBundle 构建 / 校验 / 平台变体 / 全链路内容构建
│  ├─ Inspector/                        构建期诊断窗口（扫描 / 依赖图 / 构建校验）
│  └─ SampleContent/                    测试与 demo 用的示例资源生成器
├─ Tests/
│  ├─ Editor/                           7 个 EditMode 用例
│  └─ PlayMode/                         30 个 PlayMode 用例 + Support/ 测试数据
└─ Lesson4xSample/                      测试生成的示例资源（非代码）
```

程序集边界：`Game.AssetSystem` ← `Game.AssetSystem.Simulation` ← `Game.AssetSystem.Demos`，
`Game.AssetSystem.Editor` 依赖前两者。demo 代码漏进 runtime、或 runtime 反向依赖替身，
现在都是编译错误，不再靠人盯。

命名空间不再带课程后缀：`Game.AssetSystem.Loading`、`Game.AssetSystem.Bundles`、
`Game.AssetSystem.Content`、`Game.AssetSystem.Patch`、`Game.AssetSystem.Release`、
`Game.AssetSystem.Editor.Building` ……

## 3. 从 15 份课程快照合并成一份

原工程把文档的 15 个课程快照各自完整落地，同名类型大量重复
（`BundleRuntime` ×3、`AssetCache` ×3、`UnityMainThreadPump` ×5、`AssetScanner` ×2），
只靠 `.L31` / `.L46` 这样的命名空间后缀隔离。整理后：

| 处理 | 内容 |
|---|---|
| 每层只留最新实现 | Loading 取 3.3（`AcquireWaiter` 已被 `TryAcquireWaiter` 取代）、Storage 取 3.5、Scanning 取 4.2、Bundles/Assets 取 4.6（4.4/4.5 被 4.6 覆盖） |
| 主线程调度器合并 | 3 个 `UnityMainThreadRunner` + 5 个 `UnityMainThreadPump` + `IntegrationMainThreadPump` → 1 个 `Core/MainThreadPump`（优先级队列 + 每帧预算 + `EnsureMainThread`） |
| 课程 demo 收敛 | 12 个逐课 bootstrap 合并为一个场景 + 枚举入口（`Demo_Lessons`，见 6.5）；3 个课程编辑器窗口还原为 `Editor/Inspector`（见 6.6） |
| 类型名去课程号 | `Lesson43AssetBundleBuilder` → `AssetBundleBuilder`、`LessonHud` → `AssetSystemHud`；`LessonBootGate` 直接删除，改由场景入口组件启动 |
| 测试保留并改到新 API | 21 个用例全保留；3.2 的两个用例改到 3.3 的 `TryAcquireWaiter`；测试替身集中到 `Tests/PlayMode/Support` |

4.4 / 4.5 / 4.7 与 5.1 的演示代码已在 `Demo_Lessons` 场景中还原，同时其行为语义仍由全链路用例与 Release 用例覆盖（见第 8 节）。

## 4. 文档代码本身的缺陷（已修）

| # | 问题 | 影响文件 | 修复 |
|---|---|---|---|
| 1 | 用了 `TaskAwaiter<T>` 但缺 `using System.Runtime.CompilerServices;` | 10 个（L31/L32/L33/L36/L44/L45/L46/L51） | 补 using |
| 2 | `AssetLoadResult<T>` 缺少 `where T : class`，无法作为 `AssetHandle<T>` 的类型参数 | L45、L46 | 补约束 |
| 3 | `readonly struct DownloadAttempt` 含可变字段（CS8340） | L51 `SimulatedCdn.cs` | 去掉 `readonly` |
| 4 | `AssetSystem.Storage.AssetFileStore` 相对命名空间在隔离后失效 | L35 `PatchStagingStore.cs` | 改为全限定名 |
| 5 | 缺 using：`IntegrationMainThreadPump`、`Lesson43GraphNode` | L52、L43 | 补 using |
| 6 | 校验器 `Unload(false)` 泄漏 Bundle，污染同一 Editor 会话后续加载 | L43 `Lesson43BundleValidator` | 改为 `Unload(true)` |
| 7 | v1/v2 Bundle 含相同资源时，切换版本触发 “another AssetBundle with the same files” | 全链路运行器 | 激活后 `UnloadAllAssetBundles(true)`（对应“激活 = 重建运行时”） |

## 5. 全链路 Demo（跨课程串联）

### 5.1 它串了什么

```
4.1 扫描/分组  →  4.2 依赖图 + Manifest  →  4.3 真实 AssetBundle 构建
        ↓（构建产物落到磁盘）
5.2 RealBundlePayloadFactory（真实 AssetBundle.LoadFromFile）
        ↓
5.1 PatchActivationService（Hash/平台校验）→ ActiveContentContext
        ↓
5.1 BundleRuntime（引用计数）→ AssetCache / AssetScope / AssetHandle
        ↓
5.1 AssetMemoryEvaluator（先 Asset 后 Bundle）
```

文档原本的 5.1 场景 S 用模拟 backend 跑；这个 demo 把 **4.3 的真实构建产物**接进去，补上了文档里断掉的“构建 → 产物 → 被运行时消费”一段。

### 5.2 怎么用

1. 菜单 `Tools/Game Asset System/Build Full-Chain Demo Content`
   - 生成 4.3 示例内容（5 个 ScriptableObject）
   - 构建 v1 真实 Bundle 到 `GameAssetSystemBuild/Lesson43/<平台>/fullchain-v1`
   - 修改 `ShopRoot` 标签为 `ShopRoot_v2` 后构建 v2
   - 还原示例内容，避免污染其它课程
2. 直接进 Play 模式，看控制台 `[FullChain][A]…[G]` 阶段日志；
   或运行 PlayMode 测试 `FullChain_RealBundlesAcrossVersionSwitch`。
3. 内容缺失时 demo 只打 warning 并提示先执行菜单，不会报错。

### 5.3 断言（真实产物，不是模拟）

| 阶段 | 断言 |
|---|---|
| A 冷启动 | v1 激活 `Succeeded`，3 bundles / 5 assets |
| B 共享加载 | 2 个 owner 只触发 1 次 Bundle 加载，拿到同一 Asset 实例 |
| C 释放 UI | 活动 Handle 剩 1，Battle 不受影响 |
| D 有 Handle 激活 | `RejectedActiveHandles` |
| E 非安全点激活 | `RejectedUnsafePoint` |
| F 安全点激活 | `Succeeded`，运行时重建 |
| G v2 加载 | 标签变为 `ShopRoot_v2`（内容真的换了） |
| G 回收 | 至少 1 个 Asset + 1 个 Bundle 被回收，Handle 归零 |

## 6. 交互演示（可点击）

`Assets/Game/AssetSystem/Runtime/Demos/Interactive/` 提供一套**零预制体、全运行时构建**的交互界面：Canvas、字体、圆角贴图、按钮、RenderTexture 视口都在代码里生成，因此在任何场景按 Play 都能跑。

### 6.1 界面分区

| 区域 | 内容 |
|---|---|
| 顶栏 | 版本徽章、`激活 v2` / `激活 v1` / `全部释放` / `回收内存` |
| 左栏 | 5 个资产行：key、所属 Bundle、状态（Ready / Idle / NotLoaded）、refs 条、`UI` 与 `Battle` 两个 owner 按钮 |
| 右上视口 | 独立相机渲染到 RenderTexture：每个已加载资产一个方块，颜色由 key 决定，尺寸随引用数变化，Idle 时变暗 |
| 右中 | Bundle 运行时：每个 Bundle 的状态与 lease 数（Idle 表示被 Asset 缓存持有） |
| 右下 | 统计（活动 Handle / Ready·Idle / Bundle 加载次数 / 累计回收 / 激活次数）+ 操作日志 |

### 6.2 交互清单

- **单资产加载/释放**：点 `UI` 或 `Battle` 按钮；同一个 key 两个 owner 都持有时，日志里 `bundleLoads` 不变 → 去重可见。
- **一键演示**：左栏顶部按钮，自动把 5 个资产按 UI 加载、再挑 2 个给 Battle，适合快速看全貌。
- **全部释放**：所有 Handle 归零，资产转 Idle（payload 保留），方块变暗。
- **回收内存**：按预算先卸 Asset 再卸 Bundle，方块消失，统计累加。
- **激活 v2 / v1**：有 Handle 时被拒（`RejectedActiveHandles`）并提示先释放；成功则重建运行时，方块清空，之后再加载会命中新版本（`ShopRoot_v2`）。
- **内容缺失**：若还没构建资源，界面显示引导卡片 + `重新检测` 按钮，不会直接报错。

### 6.3 实测

| 截图 | 场景 | 关键读数 |
|---|---|---|
| `Captures/demo-1-loaded.png` | 一键演示后 | 7 Handle / 5 Ready / Bundle 加载 3 次（5 个资产只加载 3 个 Bundle） |
| `Captures/demo-2-idle.png` | 全部释放 | Handle 0，资产 Idle（payload 保留），Bundle `Idle(被缓存持有) leases=1/2/2` |
| `Captures/demo-3-reclaimed.png` | 回收内存 | Asset 5 / Bundle 3 回收，全部 NotLoaded，方块消失 |
| `Captures/demo-4-v2.png` | 激活 v2 后再加载 | 版本徽章 `fullchain-v2`，日志出现 `→ ShopRoot_v2`，激活次数 2 |

### 6.4 进入方式（三个 demo 怎么跑）

整理后有三个 demo，**各有一个专属场景，进场景按 Play 就跑，不再用菜单切模式**。
三个场景都需要先有真实 Bundle 产物。

| 场景 | 入口 | 跑什么 |
|---|---|---|
| `Assets/Scenes/Demo_Interactive.unity` | `[DemoBootstrap] Interactive` → `InteractiveDemoBootstrap` | 可点击资产流：加载 / 共享去重 / 释放 / 回收 / v1↔v2 切换 + 3D 视口 |
| `Assets/Scenes/Demo_FullChain.unity` | `[DemoBootstrap] FullChain` → `FullChainDemoBootstrap` | 端到端链路：Console 输出 `[FullChain][A]…[G]` + summary，HUD 同步镜像 |
| `Assets/Scenes/Demo_FullFeature.unity` | `[DemoBootstrap] FullFeature` → `FullFeatureDemoBootstrap` | 全功能交互：依赖租约 / 共享 / 加载预算 / 下载中断续传 / 两阶段激活 / 内存分级回收（见 **6.7**） |

用法：

1. 首次或产物缺失时，先执行一次 `Tools/Game Asset System/Build Full-Chain Demo Content`
   （`Demo_FullFeature` 用 `Tools/Game Asset System/Build Full-Feature Demo Content`）；
2. 双击 `Assets/Scenes/Demo_Interactive.unity` → 按 **Play** → 出现 `[AssetDemo]`；
3. 双击 `Assets/Scenes/Demo_FullChain.unity` → 按 **Play** → 出现 `[FullChainScenarioDemo]` + `[AssetSystemHud]`；
4. 双击 `Assets/Scenes/Demo_FullFeature.unity` → 按 **Play** → 出现全功能面板（HUD 会被面板隐藏）；
5. 其他场景（如 `SampleScene`）按 Play 不会再启动任何 demo。

三个场景都已加入 Build Settings，可直接在 Build Profile 里单独构建。

- 一键版：`Tools/Game Asset System/Build Content And Play Demo`（构建后自动进 Play，进的是当前打开的场景）。
- 清理产物：`Tools/Game Asset System/Clean Full-Chain Demo Content`。
- HUD 快捷键：`F1` 显示/隐藏、`F2` 清空。

### 6.5 课程 demo 去哪了（课程 → 用例对照）

原工程每节课都自带一个控制台 demo（`Lessons/L31..L52` 下的 `*LessonBootstrap`，4.1/4.2/4.3 是编辑器窗口），
整理时随课程目录一并删除。**它们的场景行为没有消失**：

- 12 个运行时控制台 demo 已还原到统一场景 `Assets/Scenes/Demo_Lessons.unity`，由一个枚举选择（见下）。
- 同样的场景也被断言化用例覆盖：失败会直接指出断言，比翻日志更可靠。

**还原后的统一入口（Demo_Lessons）**

| 项 | 说明 |
|---|---|
| 场景 | `Assets/Scenes/Demo_Lessons.unity`（已加入 Build Settings） |
| 入口组件 | `LessonDemoBootstrap`，挂在 `[LessonDemoBootstrap]` 物体上 |
| 选择方式 | Inspector 的 `Demo` 枚举（`LessonDemoKind`），或代码调用 `Select(kind)` + `RunSelected()` |
| 可选课程 | 3.1 / 3.2 / 3.3 / 3.4 / 3.5 / 3.6 / 4.4 / 4.5 / 4.6 / 4.7 / 5.1 / 5.2 |
| 代码位置 | `Assets/Game/AssetSystem/Runtime/Demos/Lessons/`（每课一个 `Lxx*Demo.cs`，公共替身在 `Support/`） |
| 运行方式 | 打开场景 → 选枚举 → Play；控制台仍是原来的 `[Lesson]` / `[Scenario]` 前缀 |

> 还原的只是 demo 脚本，没有还原 `Lessons/` 下的逐课框架快照。所有 demo 直接引用当前
> `Game.AssetSystem.*`（`MainThreadPump`、`Loading`、`Scheduling`、`Storage`、`Patch`、`Bundles`、
> `Assets`、`Memory`、`Integration`、`Release`），因此能持续跟着框架演进。
>
> 4.1 / 4.2 / 4.3 是编辑器窗口（扫描、依赖图、Bundle 构建），不属于“场景 + 枚举”模型，
> 已单独还原为 `Editor/Inspector/` 下的三个诊断窗口，见 **6.6**。

| 课程 | 原 demo | 现在的等价入口（测试用例） |
|---|---|---|
| 3.1 主线程亲和 | `MainThreadRunnerLessonBootstrap` | `RuntimeScenarioTests.L31_WorkerCompletionIsDeliveredOnMainThread` |
| 3.2 可取消加载 | `CancellationLessonBootstrap` | `RuntimeScenarioTests.L32_OneWaiterCanceled_OtherWaiterStillSucceeds`、`L32_AllWaitersCanceled_LateSuccessIsAbandoned` |
| 3.3 共享加载 | `SharedLoadRegistryLessonBootstrap` | `RuntimeScenarioTests.L33_TwoWaitersReuseOneOperation`、`L33_LastWaiterCanceled_NextLoadCreatesNewOperation` |
| 3.4 文件事务 | `FileTransactionLessonBootstrap` | `PersistenceScenarioTests.L34_HashGuardBlocksBadCommitAndBadActivation` |
| 3.5 补丁下载 | `PatchDownloadLessonBootstrap` | `PersistenceScenarioTests.L35_DownloadInterruptedThenResumes`、`BuildPipelineTests.L35_StorageRejectsUnsafeFileNames` |
| 3.6 调度预算 | `SchedulerLessonBootstrap` | `RuntimeScenarioTests.L36_HighPriorityStartsFirst_WithinBudget`、`L36_ScopeDisposeCancelsQueuedLoads` |
| 4.1 构建期扫描 | `AssetScanLessonWindow`（编辑器窗口） | `BuildPipelineTests.L41_ScanReportsUnmatchedInvalidAndDuplicateKeys`、`L41_AssetKeyFormatRejectsInvalidKeys` |
| 4.2 依赖图 | `DependencyGraphLessonWindow`（编辑器窗口） | `BuildPipelineTests.L42_CleanGraphBuildsManifest` |
| 4.3 Bundle 构建 | `Lesson43BuildWindow`（编辑器窗口） | `BuildPipelineTests.L43_BuildsRealBundlesAndRejectsWrongPlatform`；产物由 `FullChainContentTests.FullChain_BuildsV1AndV2Bundles` 生成 |
| 4.4 BundleRuntime | `BundleRuntimeLessonBootstrap` | `RuntimeScenarioTests.L44_SharedBundleRefCountAndDependencyLeases`、`L44_CancelOneWaiter_OtherGetsLease`、`L44_MissingDependencyFailsWithoutLease` |
| 4.5 资源缓存 | `AssetCacheLessonBootstrap` | `FullChainScenarioTests.FullChain_RealBundlesAcrossVersionSwitch`（共享加载 / Idle / 释放段） |
| 4.6 内存策略 | `AssetMemoryLessonBootstrap` | 同上（`reclaimedAssets` / `reclaimedBundles` / Handle 归零断言） |
| 4.7 补丁激活 | `PatchActivationLessonBootstrap` | 同上（`RejectedActiveHandles` / `RejectedUnsafePoint` / 激活后重建段） |
| 5.1 场景 S | `ScenarioSDemo` | `FullChainScenarioTests.FullChain_RealBundlesAcrossVersionSwitch` |
| 5.2 发布验证 | `ReleaseValidationDemo` | `ReleaseGateTests.L52_AssetLoadGateRespectsStartAndConcurrencyBudget`、`L52_InstalledVersionCleanerRetainsActiveAndNewest` |

**怎么跑这些"按课"的场景**

- 图形界面：`Window > General > Test Runner` → 选 `EditMode` / `PlayMode` → 双击单条用例（或选中后 Run Selected）。全部 21 条约 20 秒。
- 命令行（先关掉编辑器，避免工程被锁）：

```bash
"D:/Work/UnityInstall/6000.0.32f1/Editor/Unity.exe" -batchmode -projectPath "D:/Work/Blog/HaloRes2" \
  -runTests -testPlatform PlayMode -testFilter "L36_" -testResults "Logs/L36.xml" -quit
```

  `-testFilter` 支持 `L31_`、`L44_`、`FullChain_` 等前缀；4.1/4.2/4.3/3.5 的用例在 EditMode，需把 `-testPlatform` 换成 `EditMode`。
- 想看该层的 Console 日志：单条跑用例即可，输出仍是 `[SharedLoad]` / `[BundleRuntime]` / `[AssetCache]` / `[FullChain]` 等前缀。

**运行时控制台 demo 与 3 个编辑器窗口都已还原**（见本节开头与 6.6），无需再从 git 取回。
旧提交 `47e44d0` 只作为历史参考保留。

### 6.6 构建期诊断窗口（Inspect 菜单）

4.1 / 4.2 / 4.3 的三个课程窗口已按新命名空间还原到 `Editor/Inspector/`，窗口名去掉课程号，
菜单收拢到 `Tools/Game Asset System/Inspect/` 下。

| 菜单 | 窗口 | 能做什么 |
|---|---|---|
| `Inspect/1. Scan & Group` | `AssetScanWindow` | ① 生成示例内容 ② 扫描 + 校验（显示 scanned / bindings / groups / errors / warnings，错误与警告可滚动查看，并把报告写到 `GameAssetSystemBuild/Lesson41/scan-report.json`）③ 清理示例内容 |
| `Inspect/2. Dependency Graph & Manifest` | `DependencyGraphWindow` | ① 生成干净依赖图 ② 注入缺失依赖 ③ 注入 Bundle 环路 ④ 扫描 + 建 Manifest（写 `GameAssetSystemBuild/Lesson42/manifest-<版本>.json`）⑤ 清理；面板显示扫描/构建汇总与逐条错误（含 edge `from → to`） |
| `Inspect/3. Bundle Build & Validate` | `BundleBuildWindow` | ① 生成示例内容 ② 按当前平台构建 ③ 校验产物 ④ 模拟错误平台校验 ⑤ 破坏第一个 Bundle（验证 hash 失败路径）⑥ 清理；面板显示构建汇总（bundle 数、总大小、输出目录、平台键）与校验错误列表 |

本轮实测（三个窗口均实际点过一遍）：

| 窗口 | 输出 |
|---|---|
| Scan | `scanned=7 bindings=6 groups=3 errors=4 warnings=0`，报告已落盘 |
| Graph | `assets=5 bundles=3 edges=2 errors=0`，manifest 写入 `Lesson42/manifest-graph-v1.json` |
| Build | `assets=5 bundles=3` → `bundle-v1`，校验 `bundlesLoaded=3` |

> 三个窗口只读写 `GameAssetSystemBuild/` 与 `Lesson4xSample/` 下的示例资源，不碰工程其它资产。
> 构建产物的平台目录仍沿用 `GameAssetSystemBuild/Lesson43/<平台>/`，以免已有的全链路产物失效。

### 6.7 全功能交互式 Demo（Demo_FullFeature）

一个场景走完「加载 → 共享 → 释放 → 下载中断 → 续传 → 激活 → 重建运行时 → 内存回收」全链路，
全部按钮直接调用框架 API，界面上显示的每个数字都是运行时快照读回来的，没有模拟状态。

**代码位置**

| 文件 | 作用 |
|---|---|
| `Runtime/Demos/FullFeature/FullFeatureSession.cs` | 客户端全流程：首启安装 / 下载 / 激活 / 重建 / 加载 / 释放 / 分级回收 + 每帧 Tick |
| `Runtime/Demos/FullFeature/FullFeatureDashboard.cs` | uGUI 面板（零预制体，复用 `AssetDemoUiKit`） |
| `Runtime/Demos/FullFeature/FullFeatureLayout.cs` | 服务端 / 客户端路径契约、构建清单 → `PatchManifest` 转换、`active.json` 结构 |
| `Runtime/Demos/FullFeature/FullFeatureScenario.cs` | 同一套调用的脚本化版本，PlayMode 用例直接跑它 |
| `Runtime/Demos/FullFeature/SharedInfo.cs` / `AssetInfo.cs` / `FullFeatureContent.cs` | 示例内容类型与 Key / Bundle 组名 |
| `Editor/Building/FullFeatureContentBuilder.cs` | 生成 v1/v2/v3 内容并构建 3 组 Bundle，菜单 `Tools/Game Asset System/Build Full-Feature Demo Content` |

**内容与依赖**

```text
GameAssetSystemBuild/FullFeature/<平台>/{v1,v2,v3}/
├─ ui_shop     (Panel.asset, 依赖 shared)
├─ battle_fx   (Hit.asset,   依赖 shared)
├─ shared      (Shared.asset)
└─ lesson43-manifest.json   ← bundles[].dependencies 写明依赖
```

运行时把该目录当「服务端」，首次启动把 v1 拷进
`Application.persistentDataPath/AssetSystemFullFeatureDemo/content/v1/`，
之后所有版本都从 staging（`.part` + `.part.meta`）下载、校验、换入。

**界面分区**

| 区域 | 内容 |
|---|---|
| 状态行 | Active 版本 / SafePoint / 每帧加载预算 / Scope 数；Assets 与 Bundles 的 R·I·L·H 计数；内存压力与暂存文件 |
| ① 加载与共享 | `ui/panel(A)`、`fx/hit(B)`、`ui/panel(C)`、释放全部 Handle |
| ② 安全点 | 进入主菜单（ON）/ 进入战斗（OFF） |
| ③ Patch 下载 | v1/v2/v3 选择、Range 开关、完整下载 / 70% 中断 / 中断当前下载、进度条与结果 |
| ④ 激活 | 激活选中版本（校验 Hash → 活动 Handle → 安全点 → Swap → 重建运行时） |
| ⑤ 内存压力 | Medium / High / Critical / 恢复正常，分级预算 2 / 6 / 64 次卸载 |
| ⑥ 日志 | 最近 8 条，带时间戳 |
| 右上「当前内容」 | Active 版本、最近加载的 label、`shared.version`、`shared.color` 色块 |
| 右下「Bundle / 资产状态」 | 每个 Bundle 的 State / refs / 是否持有 payload、Idle 资产列表、暂存与状态目录、建议操作顺序 |

**实测读数（真实点过一遍）**

| 步骤 | 读数 |
|---|---|
| 首启 | `恢复/安装 v1` → `[Activation] <none> -> v1` → 面板 Active v1，Bundles 全 NotLoaded |
| 加载 `ui/panel (A)` | `Assets R1/I0/L0/H1`、`Bundles R2/I0/L2/Ref2`、表格里 `shared` 与 `ui_shop` 均 Ready 且 `payload`、色块 `#E65252`（v1） |
| 70% 中断 | 进度条停在 ~70%，`Failed · 提交 2 · 跳过 0 · 续传 0`，`暂存 v2 1 个 .part（194 字节）· 1 个 .meta` |
| 完整下载 | `提交 1 · 跳过 2 · 续传 194 字节`，暂存目录清空 |
| 有 Handle 时激活 | `RejectedActiveHandles Cannot activate while 1 live handle(s) exist.` |
| 安全点 OFF 激活 | `RejectedUnsafePoint` |
| 安全点 ON 激活 | `Succeeded Activated v2 (3 bundles, 3 assets)`，运行时重建，计数归零 |
| 切版后加载 | `v2/UI/Panel`、`shared.version v2`、色块 `#4CCC7A`（绿） |

**框架侧为它补的能力（都是可选参数，不改变既有调用行为）**

| 能力 | 位置 |
|---|---|
| `RuntimeBundle.DirectDependencies` + `PatchBundleDescriptor.Dependencies` | `Integration/ActiveContentContext.cs`、`Integration/Patch/PatchTypes.cs` |
| 激活时解析依赖组名、拒绝未知依赖 / 自依赖 / 环 | `Integration/Patch/PatchActivationService.cs` |
| Bundle 先加载依赖、卸载时「先 payload 后依赖租约」 | `Integration/ContentBundleRuntime.cs` |
| `SnapshotBundles()` / `TotalUserRefs` 供面板只读展示 | 同上 |
| 构建清单携带 `dependencies`、`Build()` 可指定输出根 | `Editor/Building/{BuildTypes,AssetBundleBuilder}.cs` |

## 7. 测试清单与结果

### 7.1 EditMode（7/7 通过）

| 用例 | 对应文档 | 断言要点 |
|---|---|---|
| `L41_ScanReportsUnmatchedInvalidAndDuplicateKeys` | 4.1 §5.3 | scanned=7 / bindings=6 / groups=3 / errors=4，含三类错误码，`HasFatalErrors` |
| `L41_AssetKeyFormatRejectsInvalidKeys` | 4.1 命名规则 | 合法 key 通过；空格、空串被拒 |
| `L42_CleanGraphBuildsManifest` | 4.2 §5.2 | 5 assets / 3 bundles / 2 edges / 0 errors |
| `L43_BuildsRealBundlesAndRejectsWrongPlatform` | 4.3 §5.2 §5.3 | 真实构建 3 Bundle，`LoadAsset` 校验 5/5；错误平台必须失败 |
| `L35_StorageRejectsUnsafeFileNames` | 3.5 文件名安全 | 拒绝 `../`、路径分隔符、空名 |
| `FullChain_BuildsV1AndV2Bundles` | 全链路前置 | v1/v2 清单与 Bundle 文件存在且不同 |
| `FullFeature_BuildsThreeVersionsWithSharedDependency` | 6.7 前置 | v1/v2/v3 各 3 Bundle / 3 资产；`ui_shop`、`battle_fx` 的 `dependencies` 恰为 `shared`，`shared` 无依赖 |

### 7.2 PlayMode（24/24 通过）

| 用例 | 对应文档 | 断言要点 |
|---|---|---|
| `L31_WorkerCompletionIsDeliveredOnMainThread` | 3.1 | 完成回调线程 == 主线程 |
| `L32_OneWaiterCanceled_OtherWaiterStillSucceeds` | 3.2 场景 1 | 取消不影响他人；只 Begin 一次、只创建一次 payload |
| `L32_AllWaitersCanceled_LateSuccessIsAbandoned` | 3.2 场景 2 | 迟到成功恰好释放 1 次 |
| `L33_TwoWaitersReuseOneOperation` | 3.3 场景 1 | created=1 / reused=1 / backend Begin=1，完成后 detach |
| `L33_LastWaiterCanceled_NextLoadCreatesNewOperation` | 3.3 场景 2 | 取消即 detach，新请求 created=2 |
| `L34_HashGuardBlocksBadCommitAndBadActivation` | 3.4 | 错 Hash 不提交；缺 Bundle 不激活；active 指针不变 |
| `L35_DownloadInterruptedThenResumes` | 3.5 | 中断保留 `.part`；重建控制器后按 offset 续传并激活 v2 |
| `L36_HighPriorityStartsFirst_WithinBudget` | 3.6 场景 1 | high 先启动；每帧 ≤2；并发 ≤2 |
| `L36_ScopeDisposeCancelsQueuedLoads` | 3.6 场景 2 | 排队任务全部取消且不进 worker |
| `L36_CancelAfterWorkerFinished_ReleasesAbandonedResult` | 3.6 取消竞态 | worker 已产出后取消：任务记 Canceled，结果走 `releaseAbandoned` 恰好释放 1 次 |
| `L44_SharedBundleRefCountAndDependencyLeases` | 4.4 场景 1 | 只加载一次；Idle 保留 payload；依赖租约随父卸载释放 |
| `L44_CancelOneWaiter_OtherGetsLease` | 4.4 场景 2 | 取消者无 lease；Bundle 只启动一次 |
| `L44_MissingDependencyFailsWithoutLease` | 4.4 场景 3 | `DependencyMissing` 结构化失败，无悬挂引用 |
| `L47_ActivationRejectsBundleNameEscapingRoot` | 4.7 文件名安全 | `../escape.bundle` → `InvalidManifest`，ActiveContext 不变 |
| `L47_ActivationRejectsPlatformMismatch` | 4.7 §5.3 | `PlatformMismatch`，不激活 |
| `L47_ActivationReportsMissingBundle` | 4.7 §5.3 | `BundleMissing`，不激活 |
| `L47_ActivationReportsHashMismatch` | 4.7 §5.3 | 尺寸对、Hash 错 → `HashMismatch`，不激活 |
| `L47_ActivationRechecksHandlesInsideSwap` | 4.7 检查与 Swap 的窗口 | 预检读到 0、Swap 内读到 1 → `RejectedActiveHandles`，`ActivationCount==0` |
| `L52_AssetLoadGateRespectsStartAndConcurrencyBudget` | 5.2 场景 1 | 每帧启动 ≤2、并发 ≤2、全部完成 |
| `L52_InstalledVersionCleanerRetainsActiveAndNewest` | 5.2 场景 3 | 保留 active + 最新 1 个；有 `.part` 的 staging 版本不删，空 staging 目录不占用 |
| `L52_InstalledVersionCleanerOrdersVersionsNumerically` | 5.2 版本排序 | v8 激活时保留 v10、删 v9（ordinal 会反） |
| `FullChain_RealBundlesAcrossVersionSwitch` | 5.1 场景 S + 4.3/5.2 | 见 5.3 表格，全部通过 |
| `FullFeature_ShareInterruptResumeActivateReclaim` | 6.7 全链路 | 首启 v1；首次加载 2 payload（ui_shop+shared），battle_fx 复用 shared（3），缓存命中不再加载；释放后 2 Idle 资产 / 3 payload 保留；Critical 回收归零；70% 中断留 `.part`、续传 `resume>0`；有 Handle / 无安全点被拒；激活 v2 后 `shared.version=v2`；最终 payload 归零 |

### 7.3 演示冒烟

整理后有三个演示（交互 DemoApp / 全链路 FullChain / 全功能 FullFeature），各自绑定一个场景，进入哪个场景就跑哪个。PlayMode 用例在 `InitTestScene` 里运行，不会加载 demo 场景，控制台不产生无关 `LogError`。

## 8. 覆盖矩阵与剩余缺口

### 8.1 已自动化

逐课"原 demo → 现在的用例"精确对照见 **6.5**，这里只按能力维度汇总。

| 课程 | 覆盖方式 |
|---|---|
| 3.1 / 3.2 / 3.3 | 独立 PlayMode 用例 |
| 3.4 / 3.5 | 独立 PlayMode 用例（真实文件 IO） |
| 3.6 | 独立 PlayMode 用例（优先级 + 双预算 + Scope 取消） |
| 4.1 / 4.2 / 4.3 | 独立 EditMode 用例（含真实 Bundle 构建与 `LoadAsset` 校验） |
| 4.4 | 独立 PlayMode 用例（引用计数 / 依赖租约 / 缺依赖） |
| 4.5 / 4.6 | 由全链路用例覆盖：`AssetCache`/`AssetScope`/`AssetHandle` 共享与释放、`AssetMemoryEvaluator` 回收（Asset 先于 Bundle）、Handle 归零 |
| 4.7 | 独立 PlayMode 用例（`ActivationGuardTests`）：文件名越界 / 平台不符 / 缺 Bundle / Hash 错 / Swap 内句柄复核；主路径仍由全链路用例覆盖 |
| 5.1 | 全链路用例（场景 S 主路径 + 三条拒绝路径） |
| 5.2 | `AssetLoadGate` / `InstalledVersionCleaner` 独立用例；`RealBundlePayloadFactory` 由全链路用例覆盖 |

### 8.2 仍待补（明确记录，不假装已覆盖）

| 缺口 | 现状 | 补齐建议 |
|---|---|---|
| 4.5 / 4.6 的**独立**单测 | 仅集成层断言 | 补 `AssetRuntime` 的 Scope 释放/失败路径、`AssetMemoryPolicy` 预算与诊断快照的独立用例 |
| 5.2 `SafePointGate` | 未单测 | 补“有待处理请求时安全点不可用”的用例 |
| 3.5 服务器不支持 Range 的回退 | 未单测 | 设 `server.SupportsRange=false` 且已有 `.part`，断言删除 `.part` 并从 0 重下 |
| 4.2 环依赖 / 4.3 hash 损坏的失败用例 | 只有主路径 | 构造环依赖图与损坏 Bundle，断言结构化错误码 |
| PlayMode 全链路在缺内容时 `Assert.Ignore` | 未构建时是“假绿” | CI 先跑 `Build Full-Chain Demo Content`，再跑 PlayMode |
| 构建期 CI 脚本 | 仅静态落地 | 需要真实 Unity 命令行环境才能验证 |

## 9. 如何运行

### 9.1 为什么场景里看不到东西

这套框架是**纯逻辑库**，没有任何可渲染内容：

- 场景只有 `Assets/Scenes/SampleScene.unity`（Main Camera / Directional Light / Global Volume 三个默认物体）；
- 演示在进入 Play 时由 `[RuntimeInitializeOnLoadMethod]` 创建 `[...]` 物体，挂在 DontDestroyOnLoad 分区，场景视图里看不到；
- 所有证据都是 Console 日志。

因此「打开工程 → 空场景」是正常现象，不是资源丢失。

### 9.2 屏上 HUD

`Runtime/Demos/Diagnostics/AssetSystemHud.cs` 在 Play 时自动创建，左上角实时镜像框架日志：

- `F1` 隐藏/显示，`F2` 清空；
- 面板顶部显示当前场景、AutoBoot 状态、FPS；
- 只抓取 `[Lesson*]` / `[FullChain*]` / `[Scenario*]` / `[Registry*]` 前缀和 Error/Warning，避免刷屏；
- HUD 与演示解耦：任何场景进 Play 都会创建，交互演示会把它隐藏（界面自带日志面板）。

### 9.3 命令

- **跑测试**：`Window > General > Test Runner` → EditMode / PlayMode → Run All。
- **看交互演示**：打开 `Assets/Scenes/Demo_Interactive.unity`，按 Play。
- **看全链路日志演示**：打开 `Assets/Scenes/Demo_FullChain.unity`，按 Play。
- **看全功能交互演示**：`Tools/Game Asset System/Build Full-Feature Demo Content` 一次，
  再打开 `Assets/Scenes/Demo_FullFeature.unity` 按 Play（见 6.7）。
- **测试与演示互不干扰**：测试不加载 demo 场景，演示也不再自动启动，
  所以跑测试不会被演示日志污染（15 个 PlayMode 用例已验证）。
- **构建期诊断窗口**：`Tools/Game Asset System/Inspect/1. Scan & Group`、`2. Dependency Graph & Manifest`、`3. Bundle Build & Validate`（见 6.6）。
- **构建期一键流程**：`Tools/Game Asset System/Build Full-Chain Demo Content`（4.1→4.2→4.3 全流程）。
- **全功能 Demo 内容**：`Tools/Game Asset System/Build Full-Feature Demo Content`（v1/v2/v3 三套，含依赖声明）。
- **一键构建 + 进演示**：`Tools/Game Asset System/Build Content And Play Demo`。

## 10. 代码审查复核（对当前合并后代码）

外部审查是按 15 份课程快照写的（`L44/Bundles/BundleRuntime.cs` 之类）。合并成一份最终版之后，
**每个问题只剩一处副本**，"只改最新一课不会改掉前面的副本"这条前提已经消失。逐条对当前源码复核如下。

### 10.1 高优先级

| # | 当前位置 | 是否成立 | 处理 |
|---|---|---|---|
| 1 | `Runtime/Bundles/BundleRuntime.cs` `TryUnloadIdle` | 成立 | **已修**：先 `payload.Release()`，再释放依赖租约；与资产层"先资产后 Bundle"一致 |
| 2 | `Runtime/Scheduling/AssetLoadScheduler.cs` `OnWorkerCompleted` | 成立 | **已修**：新增 `releaseAbandoned` 钩子（`Enqueue` 可选参数），取消时把 worker 已产出的结果交还调用方释放；默认兜底 `IReleasableAssetPayload` / `IDisposable` |
| 3 | 同文件 `Tick()` | 成立 | **已修**：预算绑 `Time.frameCount`，同帧多次 `Tick()` 只消耗剩余配额 |
| 4 | `Runtime/Storage/AssetFileStore.cs` `CommitBundleFromTemp` | 成立 | **已修**：终态 Hash 失败删除目标并从 `.bak` 回滚；`.bak` 保留到校验通过再删；`active.json.bak` 也不再立刻删除，`TryLoadActiveState` 的恢复分支因此真正可达 |
| 5 | `Runtime/Integration/Patch/*` | 成立 | **已修**：新增 `Storage/PathSafety`，`GetFullPath` 后要求仍在 root 之下；接入激活、下载、staging 三处 |
| 6 | `PatchActivationService.ActivateAsync/Swap` | 成立 | **已修**：`Swap` 内用 `liveHandlesProvider` / `safePointProvider` 复核；预检与 Swap 各读一次，窗口内的新 Handle 会挡住激活 |
| 7 | `Runtime/Demos/Interactive/AssetDemoController.cs` `UpdateCubes` | 部分成立 | **已修**：`Renderer.material` 的 getter 只在首次实例化，不是"每帧新建"，但每帧访问 getter 仍依赖隐式实例化。改为 `CreateCube` 建一份实例、行内缓存、`sharedMaterial` 赋值，销毁时只销毁这一份 |
| 8 | 同文件 `Toggle` / `SwitchVersion` | 成立 | **已修**：切换期间置 `_switching` 拒绝新加载；`RebuildRuntime` 提升代际；完成回调比对代际，过期结果直接 Dispose 不写回行 |

### 10.2 中优先级

| # | 结论 | 处理 |
|---|---|---|
| 取消的排队任务占扫描预算 | 成立 | **已修**：已取消/已完成的条目不再消耗 `MaxPendingScanPerFrame`，单帧最多扫一趟队列 |
| L45 的 `AssetCacheRegistry` 未接线 | **不适用** | 合并后没有 `AssetCacheRegistry`；`AssetCache` 自持 entry 表，`CancelWaiter` 在 entry 缺失时直接 `CompleteCanceled`，不会落到 `InternalError` |
| CancellationToken 只在启动瞬间看一眼 | 部分成立 | **已修一半**：`BundleLease.Dispose` 在后台线程改为 Post 到主线程，不再抛 `EnsureMainThread`；**未修** token 侧取消注册（现有 demo/用例都走显式 `Cancel()`，语义等价，登记为后续项） |
| 同一 `.part` 没有串行化 | 部分成立 | **已修一半**：`PatchDownloadService` 提交前先删同名目标，消除 Windows 上 `File.Move` 目标存在的抛错与半套版本；**未修** 同一 `.part` 的并发写串行化（需要按版本加锁，登记为后续项） |
| 真实 Bundle 回收卸不干净 | 成立 | **未修**：`RealBundlePayload.Release` 仍是 `Unload(false)`，`LoadFromFile` 也不按绝对路径去重。改成 `Unload(true)` 会影响仍在句柄外持有对象的调用方，属于语义变更，需单独评估 |
| 版本清理按字符串排序 | 成立 | **已修**：按尾部数字比较（v10 > v9）；staging 目录只有还留着文件才算占用，空目录顺手删除并补了用例 |
| 构建失败会毁掉上一份成功产物 | 成立 | **已修**：构建先写 `<平台>/.building-<guid>/<版本>/`，清单写完再整体换入；换入失败回滚 `.previous`；失败路径清理暂存目录 |
| 全链路会弄脏编辑器会话 | 部分成立 | **已修**：构建前校验 Active Build Target 的平台键与 `FullChainLayout.PlatformKey` 一致（不一致直接报错），末尾日志改打印真实 key；v2 标记改成 `try/finally` 恢复。**未修** `FullChainRunner` 失败路径不卸载已加载 Bundle（演示/测试代码，登记为后续项） |

### 10.3 复核后的验证

| 项 | 结果 |
|---|---|
| 编译 | 0 error（Unity 6000.0.32f1） |
| EditMode | 6/6 通过（含真实 Bundle 构建，验证暂存目录换入后产物结构不变） |
| PlayMode | 23/23 通过（新增 7 条：取消释放、4.7 四类拒绝、Swap 复核、版本数字排序） |
| 构建产物 | `GameAssetSystemBuild/Lesson43/StandaloneWindows64/` 下无 `.building` / `.previous` 残留 |

## 11. 结论

- 15 个课程快照已合并为一份最终版框架（71 个文件 / 17,460 行），编译零错误；
- 新增的全链路 demo 用**真实 AssetBundle 产物**跑通“构建 → 激活 → 加载 → 回收”，补上了文档里缺失的那一段；
- 新增可点击的交互演示：真实 Bundle + 引用计数 + 版本切换 + 内存回收全部可视化，零预制体、任何场景可跑；
- 29 个用例全部通过，覆盖并发/取消/引用计数/文件事务/断点续传/调度预算/构建期校验/版本切换/激活拒绝；
- 文档自身存在 7 类缺陷（含 2 类跨版本运行时缺陷），已修复并记录；
- 外部审查的 8 条高优先级问题在当前代码上全部成立，已全部修复；8 条中优先级中 5 条已修、1 条不适用、2 条部分修复（见第 10 节）；
- 剩余缺口在 8.2 中列明，其中 4.7 的拒绝路径已补上直接断言，`SafePointGate` 与 3.5 Range 回退仍待补。

## 12. 去重与不变量收敛（第二轮）

第一轮把 15 份课程快照合并成一份，但**同一份能力仍在不同目录里各有一份实现**。第二轮按"先定义不变量、再决定留哪份"的顺序收敛。

### 12.1 收敛前的重复清单

| 重复物 | 位置 A | 位置 B | 差异 |
|---|---|---|---|
| BundleRuntime | `Bundles/BundleRuntime.cs` | `Integration/ContentBundleRuntime.cs` | A 有环检测/策略/诊断，B 有计数器；互不包含 |
| AssetCache | `Assets/AssetRuntime.cs` | `Integration/AssetCache.cs` | A 走 catalog+策略，B 走 ActiveContentContext |
| PatchStagingStore | `Patch/` | `Integration/Patch/` | 根目录、meta 字段、路径校验器都不同 |
| PatchManifest | `Patch/PatchTypes.cs` | `Integration/Patch/PatchTypes.cs` | 小写+`url` vs Pascal+`PlatformKey/Assets/Dependencies` |
| 值类型 | `Integration/Types.cs` | `Assets/` `Bundles/` | `AssetKey`/`BundleId`/`AssetLoadResult` 各两份 |
| 传输抽象 | `Patch/IPatchTransport.cs` | `Integration/Patch/IContentTransport` | 后者把 `SimulatedCdnServer` 写进了接口签名 |
| 激活 | `AssetFileStore.ActivateVersion` | `PatchActivationService` | 两条路径，一条只换指针 |
| 优先级策略 | `MainThreadPump` 三档轮转 | `AssetLoadScheduler` 连续配额 | 同一规则写两遍，规则不同 |
| 模拟后端 | Lessons / Tests / Integration 各一份 | — | 3 份 CDN + 2 份 BundleLoader，几乎逐字相同 |

### 12.2 处理结果

| 动作 | 内容 |
|---|---|
| 保留 | `Bundles` + `Assets` 作为唯一运行时；`Integration` 目录整体删除 |
| 降级 | `ActiveContentContext` 只描述内容，通过 `CreateBundleCatalog()` / `CreateAssetCatalog()` 投影给运行时 |
| 合并 | Patch 清单/结果/进度合成一套（`PatchTypes.cs`，Pascal + `Url` + 依赖） |
| 合并 | staging 移到 `Storage/`，schema 升到 2（旧 meta 读到即作废重下，无需迁移） |
| 统一 | 传输只留 `IPatchTransport`；模拟 CDN 只留 `Simulation/SimulatedPatchTransport` |
| 统一 | 激活只留 `PatchActivationService`；`PatchApplyController` 增加 `activateAfterApply`，默认不再自己换指针 |
| 新增 | `PumpBudget` 策略对象；`PostTerminal` 统一终态序域 |
| 新增 | `Simulation` 程序集：一套可参数化的替身（延迟/忽略取消/失败点/Range/传输预算） |

### 12.3 不变量与对应断言

| 不变量 | 断言位置 |
|---|---|
| 同一 Bundle 组同时只有一次加载在飞 | `L44_SharedBundleRefCountAndDependencyLeases`、`FullChain` 阶段 B |
| 卸载顺序 payload → 依赖租约、Asset → Bundle | `L46` / 全链路回收断言 |
| 激活是原子操作：平台/句柄/安全点全过才换 | `ActivationGuardTests`、`FullChain` 阶段 D/E/F |
| 同优先级 FIFO、高优先级先出 | `PumpOrderingTests.Pump_SamePriority_KeepsEnqueueOrder` / `Pump_HighDrainsBeforeNormal_EvenWhenPostedLater` |
| Low 预留按槽位计数，不依赖队列占用 | `PumpOrderingTests.Pump_LowReserveIsCounted_NotCursorBased` |
| 取消与完成同序域，胜负由入队顺序决定 | `PumpOrderingTests.Pump_TerminalTransitions_ShareOneQueueSoPostOrderWins` |
| 单次动作超预算必须被记录，不能静默 | `PumpOrderingTests.Pump_ReportsSingleActionThatBlowsTheBudget` |

### 12.4 行为变化（有意为之）

- **Pump 不再是轮转**：优先级从"占 1/3 槽位"变成"严格先出"，低优先级靠 `LowReserveEvery` 防饿死。原先"后入队的高优先级越过先入队的低优先级"由相位决定，现在由写明的规则决定。
- **终态统一 High**：完成/取消/释放走同一队列，取消不再可能被完成"插队"。
- **完成回调经 pump**：`RunLoadAsync` 的 finalize 不再直接跑在同步上下文续体上，而是 Post 回 pump，因此与取消同序域；代价是完成晚一帧生效。
- **激活默认不再由 apply 管线换指针**：`PatchApplyController` 默认 `activateAfterApply: false` 的调用方必须自己调 `PatchActivationService`。
- **程序集改名**：`DemoGraphNode` 等演示类型移入 `Game.AssetSystem.Demos`。AssetBundle 记录的是程序集名，**旧产物必须重建**，否则报 "referenced script is missing"。

### 12.5 验证

| 项 | 结果 |
|---|---|
| 编译 | 0 error |
| EditMode | 7/7 通过 |
| PlayMode | 30/30 通过（新增 6 条 Pump 顺序/预算用例） |
| 演示内容 | 重新执行 `Build Full-Chain Demo Content` 与 `Build Full-Feature Demo Content` 后通过 |

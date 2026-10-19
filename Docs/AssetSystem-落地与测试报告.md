# Unity 资源管理框架：文档落地与测试报告

> 来源文档：`C:\Users\Halo\Desktop\Unity资源管理框架方案选型与约束分析.md`（37914 行 / 1.16 MB / 15 个课程快照）
> 目标工程：`D:\Work\Blog\HaloRes2`（Unity 6000.0.32f1，URP）

---

## 1. 落地结果总览

| 项目 | 数量 | 说明 |
|---|---|---|
| 从文档提取的代码段 | 105 | 88 个唯一路径 + 17 个跨课程重复版本 |
| 整理后的框架文件 | 71 个 `.cs` / 17,460 行 | 原 15 个课程目录共 118 个 `.cs` / 29,389 行 |
| 汇编定义 asmdef | 4 | 1 运行时 + 1 编辑器 + 2 测试 |
| 全链路代码 | 3 | 布局契约、运行器、内容构建器 |
| 交互演示代码 | 2 | 控制器（状态机 + UI + 3D 视口）、uGUI 构建工具 |
| 测试用例 | 21 | EditMode 6、PlayMode 15 |

编译结果：**0 error**；测试结果：**21/21 通过**。

## 2. 目录结构（整理后：只剩一份最终版）

```
Assets/Game/AssetSystem/
├─ Runtime/                             asmdef: Game.AssetSystem（运行时）
│  ├─ Core/                             MainThreadPump（唯一主线程调度器）/ MainThreadAssetTask
│  ├─ Loading/                          可取消加载任务 + 共享加载注册表（3.2/3.3 最终版）
│  ├─ Scheduling/                       优先级 + 双预算调度器（3.6）
│  ├─ Storage/                          文件事务 / Hash 校验 / active 指针（3.4/3.5 最终版）
│  ├─ Patch/                            下载、staging、应用（IPatchTransport 抽象）
│  ├─ Bundles/                          BundleRuntime：引用计数 + 依赖租约 + 快照
│  ├─ Assets/                           AssetCache：Handle / Scope / 加载任务
│  ├─ Memory/                           AssetMemoryPolicy：预算、宽限期、回收
│  ├─ Diagnostics/                      运行时快照
│  ├─ Integration/                      5.1 集成层：ActiveContentContext / ContentBundleRuntime
│  │  └─ Patch/                         Patch 激活 + 下载编排 + 模拟 CDN
│  ├─ Release/                          5.2 发布加固：加载门禁 / 安全点 / 版本清理 / 真实载荷
│  └─ Demos/                            Interactive / FullChain 场景入口 + AssetSystemHud
├─ Editor/                              asmdef: Game.AssetSystem.Editor
│  ├─ Scanning/                         扫描 + 分组规则 + Key 校验
│  ├─ Manifest/                         依赖图 + Manifest 构建
│  ├─ Building/                         AssetBundle 构建 / 校验 / 平台变体 / 全链路内容构建
│  └─ SampleContent/                    测试与 demo 用的示例资源生成器
├─ Tests/
│  ├─ Editor/                           6 个 EditMode 用例
│  └─ PlayMode/                         15 个 PlayMode 用例 + Support/ 测试替身
└─ Lesson4xSample/                      测试生成的示例资源（非代码）
```

命名空间不再带课程后缀：`Game.AssetSystem.Loading`、`Game.AssetSystem.Bundles`、
`Game.AssetSystem.Integration`、`Game.AssetSystem.Release`、`Game.AssetSystem.Editor.Building` ……

## 3. 从 15 份课程快照合并成一份

原工程把文档的 15 个课程快照各自完整落地，同名类型大量重复
（`BundleRuntime` ×3、`AssetCache` ×3、`UnityMainThreadPump` ×5、`AssetScanner` ×2），
只靠 `.L31` / `.L46` 这样的命名空间后缀隔离。整理后：

| 处理 | 内容 |
|---|---|
| 每层只留最新实现 | Loading 取 3.3（`AcquireWaiter` 已被 `TryAcquireWaiter` 取代）、Storage 取 3.5、Scanning 取 4.2、Bundles/Assets 取 4.6（4.4/4.5 被 4.6 覆盖） |
| 主线程调度器合并 | 3 个 `UnityMainThreadRunner` + 5 个 `UnityMainThreadPump` + `IntegrationMainThreadPump` → 1 个 `Core/MainThreadPump`（优先级队列 + 每帧预算 + `EnsureMainThread`） |
| 课程 demo 删除 | 12 个逐课 bootstrap、3 个课程编辑器窗口、L51/L52 场景 demo 全部移除 |
| 类型名去课程号 | `Lesson43AssetBundleBuilder` → `AssetBundleBuilder`、`LessonHud` → `AssetSystemHud`；`LessonBootGate` 直接删除，改由场景入口组件启动 |
| 测试保留并改到新 API | 21 个用例全保留；3.2 的两个用例改到 3.3 的 `TryAcquireWaiter`；测试替身集中到 `Tests/PlayMode/Support` |

被删除的 4.4/4.5/4.7 与 5.1 演示代码，其行为语义仍由全链路用例与 Release 用例覆盖（见第 8 节）。

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

### 6.4 进入方式（两个 demo 怎么跑）

整理后只剩两个 demo，**各有一个专属场景，进场景按 Play 就跑，不再用菜单切模式**。
两个场景都需要先有 v1/v2 真实 Bundle 产物。

| 场景 | 入口 | 跑什么 |
|---|---|---|
| `Assets/Scenes/Demo_Interactive.unity` | `[DemoBootstrap] Interactive` → `InteractiveDemoBootstrap` | 可点击资产流：加载 / 共享去重 / 释放 / 回收 / v1↔v2 切换 + 3D 视口 |
| `Assets/Scenes/Demo_FullChain.unity` | `[DemoBootstrap] FullChain` → `FullChainDemoBootstrap` | 端到端链路：Console 输出 `[FullChain][A]…[G]` + summary，HUD 同步镜像 |

用法：

1. 首次或产物缺失时，先执行一次 `Tools/Game Asset System/Build Full-Chain Demo Content`；
2. 双击 `Assets/Scenes/Demo_Interactive.unity` → 按 **Play** → 出现 `[AssetDemo]`；
3. 双击 `Assets/Scenes/Demo_FullChain.unity` → 按 **Play** → 出现 `[FullChainScenarioDemo]` + `[AssetSystemHud]`；
4. 其他场景（如 `SampleScene`）按 Play 不会再启动任何 demo。

两个场景都已加入 Build Settings，可直接在 Build Profile 里单独构建。

- 一键版：`Tools/Game Asset System/Build Content And Play Demo`（构建后自动进 Play，进的是当前打开的场景）。
- 清理产物：`Tools/Game Asset System/Clean Full-Chain Demo Content`。
- HUD 快捷键：`F1` 显示/隐藏、`F2` 清空。

### 6.5 课程 demo 去哪了（课程 → 用例对照）

原工程每节课都自带一个控制台 demo（`Lessons/L31..L52` 下的 `*LessonBootstrap`，4.1/4.2/4.3 是编辑器窗口），
整理时随课程目录一并删除。**它们没有消失，而是变成了断言化用例**：每条用例跑的就是那节课的场景，
失败会直接指出断言，比翻日志更可靠。

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

**需要恢复某节课的原始控制台 demo**：文件仍在 git 提交 `47e44d0`，例如

```bash
git show 47e44d0:Assets/Game/Lessons/L36/Demo/SchedulerLessonBootstrap.cs
```

取回后必须改命名空间（`.L36` 等后缀已移除）并挂到场景入口组件上，否则不会启动。

## 7. 测试清单与结果

### 7.1 EditMode（6/6 通过，17.7s）

| 用例 | 对应文档 | 断言要点 |
|---|---|---|
| `L41_ScanReportsUnmatchedInvalidAndDuplicateKeys` | 4.1 §5.3 | scanned=7 / bindings=6 / groups=3 / errors=4，含三类错误码，`HasFatalErrors` |
| `L41_AssetKeyFormatRejectsInvalidKeys` | 4.1 命名规则 | 合法 key 通过；空格、空串被拒 |
| `L42_CleanGraphBuildsManifest` | 4.2 §5.2 | 5 assets / 3 bundles / 2 edges / 0 errors |
| `L43_BuildsRealBundlesAndRejectsWrongPlatform` | 4.3 §5.2 §5.3 | 真实构建 3 Bundle，`LoadAsset` 校验 5/5；错误平台必须失败 |
| `L35_StorageRejectsUnsafeFileNames` | 3.5 文件名安全 | 拒绝 `../`、路径分隔符、空名 |
| `FullChain_BuildsV1AndV2Bundles` | 全链路前置 | v1/v2 清单与 Bundle 文件存在且不同 |

### 7.2 PlayMode（15/15 通过，1.8s）

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
| `L44_SharedBundleRefCountAndDependencyLeases` | 4.4 场景 1 | 只加载一次；Idle 保留 payload；依赖租约随父卸载释放 |
| `L44_CancelOneWaiter_OtherGetsLease` | 4.4 场景 2 | 取消者无 lease；Bundle 只启动一次 |
| `L44_MissingDependencyFailsWithoutLease` | 4.4 场景 3 | `DependencyMissing` 结构化失败，无悬挂引用 |
| `L52_AssetLoadGateRespectsStartAndConcurrencyBudget` | 5.2 场景 1 | 每帧启动 ≤2、并发 ≤2、全部完成 |
| `L52_InstalledVersionCleanerRetainsActiveAndNewest` | 5.2 场景 3 | 保留 active + 最新 1 个；staging 占用的版本不删 |
| `FullChain_RealBundlesAcrossVersionSwitch` | 5.1 场景 S + 4.3/5.2 | 见 5.3 表格，全部通过 |

### 7.3 演示冒烟

整理后只剩两个演示（交互 DemoApp / 全链路 FullChain），各自绑定一个场景，进入哪个场景就跑哪个。PlayMode 用例在 `InitTestScene` 里运行，不会加载 demo 场景，控制台不产生无关 `LogError`。

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
| 4.7 | 由全链路用例间接覆盖：激活的 `RejectedActiveHandles` / `RejectedUnsafePoint` / 成功激活与运行时重建语义，走的是 5.1 `PatchActivationService` |
| 5.1 | 全链路用例（场景 S 主路径 + 三条拒绝路径） |
| 5.2 | `AssetLoadGate` / `InstalledVersionCleaner` 独立用例；`RealBundlePayloadFactory` 由全链路用例覆盖 |

### 8.2 仍待补（明确记录，不假装已覆盖）

| 缺口 | 现状 | 补齐建议 |
|---|---|---|
| 4.5 / 4.6 的**独立**单测 | 仅集成层断言 | 补 `Lesson45AssetRuntime` 的 Scope 释放/失败路径、`AssetMemoryPolicy` 预算与诊断快照的独立用例 |
| 4.7 `PatchActivationController` 直接断言 | 只验证了 5.1 的等价语义 | 用同一份 v1/v2 构建产物构造 `PatchManifestDescriptor`，断言 `BundleMissing` / `HashMismatch` / `PlatformMismatch` 各自的结构化结果 |
| 5.2 `SafePointGate` | 未单测 | 补“有待处理请求时安全点不可用”的用例 |
| 3.5 服务器不支持 Range 的回退 | 未单测 | 设 `server.SupportsRange=false` 且已有 `.part`，断言删除 `.part` 并从 0 重下 |
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
- **看交互演示**：直接按 **Play**（默认模式）。
- **看全链路日志演示**：打开 `Assets/Scenes/Demo_FullChain.unity`，按 Play。
- **测试与演示互不干扰**：测试不加载 demo 场景，演示也不再自动启动，
  所以跑测试不会被演示日志污染（15 个 PlayMode 用例已验证）。
- **构建期工具**：`Tools/Game Asset System/Build Full-Chain Demo Content`（4.1→4.2→4.3 全流程）。
- **一键构建 + 进演示**：`Tools/Game Asset System/Build Content And Play Demo`。

## 10. 结论

- 15 个课程快照已合并为一份最终版框架（71 个文件 / 17,460 行），编译零错误；
- 新增的全链路 demo 用**真实 AssetBundle 产物**跑通“构建 → 激活 → 加载 → 回收”，补上了文档里缺失的那一段；
- 新增可点击的交互演示：真实 Bundle + 引用计数 + 版本切换 + 内存回收全部可视化，零预制体、任何场景可跑；
- 21 个用例全部通过，覆盖并发/取消/引用计数/文件事务/断点续传/调度预算/构建期校验/版本切换；
- 文档自身存在 7 类缺陷（含 2 类跨版本运行时缺陷），已修复并记录；
- 剩余缺口在 8.2 中列明，其中 4.7 与 5.2 的安全点门禁值得优先补。

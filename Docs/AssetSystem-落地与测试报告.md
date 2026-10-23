# Unity 资源管理框架：落地与测试报告

> 来源文档：`C:\Users\Halo\Desktop\Unity资源管理框架方案选型与约束分析.md`（37914 行 / 1.16 MB / 15 个课程快照）
> 目标工程：`D:\Work\Blog\HaloRes2`（Unity 6000.0.32f1，URP）
>
> 本文分两段：**1–8 是当前状态与证据**，**9–10 是过程记录**（三轮收敛、代码审查复核）。
> 想直接接入看 `README.md`；这里回答"为什么这么做、验证到什么程度、还差什么"。

---

## 1. 总览

| 项 | 现状 |
|---|---|
| 框架规模 | 111 个 `.cs`（其中 `Runtime` 78 个）；6 个 asmdef |
| 演示 | 2 个 Play 场景：`Demo_Lessons`、`Demo_FullFeature` |
| 对外入口 | `AssetSystemHost` + `AssetSystemFacade`（`Runtime/Hosting/`） |
| 真实 Bundle | `LoadFromFileAsync` + 主线程泵每帧轮询，配合 `AssetLoadGate` 分帧启动 |
| 编译 | 0 error（Unity 6000.0.32f1） |
| 测试 | **38/38 通过**：EditMode 7、PlayMode 31 |

三条原则，后两轮收敛都是围绕它们做的：

1. **Runtime 是唯一真相源** —— 业务与测试只引用 `Game.AssetSystem`，不引用 Demos。
2. **Demo 只做演示** —— 只保留分课控制台与全功能交互两个场景，不承担集成验证。
3. **对外单一入口** —— `AssetSystemHost` / `AssetSystemFacade`，不复制 Demo 编排。

## 2. 目录与程序集

```
Assets/Game/AssetSystem/
├─ Runtime/                             asmdef: Game.AssetSystem（运行时，唯一真相源）
│  ├─ Core/                             MainThreadPump（有序队列 + 预算）/ PumpBudget / MainThreadAssetTask
│  ├─ Loading/                          可取消加载任务 + 共享加载注册表
│  ├─ Scheduling/                       优先级 + 双预算调度器
│  ├─ Storage/                          文件事务 / Hash / active 指针 / 启动恢复 / PatchStagingStore
│  ├─ Patch/                            唯一清单 / IPatchTransport / Apply / PatchActivationService
│  ├─ Bundles/                          BundleRuntime：引用计数 + 依赖租约 + 快照
│  ├─ Assets/                           AssetCache：Handle / Scope / 加载任务
│  ├─ Memory/                           AssetMemoryPolicy + Evaluator：先资产后 Bundle
│  ├─ Diagnostics/                      运行时快照（只读）
│  ├─ Content/                          ActiveContentContext、BuildManifestReader
│  ├─ Release/                          加载门禁 / 安全点 / 版本清理 / 真实 Bundle 异步加载
│  ├─ Hosting/                          AssetSystemOptions / Facade / Host / ActiveContentState / 下载报告
│  ├─ Integration/                      全链路 Runner + 布局契约（测试与 Editor 用，不是场景）
│  ├─ Simulation/                       asmdef: Game.AssetSystem.Simulation（唯一一套替身）
│  └─ Demos/                            asmdef: Game.AssetSystem.Demos（场景入口，不能反向被 runtime 引用）
│     ├─ Lessons/                       分课控制台 Demo（3.1–3.6 / 4.4–4.7 / 5.1 / 5.2）
│     ├─ FullFeature/                   全功能交互 Demo（Session 只是门面的薄壳）
│     ├─ Shared/                        uGUI 构建工具 AssetDemoUiKit（纯展示，无框架语义）
│     └─ Diagnostics/                   AssetSystemHud（仅 FullFeature 显式创建）
├─ Editor/                              asmdef: Game.AssetSystem.Editor
│  ├─ Scanning/                         扫描 + 分组规则 + Key 校验
│  ├─ Manifest/                         依赖图 + Manifest 构建
│  ├─ Building/                         AssetBundle 构建 / 校验 / 平台变体 / 内容构建
│  ├─ Inspector/                        构建期诊断窗口（扫描 / 依赖图 / 构建校验）
│  └─ SampleContent/                    示例资源生成器
├─ Tests/
│  ├─ Editor/                           7 个 EditMode 用例
│  └─ PlayMode/                         31 个 PlayMode 用例 + Support/ 测试数据
└─ Lesson41Sample / Lesson42Sample / Lesson43Sample / FullFeatureSample    示例资源（非代码）
```

**程序集边界**：`Game.AssetSystem` ← `Game.AssetSystem.Simulation` ← `Game.AssetSystem.Demos`；
`Game.AssetSystem.Editor` 依赖 Runtime + Demos（不依赖 Simulation）；PlayMode 测试依赖
Runtime + Demos + Simulation，EditMode 测试额外依赖 Editor。
Demo 代码漏进 runtime、或 runtime 反向依赖替身，都是**编译错误**，不靠人盯。

**命名空间**不带课程后缀：`Game.AssetSystem.{Core, Loading, Scheduling, Storage, Patch, Content,
Bundles, Assets, Memory, Diagnostics, Release, Hosting}`；`Integration/` 目录下的类型仍用
`Game.AssetSystem.FullChain`；`Game.AssetSystem.Integration` 这个命名空间**不存在**。

## 3. Runtime 分层

| 层 | namespace | 职责 |
|---|---|---|
| Core | `Core` | 主线程泵、优先级队列、跨线程 Post、`PostTerminal` / `PostNextFrame` |
| Loading / Scheduling | `Loading`, `Scheduling` | 可取消加载、同 key 合并、优先级与双预算 |
| Storage / Patch | `Storage`, `Patch` | 磁盘布局、断点续传、apply、激活校验、路径安全 |
| Content | `Content` | 激活后 manifest → Bundle/Asset 目录；构建清单解析 |
| Bundles / Assets | `Bundles`, `Assets` | Bundle 引用与依赖租约；按 Key 加载、Scope 生命周期 |
| Memory | `Memory` | 压力等级下的批量卸载（Medium/High/Critical → 2/6/64 次每帧） |
| Diagnostics | `Diagnostics` | 运行时快照，只读 |
| Release | `Release` | 每帧启动数 / 并发数、安全点、真实 Bundle 异步加载、版本清理 |
| Hosting | `Hosting` | **对外门面**：初始化、Tick、加载、下载、激活、重建运行时 |
| Integration | `FullChain` | 全链路 Runner + 布局常量，供测试与 Editor，不是玩家 Demo |

高级场景仍可直接注入自定义 `IBundleLoader` 的 `BundleRuntime`；日常业务走门面。

### 3.1 不变量与对应断言

改动这些行为之前，先看对应用例——它们不是风格偏好，是踩过的坑。

| 不变量 | 断言位置 |
|---|---|
| 同一 Bundle 组同时只有一次加载在飞 | `L44_SharedBundleRefCountAndDependencyLeases`、`FullChain` 阶段 B |
| 卸载顺序：payload → 依赖租约；资产 → Bundle | `FullChain` 阶段 G、`FullFeature` 最终回收 |
| 激活是原子操作：平台 / 句柄 / 安全点全过才换 | `ActivationGuardTests`、`FullChain` 阶段 D/E/F |
| 同优先级 FIFO、高优先级先出 | `Pump_SamePriority_KeepsEnqueueOrder`、`Pump_HighDrainsBeforeNormal_EvenWhenPostedLater` |
| Low 预留按帧内槽位计数，不依赖队列占用 | `Pump_LowReserveIsCounted_NotCursorBased` |
| 取消与完成同序域，胜负由入队顺序决定 | `Pump_TerminalTransitions_ShareOneQueueSoPostOrderWins` |
| 单次动作超预算必须被记录，不能静默 | `Pump_ReportsSingleActionThatBlowsTheBudget` |
| 门禁：每帧启动数、并发数都不越界 | `L52_AssetLoadGateRespectsStartAndConcurrencyBudget` |
| Bundle 加载不占满单帧 | `RealBundleLoader_CompletesAcrossFrames` |
| manifest 里的文件名是数据不是路径 | `L47_ActivationRejectsBundleNameEscapingRoot`、`L35_StorageRejectsUnsafeFileNames` |

## 4. 对外接入

场景挂 `AssetSystemHost`（`Runtime/Hosting/`），Inspector 填客户端目录、首装来源与加载预算：

| 阶段 | 行为 |
|---|---|
| Awake | `MainThreadPump.EnsureExists()`，构造 `AssetSystemFacade` |
| Start | `await facade.InitializeAsync()` —— 恢复 `state/active.json`，或首装基础版本并激活、重建运行时 |
| Update | `facade.Tick()` —— 启动排队加载 + 按内存压力回收 |
| 加载 | `CreateScope` → `LoadAsync<T>(key, scope)` → Scope Dispose 释放引用 |
| 换版本 | `DownloadAndStageAsync(..., IPatchTransport)` → 安全点内 `ActivateAsync` → 自动重建运行时 |

不想挂组件就自己持有门面：

```csharp
using System.IO;
using Game.AssetSystem.Hosting;
using Game.AssetSystem.Assets;

var facade = new AssetSystemFacade(new AssetSystemOptions
{
    ClientRoot      = Path.Combine(Application.persistentDataPath, "Content"),
    BaseContentRoot = cdnRoot,              // 首装来源；已有 active.json 时可留空
    PlatformKey     = PlatformKeys.Current,
    BaseVersion     = "v1"
});

await facade.InitializeAsync();

using (var scope = facade.CreateScope("MainMenu"))
{
    var result = await facade.LoadAsync<MyConfig>("ui/shop/shoproot", scope);
    // result.Status / result.Value
}                                           // Scope Dispose 即释放引用

var report = await facade.DownloadAndStageAsync(manifest, manifestPath, transport, ct);
facade.SafePoint.Enter("MainMenu");
await facade.ActivateAsync("v2");           // 内部 PersistActiveState + RebuildRuntime
```

门面 API：`InitializeAsync` / `Tick` / `CreateScope` / `LoadAsync<T>` / `ReleaseAllScopes` /
`DownloadAndStageAsync` / `ActivateAsync` / `RebuildRuntime` / `SetMemoryPressure`；
只读 `ActiveVersion` / `Ready` / `GatePendingCount` / `Diagnostics`（`IAssetSystemReadOnly`）。

下载传输由 `IPatchTransport` 注入：生产用真实 HTTP/CDN，FullFeature Demo 用 `SimulatedCdnServer`。
`FullFeatureSession` 只剩 Demo 语义（路径 + 日志 + 模拟 CDN），内部持有同一个门面。

### 4.1 真实 Bundle 异步加载

两层节流：**门禁控制每帧启动几个**，**`LoadFromFileAsync` 控制每个多久完成**。

```
AssetCache → BundleRuntime → GatedBundleLoader（MaxStartsPerFrame / MaxConcurrent）
           → RealBundleLoader → AssetBundle.LoadFromFileAsync
                               → MainThreadPump.PostNextFrame(Poll)   // 每帧一次
                               → isDone ? RealBundlePayload : 再排下一帧
```

- 轮询用 `MainThreadPump.PostNextFrame`：它进独立队列、下一帧 `Update` 开头并入 Normal。
  在 pump 动作里直接 `Post` 会被同帧重新取到，退化成空转到动作上限。
- 取消：Unity 没有取消接口。调用方立即拿到 canceled，轮询继续到 `isDone`，再把无人认领的
  Bundle `Unload(true)`，不泄漏文件映射。
- 资产级 `LoadAsset` 仍同步——那是 Unity 常规用法，异步范围只到 Bundle 文件加载。

## 5. 构建期工具

| 菜单 | 作用 |
|---|---|
| `Tools/Game Asset System/Build Full-Chain Demo Content` | 4.1 扫描 → 4.2 依赖图 → 4.3 构建 `fullchain-v1/v2` 真实 Bundle |
| `Tools/Game Asset System/Build Full-Feature Demo Content` | 构建 `v1/v2/v3` 三套内容（`ui_shop`/`battle_fx` 依赖 `shared`） |
| `Tools/Game Asset System/Build Content And Play Demo` | 构建后自动进 Play |
| `Tools/Game Asset System/Build Full-Feature Content And Play` | 同上，全功能内容 |
| `Tools/Game Asset System/Clean …` | 清理对应产物 |
| `Inspect/1. Scan & Group` | 扫描 + 分组 + Key 校验，报告写 `GameAssetSystemBuild/Lesson41/scan-report.json` |
| `Inspect/2. Dependency Graph & Manifest` | 干净图 / 注入缺失依赖 / 注入环路 / 建 Manifest |
| `Inspect/3. Bundle Build & Validate` | 按平台构建 + 校验 + 错误平台 + 破坏 Hash |

三个 Inspect 窗口实测（均实际点过）：

| 窗口 | 输出 |
|---|---|
| Scan | `scanned=7 bindings=6 groups=3 errors=4 warnings=0`，报告已落盘 |
| Graph | `assets=5 bundles=3 edges=2 errors=0`，manifest 写入 `Lesson42/manifest-graph-v1.json` |
| Build | `assets=5 bundles=3` → `bundle-v1`，校验 `bundlesLoaded=3` |

> 三个窗口只读写 `GameAssetSystemBuild/` 与 `Lesson41/42/43Sample/`，不碰工程其它资产。
> 平台目录沿用 `GameAssetSystemBuild/Lesson43/<平台>/`，以免已有全链路产物失效。

## 6. 演示

### 6.1 两个场景

| 场景 | 受众 | 跑什么 |
|---|---|---|
| `Demo_Lessons` | 学习 / 单模块回归 | Inspector 选 `LessonDemoKind`，控制台跑 3.1–3.6 / 4.4–4.7 / 5.1 / 5.2 |
| `Demo_FullFeature` | 集成验收 / 试玩 | 三版本、断点续传、两阶段激活、共享 Bundle、内存分级回收 |

两个场景都在 Build Settings 里，都需要先有真实 Bundle 产物。全链路（FullChain）没有场景，
由 `FullChain_RealBundlesAcrossVersionSwitch` 驱动。

### 6.2 用法

1. 首次或产物缺失时先跑构建菜单（见第 5 节）；
2. 打开 `Demo_Lessons` → 选课程 → Play → 看控制台 `[Lesson]` / `[Scenario]`；
3. 打开 `Demo_FullFeature` → Play → 出现全功能面板（HUD 默认隐藏，`F1` 唤出、`F2` 清空）；
4. 其它场景（如 `SampleScene`）按 Play 不会启动任何 demo，也不会出现 HUD。

### 6.3 Lessons：课程 → 用例对照

12 个运行时控制台 demo 已合并到一个场景 + 枚举入口，行为同时被断言化用例覆盖。

| 项 | 说明 |
|---|---|
| 场景 | `Assets/Scenes/Demo_Lessons.unity`（已加入 Build Settings） |
| 入口组件 | `LessonDemoBootstrap`，挂在 `[LessonDemoBootstrap]` 物体上 |
| 选择方式 | Inspector 的 `LessonDemoKind` 枚举，或代码 `Select(kind)` + `RunSelected()` |
| 可选课程 | 3.1 / 3.2 / 3.3 / 3.4 / 3.5 / 3.6 / 4.4 / 4.5 / 4.6 / 4.7 / 5.1 / 5.2 |
| 代码位置 | `Runtime/Demos/Lessons/`（每课一个 `Lxx*Demo.cs`，公共替身在 `Support/`） |

| 课程 | 原 demo | 现在的等价入口（测试用例） |
|---|---|---|
| 3.1 主线程亲和 | `MainThreadRunnerLessonBootstrap` | `L31_WorkerCompletionIsDeliveredOnMainThread` |
| 3.2 可取消加载 | `CancellationLessonBootstrap` | `L32_OneWaiterCanceled_OtherWaiterStillSucceeds`、`L32_AllWaitersCanceled_LateSuccessIsAbandoned` |
| 3.3 共享加载 | `SharedLoadRegistryLessonBootstrap` | `L33_TwoWaitersReuseOneOperation`、`L33_LastWaiterCanceled_NextLoadCreatesNewOperation` |
| 3.4 文件事务 | `FileTransactionLessonBootstrap` | `L34_HashGuardBlocksBadCommitAndBadActivation`、`L34_RecoverOnStartupCleansLeftoversAndReportsMissingBundles` |
| 3.5 补丁下载 | `PatchDownloadLessonBootstrap` | `L35_DownloadInterruptedThenResumes`、`L35_StorageRejectsUnsafeFileNames` |
| 3.6 调度预算 | `SchedulerLessonBootstrap` | `L36_HighPriorityStartsFirst_WithinBudget`、`L36_ScopeDisposeCancelsQueuedLoads`、`L36_CancelAfterWorkerFinished_ReleasesAbandonedResult` |
| 4.1 构建期扫描 | `AssetScanLessonWindow`（编辑器窗口） | `L41_ScanReportsUnmatchedInvalidAndDuplicateKeys`、`L41_AssetKeyFormatRejectsInvalidKeys` |
| 4.2 依赖图 | `DependencyGraphLessonWindow`（编辑器窗口） | `L42_CleanGraphBuildsManifest` |
| 4.3 Bundle 构建 | `Lesson43BuildWindow`（编辑器窗口） | `L43_BuildsRealBundlesAndRejectsWrongPlatform`；产物由 `FullChain_BuildsV1AndV2Bundles` 生成 |
| 4.4 BundleRuntime | `BundleRuntimeLessonBootstrap` | `L44_SharedBundleRefCountAndDependencyLeases`、`L44_CancelOneWaiter_OtherGetsLease`、`L44_MissingDependencyFailsWithoutLease` |
| 4.5 资源缓存 | `AssetCacheLessonBootstrap` | `FullChain_RealBundlesAcrossVersionSwitch`（共享 / Idle / 释放段） |
| 4.6 内存策略 | `AssetMemoryLessonBootstrap` | 同上（`reclaimedAssets` / `reclaimedBundles` / Handle 归零） |
| 4.7 补丁激活 | `PatchActivationLessonBootstrap` | 同上 + `ActivationGuardTests`（五条拒绝路径） |
| 5.1 场景 S | `ScenarioSDemo` | `FullChain_RealBundlesAcrossVersionSwitch` |
| 5.2 发布验证 | `ReleaseValidationDemo` | `L52_*`（门禁预算、版本清理、异步加载） |

**怎么跑**：`Window > General > Test Runner` → 选 `EditMode` / `PlayMode` → 双击单条或 Run Selected。
命令行（先关编辑器，避免 `UnityLockfile` 被占用）：

```bash
"D:/Work/UnityInstall/6000.0.32f1/Editor/Unity.exe" -batchmode -projectPath "D:/Work/Blog/HaloRes2" \
  -runTests -testPlatform PlayMode -testFilter "L36_" -testResults "Logs/L36.xml" -quit
```

`-testFilter` 支持 `L31_`、`L44_`、`FullChain_` 等前缀；4.1/4.2/4.3/3.5 在 EditMode，需换 `-testPlatform`。

### 6.4 FullFeature 详情

一个场景走完「加载 → 共享 → 释放 → 下载中断 → 续传 → 激活 → 重建运行时 → 内存回收」，
按钮调用 Demo Session，Session 转调门面；面板上的每个数字都是运行时快照读回来的，没有模拟状态。

**代码位置**

| 文件 | 作用 |
|---|---|
| `Runtime/Hosting/AssetSystemFacade.cs` | **真正的客户端编排**（游戏侧同一份）：首启安装 / 下载 / 激活 / 重建 / 加载 / 释放 / 分级回收 + 每帧 Tick |
| `Runtime/Hosting/AssetSystemOptions.cs` | 客户端根目录、`content`/`staging`/`state` 子路径、平台键、基础版本、加载预算 |
| `Runtime/Demos/FullFeature/FullFeatureSession.cs` | Demo 薄壳：模拟 CDN + 中断预算 + 滚动日志；编排全部转调门面 |
| `Runtime/Demos/FullFeature/FullFeatureDashboard.cs` | uGUI 面板（零预制体，复用 `Demos/Shared/AssetDemoUiKit`） |
| `Runtime/Demos/FullFeature/FullFeatureLayout.cs` | 服务端 / 客户端路径契约 + 清单读取（转调 `BuildManifestReader`） |
| `Runtime/Demos/FullFeature/FullFeatureScenario.cs` | 同一套调用的脚本化版本，PlayMode 用例直接跑它 |
| `Runtime/Demos/FullFeature/{SharedInfo,AssetInfo,FullFeatureContent}.cs` | 示例内容类型与 Key / Bundle 组名 |
| `Editor/Building/FullFeatureContentBuilder.cs` | 生成 v1/v2/v3 并构建 3 组 Bundle |

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
| 右下「Bundle / 资产状态」 | 每个 Bundle 的 State / refs / 是否持有 payload、Idle 资产列表、暂存与状态目录 |

**实测读数（真实点过一遍）**

| 步骤 | 读数 |
|---|---|
| 首启 | `恢复/安装 v1` → `[Activation] <none> -> v1` → 面板 Active v1，Bundles 全 NotLoaded |
| 加载 `ui/panel (A)` | `Assets R1/I0/L0/H1`、`Bundles R2/I0/L2/Ref2`、`shared` 与 `ui_shop` 均 Ready 且持有 payload、色块 `#E65252`（v1） |
| 70% 中断 | 进度条停在 ~70%，`Failed · 提交 2 · 跳过 0 · 续传 0`，`暂存 v2 1 个 .part（194 字节）· 1 个 .meta` |
| 完整下载 | `提交 1 · 跳过 2 · 续传 194 字节`，暂存目录清空 |
| 有 Handle 时激活 | `RejectedActiveHandles Cannot activate while 1 live handle(s) exist.` |
| 安全点 OFF 激活 | `RejectedUnsafePoint` |
| 安全点 ON 激活 | `Succeeded Activated v2 (3 bundles, 3 assets)`，运行时重建，计数归零 |
| 切版后加载 | `v2/UI/Panel`、`shared.version v2`、色块 `#4CCC7A`（绿） |

**框架侧为它补的能力**（都是可选参数，不改变既有调用行为）

| 能力 | 位置 |
|---|---|
| `RuntimeBundle.DirectDependencies` + `PatchBundleDescriptor.Dependencies` | `Content/ActiveContentContext.cs`、`Patch/PatchTypes.cs` |
| 激活时解析依赖组名、拒绝未知依赖 / 自依赖 / 环 | `Patch/PatchActivationService.cs` |
| Bundle 先加载依赖、卸载时「先 payload 后依赖租约」 | `Bundles/BundleRuntime.cs` |
| `SnapshotBundles()` / `TotalUserRefs` 供面板只读展示 | `Bundles/BundleRuntime.cs` |
| 构建清单携带 `dependencies`、`Build()` 可指定输出根 | `Editor/Building/{BuildTypes,AssetBundleBuilder}.cs` |

### 6.5 已删除的演示

| 删除项 | 原因 |
|---|---|
| `Demo_Interactive.unity` + `Runtime/Demos/Interactive/` | 与 FullFeature 职责重叠（双 Owner / 共享去重 / 版本切换），还多一套 3D 视口，信息增量抵不上维护成本 |
| `Demo_FullChain.unity` + `FullChainDemoBootstrap` / `FullChainScenarioDemo` | 全链路是断言问题不是演示问题；Runner 移入 `Runtime/Integration/` 后由用例驱动 |
| `AssetSystemHud` 的全局 `AfterSceneLoad` 自启 | 任何场景进 Play 都被面板盖住，Lessons 的控制台演示尤其受影响 |

`AssetDemoUiKit`（零预制体 uGUI 构建工具）没有跟着删：纯展示代码、不含框架语义，
移到 `Demos/Shared/` 后继续被 FullFeature 面板使用。

## 7. 测试与结果

### 7.1 EditMode（7/7）

| 用例 | 对应文档 | 断言要点 |
|---|---|---|
| `L41_ScanReportsUnmatchedInvalidAndDuplicateKeys` | 4.1 §5.3 | scanned=7 / bindings=6 / groups=3 / errors=4，含三类错误码，`HasFatalErrors` |
| `L41_AssetKeyFormatRejectsInvalidKeys` | 4.1 命名规则 | 合法 key 通过；空格、空串被拒 |
| `L42_CleanGraphBuildsManifest` | 4.2 §5.2 | 5 assets / 3 bundles / 2 edges / 0 errors |
| `L43_BuildsRealBundlesAndRejectsWrongPlatform` | 4.3 §5.2 §5.3 | 真实构建 3 Bundle，`LoadAsset` 校验 5/5；错误平台必须失败 |
| `L35_StorageRejectsUnsafeFileNames` | 3.5 文件名安全 | 拒绝 `../`、路径分隔符、空名 |
| `FullChain_BuildsV1AndV2Bundles` | 全链路前置 | v1/v2 清单与 Bundle 存在且不同 |
| `FullFeature_BuildsThreeVersionsWithSharedDependency` | 6.4 前置 | v1/v2/v3 各 3 Bundle / 3 资产；`ui_shop`、`battle_fx` 的 `dependencies` 恰为 `shared` |

### 7.2 PlayMode（31/31）

| 用例 | 对应文档 | 断言要点 |
|---|---|---|
| `L31_WorkerCompletionIsDeliveredOnMainThread` | 3.1 | 完成回调线程 == 主线程 |
| `L32_OneWaiterCanceled_OtherWaiterStillSucceeds` | 3.2 场景 1 | 取消不影响他人；只 Begin 一次、只创建一次 payload |
| `L32_AllWaitersCanceled_LateSuccessIsAbandoned` | 3.2 场景 2 | 迟到成功恰好释放 1 次 |
| `L33_TwoWaitersReuseOneOperation` | 3.3 场景 1 | created=1 / reused=1 / backend Begin=1，完成后 detach |
| `L33_LastWaiterCanceled_NextLoadCreatesNewOperation` | 3.3 场景 2 | 取消即 detach，新请求 created=2 |
| `L34_HashGuardBlocksBadCommitAndBadActivation` | 3.4 | 错 Hash 不提交；缺 Bundle 不激活；active 指针不变 |
| `L34_RecoverOnStartupCleansLeftoversAndReportsMissingBundles` | 3.4 启动恢复 | 清 temp 残留与 `active.json.tmp`；从 `.bak` 恢复；统计缺失 Bundle |
| `L35_DownloadInterruptedThenResumes` | 3.5 | 中断保留 `.part`；重建控制器后按 offset 续传并激活 v2 |
| `L36_HighPriorityStartsFirst_WithinBudget` | 3.6 场景 1 | high 先启动；每帧 ≤2；并发 ≤2 |
| `L36_ScopeDisposeCancelsQueuedLoads` | 3.6 场景 2 | 排队任务全部取消且不进 worker |
| `L36_CancelAfterWorkerFinished_ReleasesAbandonedResult` | 3.6 取消竞态 | worker 已产出后取消：任务记 Canceled，结果走 `releaseAbandoned` 恰好释放 1 次 |
| `Pump_SamePriority_KeepsEnqueueOrder` | 3.1 顺序契约 | 同优先级严格 FIFO |
| `Pump_HighDrainsBeforeNormal_EvenWhenPostedLater` | 3.1 | 高优先级先出，即使后入队 |
| `Pump_LowReserveIsCounted_NotCursorBased` | 3.1 | Low 预留按帧内槽位计数，不依赖队列占用 |
| `Pump_TerminalTransitions_ShareOneQueueSoPostOrderWins` | 3.1 | 终态同队列，入队顺序决定胜负 |
| `Pump_ReportsSingleActionThatBlowsTheBudget` | 3.1 | 单动作超预算被记录 |
| `Pump_DifferentQueues_LosePostOrder_WhichIsWhyPostTerminalExists` | 3.1 | 反例：不同队列会丢入队顺序，所以需要 `PostTerminal` |
| `L44_SharedBundleRefCountAndDependencyLeases` | 4.4 场景 1 | 只加载一次；Idle 保留 payload；依赖租约随父卸载释放 |
| `L44_CancelOneWaiter_OtherGetsLease` | 4.4 场景 2 | 取消者无 lease；Bundle 只启动一次 |
| `L44_MissingDependencyFailsWithoutLease` | 4.4 场景 3 | `DependencyMissing` 结构化失败，无悬挂引用 |
| `L47_ActivationRejectsBundleNameEscapingRoot` | 4.7 文件名安全 | `../escape.bundle` → `InvalidManifest`，ActiveContext 不变 |
| `L47_ActivationRejectsPlatformMismatch` | 4.7 §5.3 | `PlatformMismatch`，不激活 |
| `L47_ActivationReportsMissingBundle` | 4.7 §5.3 | `BundleMissing`，不激活 |
| `L47_ActivationReportsHashMismatch` | 4.7 §5.3 | 尺寸对、Hash 错 → `HashMismatch`，不激活 |
| `L47_ActivationRechecksHandlesInsideSwap` | 4.7 检查与 Swap 的窗口 | 预检读到 0、Swap 内读到 1 → `RejectedActiveHandles`，`ActivationCount==0` |
| `L52_AssetLoadGateRespectsStartAndConcurrencyBudget` | 5.2 场景 1 | 每帧启动 ≤2、并发 ≤2、全部完成 |
| `L52_InstalledVersionCleanerRetainsActiveAndNewest` | 5.2 场景 3 | 保留 active + 最新 1 个；有 `.part` 的 staging 不删，空目录不占位 |
| `L52_InstalledVersionCleanerOrdersVersionsNumerically` | 5.2 版本排序 | v8 激活时保留 v10、删 v9（ordinal 会反） |
| `RealBundleLoader_CompletesAcrossFrames` | 5.2 异步加载 | 起始帧不可能完成；至少跨 1 帧才拿到 payload |
| `FullChain_RealBundlesAcrossVersionSwitch` | 5.1 场景 S + 4.3/5.2 | 见 7.3 阶段表，`violations=0` |
| `FullFeature_ShareInterruptResumeActivateReclaim` | 6.4 全链路 | 首启 v1；首次 2 payload，battle_fx 复用 shared；释放后 2 Idle / 3 payload；Critical 归零；70% 中断留 `.part`、续传 >0；两类拒绝；v2 `shared.version=v2`；最终 payload 归零 |

### 7.3 全链路（真实产物，不是模拟）

文档原本的 5.1 场景 S 用模拟 backend 跑；全链路把 **4.3 的真实构建产物**接进去，
补上了文档里断掉的"构建 → 产物 → 被运行时消费"一段：

```
4.1 扫描/分组 → 4.2 依赖图 + Manifest → 4.3 真实 AssetBundle 构建
        ↓（产物落盘）
RealBundleLoader（LoadFromFileAsync）→ PatchActivationService → ActiveContentContext
        ↓
BundleRuntime（引用计数）→ AssetCache / Scope / Handle
        ↓
AssetMemoryEvaluator（先 Asset 后 Bundle）
```

内容由 `Build Full-Chain Demo Content` 生成；缺失时用例 `Assert.Ignore` 并提示先构建，不误报失败。
没有独立场景：Runner 在 `Runtime/Integration/`，只由用例驱动。

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

### 7.4 覆盖矩阵

| 课程 | 覆盖方式 |
|---|---|
| 3.1 / 3.2 / 3.3 | 独立 PlayMode 用例 |
| 3.4 / 3.5 | 独立 PlayMode 用例（真实文件 IO） |
| 3.6 | 独立 PlayMode 用例（优先级 + 双预算 + Scope 取消） |
| 4.1 / 4.2 / 4.3 | 独立 EditMode 用例（含真实 Bundle 构建与 `LoadAsset` 校验） |
| 4.4 | 独立 PlayMode 用例（引用计数 / 依赖租约 / 缺依赖） |
| 4.5 / 4.6 | 全链路用例：共享与释放、`AssetMemoryEvaluator` 回收（Asset 先于 Bundle）、Handle 归零 |
| 4.7 | `ActivationGuardTests` 五条拒绝路径；主路径由全链路用例覆盖 |
| 5.1 | 全链路用例（场景 S 主路径 + 三条拒绝路径） |
| 5.2 | 门禁预算、版本清理、异步加载各有独立用例 |

### 7.5 仍待补（明确记录，不假装已覆盖）

| 缺口 | 现状 | 补齐建议 |
|---|---|---|
| 4.5 / 4.6 的**独立**单测 | 仅集成层断言 | 补 Scope 释放/失败路径、`AssetMemoryPolicy` 预算与诊断快照的独立用例 |
| `SafePointGate` | 未单测 | 补"有待处理请求时安全点不可用"的用例 |
| 3.5 服务器不支持 Range 的回退 | 未单测 | 设 `SupportsRange=false` 且已有 `.part`，断言删 `.part` 从 0 重下 |
| 4.2 环依赖 / 4.3 Hash 损坏 | 只有主路径 | 构造环依赖图与损坏 Bundle，断言结构化错误码 |
| PlayMode 缺内容时 `Assert.Ignore` | 未构建时是"假绿" | CI 先跑构建菜单再跑 PlayMode（本仓库已提交产物规避） |
| 构建期 CI 脚本 | 仅静态落地 | 需要真实 Unity 命令行环境才能验证 |

## 8. 运行与排错

### 8.1 为什么场景里看不到东西

框架是**纯逻辑库**，没有任何可渲染内容：物体都在 `DontDestroyOnLoad` 分区，场景视图看不到，
所有证据都是 Console 日志。仓库里的 `SampleScene` 只有默认的相机 / 灯光 / Volume，
打开它是空的属于正常现象。

### 8.2 屏上 HUD

`Runtime/Demos/Diagnostics/AssetSystemHud.cs` 不再自启，只由 `FullFeatureDashboard` 通过
`AssetSystemHud.EnsureHud()` 创建，启动时 `SetVisible(false)`，`F1` 唤出：

- `F1` 隐藏/显示，`F2` 清空；
- 面板顶部显示当前场景与 FPS；
- 只抓 `[Lesson*]` / `[FullChain*]` / `[FullFeature*]` / `[AssetSystem*]` / `[Scenario*]` / `[Registry*]`
  前缀和 Error/Warning，避免刷屏；
- Lessons 场景不再被覆盖：它的输出只在 Console。

### 8.3 命令

| 目的 | 操作 |
|---|---|
| 跑测试 | `Window > General > Test Runner` → EditMode / PlayMode → Run All |
| 看分课演示 | 打开 `Demo_Lessons`，选课程，Play |
| 看全链路 | 跑 `FullChain_RealBundlesAcrossVersionSwitch`（无独立场景） |
| 看全功能演示 | 先跑构建菜单，再打开 `Demo_FullFeature` Play |
| 构建期诊断 | `Tools/Game Asset System/Inspect/1·2·3` |

测试不加载 demo 场景、演示也不再自动启动，两者互不污染日志。

### 8.4 排错

- **`referenced script is missing`**：Bundle 记录的是程序集名，改过 asmdef 或挪过类型后必须重跑两个
  `Build … Demo Content` 菜单。
- **激活一直 `RejectedActiveHandles`**：还有活动 Handle 或在飞加载，先 `ReleaseAllScopes()`，
  确认 `TotalLiveHandles + LoadingAssetCount == 0`。
- **激活一直 `RejectedUnsafePoint`**：`SafePointGate` 没进入，先 `facade.SafePoint.Enter("MainMenu")`。
- **Bundle 加载一直不完成**：`MainThreadPump` 没跑起来（它由 `[RuntimeInitializeOnLoadMethod]` 创建，
  `AssetSystemHost.Awake` 也会 `EnsureExists()`）；纯 EditMode 里手动构造 loader 没有帧循环。

## 9. 过程记录

### 9.1 第一轮：15 份课程快照 → 一份

原工程把文档的 15 个课程快照各自完整落地，同名类型大量重复（`BundleRuntime` ×3、`AssetCache` ×3、
`UnityMainThreadPump` ×5、`AssetScanner` ×2），只靠 `.L31` / `.L46` 这样的命名空间后缀隔离。

| 处理 | 内容 |
|---|---|
| 每层只留最新实现 | Loading 取 3.3（`AcquireWaiter` 已被 `TryAcquireWaiter` 取代）、Storage 取 3.5、Scanning 取 4.2、Bundles/Assets 取 4.6 |
| 主线程调度器合并 | 3 个 `UnityMainThreadRunner` + 5 个 `UnityMainThreadPump` + `IntegrationMainThreadPump` → 1 个 `Core/MainThreadPump` |
| 课程 demo 收敛 | 12 个逐课 bootstrap → 一个场景 + 枚举入口；3 个编辑器窗口 → `Editor/Inspector` |
| 类型名去课程号 | `Lesson43AssetBundleBuilder` → `AssetBundleBuilder`、`LessonHud` → `AssetSystemHud`；`LessonBootGate` 删除 |
| 测试保留并改到新 API | 21 个用例全保留；3.2 的两条改到 `TryAcquireWaiter`；替身集中到 `Tests/PlayMode/Support` |

**文档代码本身的缺陷（已修）**

| # | 问题 | 影响文件 | 修复 |
|---|---|---|---|
| 1 | 用了 `TaskAwaiter<T>` 但缺 `using System.Runtime.CompilerServices;` | 10 个（L31/L32/L33/L36/L44/L45/L46/L51） | 补 using |
| 2 | `AssetLoadResult<T>` 缺 `where T : class`，无法作为 `AssetHandle<T>` 的类型参数 | L45、L46 | 补约束 |
| 3 | `readonly struct DownloadAttempt` 含可变字段（CS8340） | L51 `SimulatedCdn.cs` | 去掉 `readonly` |
| 4 | `AssetSystem.Storage.AssetFileStore` 相对命名空间在隔离后失效 | L35 `PatchStagingStore.cs` | 改全限定名 |
| 5 | 缺 using：`IntegrationMainThreadPump`、`Lesson43GraphNode` | L52、L43 | 补 using |
| 6 | 校验器 `Unload(false)` 泄漏 Bundle，污染同一 Editor 会话后续加载 | L43 `Lesson43BundleValidator` | 改 `Unload(true)` |
| 7 | v1/v2 Bundle 含相同资源时，切换版本触发 "another AssetBundle with the same files" | 全链路运行器 | 激活后 `UnloadAllAssetBundles(true)`（对应"激活 = 重建运行时"） |

### 9.2 第二轮：去重与不变量收敛

第一轮合并了目录，但**同一份能力仍在不同目录各有一份实现**。第二轮按"先定义不变量、再决定留哪份"收敛。

**收敛前的重复清单**

| 重复物 | 位置 A | 位置 B | 差异 |
|---|---|---|---|
| BundleRuntime | `Bundles/BundleRuntime.cs` | `Integration/ContentBundleRuntime.cs` | A 有环检测/策略/诊断，B 有计数器 |
| AssetCache | `Assets/AssetRuntime.cs` | `Integration/AssetCache.cs` | A 走 catalog+策略，B 走 ActiveContentContext |
| PatchStagingStore | `Patch/` | `Integration/Patch/` | 根目录、meta 字段、路径校验器都不同 |
| PatchManifest | `Patch/PatchTypes.cs` | `Integration/Patch/PatchTypes.cs` | 小写 + `url` vs Pascal + `PlatformKey/Assets/Dependencies` |
| 值类型 | `Integration/Types.cs` | `Assets/` `Bundles/` | `AssetKey` / `BundleId` / `AssetLoadResult` 各两份 |
| 传输抽象 | `Patch/IPatchTransport.cs` | `Integration/Patch/IContentTransport` | 后者把 `SimulatedCdnServer` 写进了接口签名 |
| 激活 | `AssetFileStore.ActivateVersion` | `PatchActivationService` | 两条路径，一条只换指针 |
| 优先级策略 | `MainThreadPump` 三档轮转 | `AssetLoadScheduler` 连续配额 | 同一规则写两遍，规则不同 |
| 模拟后端 | Lessons / Tests / Integration 各一份 | — | 3 份 CDN + 2 份 BundleLoader，几乎逐字相同 |

**处理结果**

| 动作 | 内容 |
|---|---|
| 保留 | `Bundles` + `Assets` 作为唯一运行时；`Integration` **命名空间**整体删除 |
| 降级 | `ActiveContentContext` 只描述内容，通过 `CreateBundleCatalog()` / `CreateAssetCatalog()` 投影 |
| 合并 | Patch 清单/结果/进度合成一套（Pascal + `Url` + 依赖）；staging 移到 `Storage/`，schema 升到 2 |
| 统一 | 传输只留 `IPatchTransport`；模拟 CDN 只留 `Simulation/SimulatedPatchTransport` |
| 统一 | 激活只留 `PatchActivationService`；`PatchApplyController` 增加 `activateAfterApply` |
| 新增 | `PumpBudget`；`PostTerminal` 统一终态序域；`Simulation` 程序集（延迟/忽略取消/失败点/Range/预算） |

> 第三轮又建了 `Runtime/Integration/` **目录**，但那里放的是全链路 Runner，命名空间仍是
> `Game.AssetSystem.FullChain`。目录名与第二轮删掉的命名空间不是一回事。

**行为变化（有意为之）**

- **Pump 不再是轮转**：优先级从"占 1/3 槽位"变成"严格先出"，低优先级靠 `LowReserveEvery` 防饿死。
- **终态统一 High**：完成 / 取消 / 释放走同一队列，取消不再可能被完成"插队"。
- **完成回调经 pump**：`RunLoadAsync` 的 finalize 不再跑在同步上下文续体上；代价是完成晚一帧生效。
- **激活默认不再由 apply 管线换指针**：`activateAfterApply: false` 的调用方必须自己调激活服务。
- **程序集搬家**：`DemoGraphNode` 等类型先移入 `Game.AssetSystem.Demos`（第二轮），第三轮又移回
  `Game.AssetSystem`。Bundle 记录程序集名，**每次搬家都要重跑构建菜单**。

### 9.3 第三轮：Demo 收敛 + 对外门面 + 异步加载

前两轮解决"同一能力有几份实现"，这一轮解决"同一能力只有 Demo 会用"：全功能 Demo 里的客户端编排
是唯一一份完整流程，其它系统想接热更内容只能抄它。

| 动作 | 内容 |
|---|---|
| 删除 | Interactive 场景与目录、FullChain 场景与两个 MonoBehaviour 入口、HUD 全局自启（见 6.5） |
| 迁移 | `FullChainRunner` / `FullChainLayout` / `DemoGraphNode` → `Runtime/Integration/`；`AssetDemoUiKit` → `Demos/Shared/` |
| 新增 | `Runtime/Hosting/`：Options / Facade / Host / ActiveContentState / PatchDownloadReport / IAssetSystemReadOnly / PlatformKeys |
| 收薄 | `FullFeatureSession` 只剩模拟 CDN、中断预算、日志；`FullFeatureLayout` 只剩路径契约 |
| 合并 | 两份清单解析 → `Content.BuildManifestReader`（原来只有一份带 `dependencies`） |
| 异步 | `RealBundleLoader` 改 `LoadFromFileAsync`；`MainThreadPump` 增加 `EnsureExists` / `PostNextFrame` |

验证：编译 0 error；EditMode 7/7；PlayMode 31/31（新增 `RealBundleLoader_CompletesAcrossFrames`）；
两套内容产物因程序集变更已重跑；FullChain `[A]…[G]` 全过、`violations=0`；FullFeature 全流程读数正常。

## 10. 代码审查复核

外部审查是按 15 份课程快照写的（`L44/Bundles/BundleRuntime.cs` 之类）。合并成一份之后，
**每个问题只剩一处副本**，"只改最新一课不会改掉前面的副本"这条前提已经消失。

### 10.1 高优先级

| # | 当前位置 | 是否成立 | 处理 |
|---|---|---|---|
| 1 | `Bundles/BundleRuntime.cs` `TryUnloadIdle` | 成立 | **已修**：先 `payload.Release()` 再释放依赖租约，与资产层"先资产后 Bundle"一致 |
| 2 | `Scheduling/AssetLoadScheduler.cs` `OnWorkerCompleted` | 成立 | **已修**：新增 `releaseAbandoned` 钩子，取消时把 worker 已产出的结果交还调用方释放 |
| 3 | 同文件 `Tick()` | 成立 | **已修**：预算绑 `Time.frameCount`，同帧多次 `Tick()` 只消耗剩余配额 |
| 4 | `Storage/AssetFileStore.cs` `CommitBundleFromTemp` | 成立 | **已修**：Hash 失败删目标并从 `.bak` 回滚；`.bak` 保留到校验通过；`active.json.bak` 恢复分支真正可达 |
| 5 | 原 `Integration/Patch/*`（现 `Patch/*`） | 成立 | **已修**：新增 `Storage/PathSafety`，`GetFullPath` 后要求仍在 root 之下；接入激活、下载、staging 三处 |
| 6 | `PatchActivationService.ActivateAsync/Swap` | 成立 | **已修**：`Swap` 内用 `liveHandlesProvider` / `safePointProvider` 复核，窗口内的新 Handle 会挡住激活 |
| 7 | `Demos/Interactive/AssetDemoController.cs` `UpdateCubes` | 部分成立 | **已修，文件已随第三轮删除**：当时改为 `CreateCube` 建实例、缓存 `sharedMaterial` |
| 8 | 同文件 `Toggle` / `SwitchVersion` | 成立 | **已修，文件已删除**：当时改为切换期拒绝新加载 + 代际比对丢弃过期回调 |

### 10.2 中优先级

| 结论 | 处理 |
|---|---|
| 取消的排队任务占扫描预算 | **已修**：已取消/已完成的条目不再消耗 `MaxPendingScanPerFrame`，单帧最多扫一趟 |
| L45 的 `AssetCacheRegistry` 未接线 | **不适用**：合并后没有该类型；`AssetCache` 自持 entry 表，`CancelWaiter` 在 entry 缺失时直接 `CompleteCanceled` |
| CancellationToken 只在启动瞬间看一眼 | **部分修**：`BundleLease.Dispose` 在后台线程改为 Post 到主线程；token 侧取消注册未做（现有调用都走显式 `Cancel()`） |
| 同一 `.part` 没有串行化 | **部分修**：提交前先删同名目标，消除 Windows `File.Move` 抛错与半套版本；同一 `.part` 并发写仍未加锁 |
| 真实 Bundle 回收卸不干净 | **未修**：`RealBundlePayload.Release` 仍是 `Unload(false)`，`LoadFromFileAsync` 也不按绝对路径去重。改 `Unload(true)` 属于语义变更，需单独评估 |
| 版本清理按字符串排序 | **已修**：按尾部数字比较（v10 > v9）；staging 只有还留着文件才算占用 |
| 构建失败会毁掉上一份成功产物 | **已修**：先写 `.building-<guid>/<版本>/`，清单写完再整体换入，失败回滚 `.previous` |
| 全链路会弄脏编辑器会话 | **部分修**：构建前校验平台键一致、v2 标记 `try/finally` 恢复；`FullChainRunner` 失败路径仍不卸载已加载 Bundle（测试代码，登记为后续项） |

### 10.3 复核时的验证（第二轮之后）

| 项 | 结果 |
|---|---|
| 编译 | 0 error（Unity 6000.0.32f1） |
| EditMode | 6/6 通过（含真实 Bundle 构建，验证暂存换入后产物结构不变） |
| PlayMode | 23/23 通过（新增 7 条：取消释放、4.7 四类拒绝、Swap 复核、版本数字排序） |
| 构建产物 | `GameAssetSystemBuild/Lesson43/StandaloneWindows64/` 下无 `.building` / `.previous` 残留 |

> 这里是当时的数字，不是当前数字；当前结果见第 7 节（EditMode 7 / PlayMode 31）。

## 11. 结论

- 15 份课程快照已合并为一份框架（111 个 `.cs`，Runtime 78 个），编译零错误；
- 同一能力只剩一处实现：一套 `BundleRuntime` + `AssetCache`、一套 Patch 清单与 staging、一条激活决策；
- 业务接入从"抄 Demo"变成 `AssetSystemHost` + `AssetSystemFacade` 两个类型；
- 真实 Bundle 走 `LoadFromFileAsync`，单帧不再有同步 `LoadFromFile` 尖刺；
- 38 个用例覆盖并发/取消/引用计数/文件事务/断点续传/调度预算/构建期校验/版本切换/激活拒绝/异步加载；
- 已知缺口在 7.5 列明：4.5/4.6 的独立单测、`SafePointGate`、Range 回退、环依赖与损坏 Bundle 的失败用例。

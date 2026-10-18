# Unity 资源管理框架：文档落地与测试报告

> 来源文档：`C:\Users\Halo\Desktop\Unity资源管理框架方案选型与约束分析.md`（37914 行 / 1.16 MB / 15 个课程快照）
> 目标工程：`D:\Work\Blog\HaloRes2`（Unity 6000.0.32f1，URP）

---

## 1. 落地结果总览

| 项目 | 数量 | 说明 |
|---|---|---|
| 从文档提取的代码段 | 105 | 88 个唯一路径 + 17 个跨课程重复版本 |
| 写入工程的课程文件 | 104 | 跳过 `Packages/manifest.json`、`ProjectSettings/ProjectVersion.txt`（工程已有，且版本为 Unity 6，不能覆盖） |
| CI 脚本 | 1 | `ci/build-and-publish.sh` |
| 汇编定义 asmdef | 7 | 1 运行时 + 4 编辑器 + 2 测试 |
| 新增全链路代码 | 3 | 布局契约、运行器、内容构建器 |
| 新增交互演示代码 | 2 | 控制器（状态机 + UI + 3D 视口）、uGUI 构建工具 |
| 测试用例 | 21 | EditMode 6、PlayMode 15 |

编译结果：**0 error**。

## 2. 目录结构

```
Assets/Game/Lessons/
├─ Game.AssetSystem.Lessons.asmdef      # 运行时程序集（覆盖全部课程代码）
├─ LessonBootGate.cs                    # 演示统一启动开关
├─ Diagnostics/LessonHud.cs             # 屏上 HUD（课程日志镜像，F1/F2）
├─ L31/  (3.1 主线程亲和性)              3 个文件
├─ L32/  (3.2 可取消加载任务)            7 个文件
├─ L33/  (3.3 共享加载注册表)            8 个文件
├─ L34/  (3.4 文件事务与 Hash)           4 个文件
├─ L35/  (3.5 Patch staging)            9 个文件
├─ L36/  (3.6 调度预算)                  7 个文件
├─ L41/  (4.1 构建期扫描)   Editor/       5 个文件 + asmdef
├─ L42/  (4.2 依赖图与 Manifest) Editor/  10 个文件 + asmdef
├─ L43/  (4.3 AssetBundle 构建)  Editor/   8 个文件 + asmdef
├─ L44/  (4.4 BundleRuntime)            5 个文件
├─ L45/  (4.5 AssetCache/Scope)         5 个文件
├─ L46/  (4.6 内存策略与诊断)            7 个文件
├─ L47/  (4.7 Patch 激活)               5 个文件
├─ L51/  (5.1 系统集成 场景 S)          13 个文件
├─ L52/  (5.2 发布前验证)                7 个文件
├─ FullChain/                           全链路 demo（跨课程串联）
│  ├─ Demo/FullChainLayout.cs           产物路径与平台键契约
│  ├─ Demo/FullChainRunner.cs           端到端运行器 + 报告
│  └─ Editor/FullChainContentBuilder.cs v1/v2 真实 Bundle 构建（+ asmdef）
├─ DemoApp/                             交互演示（运行时构建，无预制体）
│  ├─ AssetDemoController.cs            状态机 + 全部交互 + 3D 视口
│  └─ AssetDemoUiKit.cs                 uGUI 构建工具（圆角/字体/按钮）
└─ Tests/
   ├─ Editor/    LessonBuildPipelineTests.cs / FullChainContentTests.cs
   └─ PlayMode/  RuntimeScenarioTests.cs / PersistenceScenarioTests.cs
                 / ReleaseGateTests.cs / FullChainScenarioTests.cs
```

## 3. 为什么必须做隔离

15 个课程快照**不是增量补丁，而是各自完整的工程**，同名同命名空间的类型大量重复：

- `Game.AssetSystem.Bundles.BundleRuntime`：4.4 / 4.5 / 4.6 各一份
- `Game.AssetSystem.Assets.AssetCache`：4.5 / 4.6 各一份
- `Game.AssetSystem.Infrastructure.UnityMainThreadPump`：3.6 / 4.4 / 4.5 / 4.6 / 4.7 共 5 份
- `Game.AssetSystem.Editor.Scanning.AssetScanner`：4.1 / 4.2 各一份

处理方式：**按课程给命名空间加后缀**（`Game.AssetSystem.Loading` → `Game.AssetSystem.Loading.L32`），跨课程依赖自动解析到"文档顺序上最近的前置课程"（4.3→4.2、4.7→4.6、5.2→5.1）。源码其余部分保持原样。

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

`Assets/Game/Lessons/DemoApp/` 提供一套**零预制体、全运行时构建**的交互界面：Canvas、字体、圆角贴图、按钮、RenderTexture 视口都在代码里生成，因此在任何场景按 Play 都能跑。

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

### 6.4 进入方式

- 直接按 **Play**（默认就是交互演示；12 个课程日志演示会自动静默）；
- 想看原来的课程日志演示：`Tools/Game Asset System/Toggle Lesson Demo Mode`；
- 还没构建资源时：`Tools/Game Asset System/Build Content And Play Demo`（构建 v1/v2 后自动进 Play）。



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

12 个课程演示在 Play 模式下同时启动并输出预期日志。控制台仅 1 条 `LogError`，是 4.4「缺失依赖」失败路径的设计内输出。

## 8. 覆盖矩阵与剩余缺口

### 8.1 已自动化

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
- 12 个课程演示在进入 Play 时由 `[RuntimeInitializeOnLoadMethod]` 创建 `[...]` 物体，挂在 DontDestroyOnLoad 分区，场景视图里看不到；
- 所有证据都是 Console 日志。

因此「打开工程 → 空场景」是正常现象，不是资源丢失。

### 9.2 屏上 HUD

`Diagnostics/LessonHud.cs` 在 Play 时自动创建，左上角实时镜像课程日志：

- `F1` 隐藏/显示，`F2` 清空；
- 面板顶部显示当前场景、AutoBoot 状态、FPS；
- 只抓取 `[Lesson*]` / `[FullChain*]` / `[Scenario*]` / `[Registry*]` 前缀和 Error/Warning，避免刷屏；
- HUD 不受 `LessonBootGate` 控制（关掉演示也能看），但在 PlayMode 测试场景里同样静默。

### 9.3 命令

- **跑测试**：`Window > General > Test Runner` → EditMode / PlayMode → Run All。
- **看交互演示**：直接按 **Play**（默认模式）。
- **看课程日志演示**：`Tools/Game Asset System/Toggle Lesson Demo Mode` 切到日志模式，再按 Play。
- **测试与演示互不干扰**：`LessonBootGate` 检测到 `InitTestScene` 时自动让所有演示静默，
  所以跑测试不会被演示日志污染（15 个 PlayMode 用例已验证）。
- **构建期课程**：`Tools/Game Asset System/Lesson 4.1 / 4.2 / 4.3` 三个窗口。
- **一键构建 + 进演示**：`Tools/Game Asset System/Build Content And Play Demo`。

## 10. 结论

- 文档全部代码已进入工程，编译零错误，15 个快照共存且互不冲突；
- 新增的全链路 demo 用**真实 AssetBundle 产物**跑通“构建 → 激活 → 加载 → 回收”，补上了文档里缺失的那一段；
- 新增可点击的交互演示：真实 Bundle + 引用计数 + 版本切换 + 内存回收全部可视化，零预制体、任何场景可跑；
- 21 个用例全部通过，覆盖并发/取消/引用计数/文件事务/断点续传/调度预算/构建期校验/版本切换；
- 文档自身存在 7 类缺陷（含 2 类跨版本运行时缺陷），已修复并记录；
- 剩余缺口在 8.2 中列明，其中 4.7 与 5.2 的安全点门禁值得优先补。

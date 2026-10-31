# 资源管理框架验收记录

日期：2026-10-31。项目：HaloRes2。Unity：6000.0.32f1。
分支：`codex/clear-framework`。实现改动尚未提交。

## 需求覆盖

| 需求 | 实现 | 验证依据 |
| --- | --- | --- |
| R1 按地址加载资源、Prefab 和场景 | `AssetScope`、`AssetManager`、只读 `AssetManifest` | 真实 Prefab 加载、场景加载与卸载、独立 Player 自动验收 |
| R2 依赖管理与安全卸载 | `BundleManager`、扁平依赖闭包、资源句柄持有的包引用 | 共享材质、循环与菱形依赖图测试、场景卸载顺序 |
| R3 拥有者生命周期 | 幂等 `AssetLease`、`AssetScope`、子作用域、实例转移 | 作用域释放、实例转移、延迟 `Destroy` 和 `Awake` 重入测试 |
| R4 请求合并与独立取消 | `SharedCompletion`、等待前同步 `Acquire` | 重复请求、单个等待者取消、资源与场景迟到完成、同帧复活 |
| R5 编辑器模拟模式 | 位于同一套 `AssetManager` 下方的 `EditorAssetProvider` | 编辑器 Provider 通过普通作用域加载 Prefab 和叠加场景 |
| R6 启动时内容更新 | `VersionFile`、`PatchManager`、`FileDownloader`、SHA-256 内容仓库 | HTTP 安装、增量复用、本地修复、激活失败处理、离线 Manifest 校验 |
| R7 可定位并支持分类处理的失败 | `AssetErrorCode` 和 `AssetException`；取消仍使用 `OperationCanceledException` | 地址不存在、类型错误、资源包缺失或损坏、超时、下载中取消与重试 |
| R8 持有关系诊断 | `AssetTracker`、只读快照、`AssetDebuggerWindow` | 拥有者计数、卸载失败状态、共享包计数、最终句柄全部归零的断言 |

原始文档结束于核心抽象阶段。本次实现完成了其中规划的基础设施、子系统及集成要求，
并解决了独立等待者、Manifest 数据不可变、卸载失败和实例生命周期等遗留问题。

## 自动化结果

| 检查项 | 结果 |
| --- | --- |
| Unity 脚本编译 | 通过；无编译错误 |
| `GameFramework.Asset.EditModeTests` | 18 项通过，0 项失败，0 项跳过 |
| `GameFramework.Asset.PlayModeTests` | 29 项通过，0 项失败，0 项跳过 |
| Windows 真实 AssetBundle 构建 | 通过；6 个资源包，6 个资源地址 |
| Windows 开发版 Player 构建 | 成功；0 个构建错误 |
| 独立 D3D11 Player 自动验收 | 通过；退出码为 0；运行日志未匹配到错误、警告或异常 |
| Unity 资源元数据配对 | 无缺失或孤立的 `.meta` 文件 |

最终测试任务 ID：

- EditMode：`b77d013593304ee6a4ae00d88ca2205e`。
- PlayMode：`e1eb7633ce0d478a81cdf697eba87cda`。

Player 自动验收依次加载随包内容、进入战斗、返回主城、检查剩余实例的材质，并等待系统彻底关闭。
最终输出保存在本地 `Build/Player/final-smoke-result.json`：

```json
{
  "passed": true,
  "cityBundles": 2,
  "battleBundles": 6,
  "commonReferences": 2,
  "heroReferences": 2,
  "returnedCityBundles": 2,
  "finalAssets": 0,
  "finalBundles": 0,
  "error": ""
}
```

Player 和日志属于生成产物，位于已有忽略规则覆盖的 `Build` 目录。
Windows 首装资源包及其 Manifest 位于 `Assets/StreamingAssets/GameFrameworkAsset/Windows`，
并配有对应的 Unity 元数据。示例资源、构建配置和场景均保留为普通项目资源。
验证过程中生成的本地截图已加入忽略清单。

## 运行时人工检查

`AssetLifecycleLab` 场景已使用真实 Bundle 模式运行。战斗期间有 4 个资源句柄和 6 个包句柄，
`ui_common` 与 `hero_001` 的引用计数均为 `ref=2`。
4 个可见渲染器均持有有效的 URP Unlit 材质。返回主城后剩余 1 个资源句柄、
2 个包句柄和 1 个已加载场景；`ui_common` 的引用计数为 `ref=1`，主城材质保持有效。
Game View 截图确认，在 1920 × 1080 分辨率下，主城与战斗对象、诊断信息和控件正常显示，无重叠。

此前的无图形模式验收也通过了生命周期断言。无图形设备时出现的 Shader 警告符合预期，
该结果不作为渲染验证依据。独立 Player 的最终验证结果以上述 D3D11 运行结果为准。

## 验证边界

已实现 Android/iOS 平台映射和随包 URI 加载路径，但没有连接或测试 Android/iOS 真机。
设备原生文件系统、切入后台与恢复、内存以及目标平台构建后的渲染行为，仍需完成真机验收。
本记录不声称已完成这些平台的 Player 构建。

约定的 MVP 不包含同步加载、子资源、LRU、加密、断点续传、按需下载和自动分包优化。
更新在 `AssetSystem` 初始化之前执行。SHA-256 用于检查文件完整性，发布来源的可信性由部署渠道保证。
旧的已校验内容保留在磁盘上，以便更新失败时继续使用上一版本。

编辑器构建可能重写 URP 设置、`GraphicsSettings` 格式和输入资源预加载配置。
Player 构建完成后，已从工作区清理这些自动生成的变更；本功能不包含渲染或输入配置调整。

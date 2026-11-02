# HaloFrame UI 迁移与导航验收

日期：2026-11-02。分支：ui-framework。Unity：6000.0.32f1。环境：Windows、URP、Mono Player、D3D11。

## 交付范围

运行时位于 Assets/GameFramework/Runtime/UI，示例位于 Assets/GameFramework/Samples/UI。
保留来源的 10 个 Prefab、9 个视图类型与示例场景，改用原生 UIConfigSO、AssetManifest 地址及
现有 AssetSystem。所有资源请求直接调用 AssetScope，无新增资源桥接脚本或第三方依赖。

| 改进 | 最终行为与测试依据 |
| --- | --- |
| 导航契约 | UIHandle 标识一次访问；关闭指定句柄、关闭最后一条同类型访问、按层 Pop 语义明确；连续 Pop、关闭中间记录、加载中关闭均有测试 |
| 生命周期与重入 | 保留 OnAwake / OnStart / OnEnable / OnDisable / OnDestroy；每实例初始化和 Start 一次；回调内的导航延后执行，销毁幂等；异常和回调内关闭均已验证 |
| 资源所有权 | 根共享实例、动态子界面、地址 Item 各有 AssetScope；静态节点借用父对象；模板 Item 共享父资产依赖；晚完成、独立取消和最终资源归零已验证 |
| 配置精炼 | 删除 ResId 与第二份资源路径表；不依赖 Odin 或静态 GameManager；配置校验并复制为只读定义；覆盖重复、缺失、环、非法地址、路径、分辨率 |
| 排序 | 根据可见视图树紧凑分配，计入嵌套 Canvas、子界面和 Item；显式设置 overrideSorting；移除 10/9 界面数量限制；覆盖 24 次访问及跨层排序 |
| 隐藏性能边界 | 不停用根 GameObject，暂停自有 Canvas、GraphicRaycaster、Animator，并还原初始启用状态；隐藏取消 VisibilityToken；无逐帧导航轮询，不宣称整体零 GC |
| 导航记录与唯一实例 | A-B-C-B 共四条访问记录、三份根实例，两个 B 共用实例和加载任务；回退 C-B-A；最后一条引用关闭才释放；每次访问独立参数及可选状态快照 |

每次访问的 Arguments 数组复制；内部参数对象须由业务提供不可变数据。页面状态通过
CaptureNavigationState / RestoreNavigationState 显式保存和恢复，不自动序列化任意业务对象。
子界面保留父级内部选择语义，不混入根导航历史。UIManager 不关闭应用拥有的 AssetSystem。

## 自动化结果

| 检查 | 结果 |
| --- | --- |
| Unity 编译 | 成功；控制台 0 条 Error |
| dotnet build HaloRes2.sln --no-restore --nologo | 成功；包含新 UI、示例与测试程序集，0 警告、0 错误 |
| UI EditMode | 15/15 通过 |
| 资源框架 EditMode 回归 | 18/18 通过 |
| UI PlayMode | 32/32 通过；含真实 Bundle 集成 |
| 资源框架 PlayMode 回归 | 29/29 通过 |
| 合计 | 94/94 通过，0 失败、0 跳过 |
| Windows Player | BuildReport 为 Succeeded，BuildOptions.Development / StrictMode |
| 最终 Player 冒烟 | 退出码 0；复用、回退、释放均为 true |
| 资产检查 | 新增目录无缺失/孤立 .meta，Assets 无重复 GUID；C# 文件均低于 500 行 |
| git diff --check | 通过 |

测试任务 ID：

- EditMode：66eb0eeed8ce4093bbc0b3f1d1f7a8cd，33 项通过。
- 最终 PlayMode：735ed63eaa8243858f32d5d0d00c053d，61 项通过。

首轮运行发现并修复了 Unity 对象的假 null 导致 Item 未补齐 CanvasGroup，以及嵌套 Canvas
未开启 overrideSorting 的问题。收尾补测覆盖状态快照抛异常、子界面启用失败的选择回滚、
共享子界面加载独立取消、父级关闭取消子界面/Item、工作线程取消、隐藏 Animator 与异常清理。

最终 Player 的机器可读结果：

```json
{
  "passed": true,
  "reusedB": true,
  "returnedInOrder": true,
  "resourcesReleased": true,
  "error": ""
}
```

本地验收产物（Build 已被 Git 忽略）：

- Build/Verification/ui-test-summary-2026-11-02.json
- Build/Verification/ui-and-assets-playmode-2026-11-02.xml
- Build/Verification/ui-player-build.txt
- Build/Verification/ui-player-smoke-verified-2026-11-02.json
- Build/Verification/ui-player-verified-2026-11-02.log
- Build/Verification/ui-home-final.png、ui-child-final.png：Unity 运行画面，已目视检查。
- Build/UIPlayer/HaloFrameUI.exe

## 复现与验证边界

执行 Game Framework > UI > Stage Sample Bundles 后运行 UI 与资源框架的 EditMode、PlayMode 测试。
执行 Build Windows Sample Player 以后：

```powershell
$resultPath = Join-Path $PWD 'Build/Verification/ui-player-smoke.json'
$logPath = Join-Path $PWD 'Build/Verification/ui-player.log'
$player = Start-Process './Build/UIPlayer/HaloFrameUI.exe' -ArgumentList @(
    '-batchmode', '-force-d3d11', '--ui-smoke-result', $resultPath, '-logFile', $logPath
) -WindowStyle Hidden -PassThru -Wait
$player.ExitCode
```

已验证 Windows 编辑器和独立 Mono Player；移动设备、IL2CPP、复杂业务页面状态和长期
运行性能不由本轮结果证明。框架不新增对象池、动画系统、热更新、商业工具或代码生成器。
架构与使用契约见 ADR 0004 和 README。

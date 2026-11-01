# UI 框架验收记录

日期：2026-11-01。项目：HaloRes2。Unity：6000.0.32f1，URP 17、UGUI、Timeline。
设计依据：`UI框架设计.md` 的 C 契约覆盖早期 F 契约，结合 ADR 0002 与 ADR 0003。

## 功能覆盖

| 设计范围 | 完成内容 | 验证依据 |
| --- | --- | --- |
| C2–C9 导航与收敛 | Push/Replace/Reset、Single 交接、替换失败回退、回调导航重新派生、参数校验、结果句柄、历史和上下文 | 确定性内核场景、200 次固定种子导航、真实弹窗与类型化结果 |
| C10–C14 节点与生命周期 | Instance/Session/Resume/Bind Scope、Panel 有效可见性、树转发、标脏刷新、Item 绑定版本、逐项异常收尾 | Scope 取消异常、父级隐藏、过期事件和列表销毁测试 |
| C15 资源与消息 | 外部 AssetSystem 注入、AssetScope 持有 Prefab、独立取消、主线程消息 FIFO、嵌套跨类型发布 | 预加载不导航、真实 Bundle 取消、消息顺序与中途退订测试 |
| C16 渲染后端 | 分段排序、共享遮罩、遮罩点击关闭、System 与强输入锁分离、延迟加载圈、隐藏状态保留 | 真实 GraphicRaycaster 射线、System 自身按钮交互、嵌套 Canvas/Raycaster 状态恢复 |
| Timeline | 不受 timeScale 影响、入场 Finish 后 Stop、出场隐藏后 Stop、Alpha Clip 插值 | 零 timeScale PlayMode 测试、运行画面及终态 Lint |
| 相机与屏幕适配 | URP UI 相机栈、世界遮挡、场景相机注册和恢复、无世界相机回退；Expand/None/Letterbox 和安全区交集 | 相机切换/销毁、设计比例测试、演示分辨率运行 |
| 实例池 | KeepAlive 预加载、LRU 容量、低内存 Trim、Single Exiting 交接、失败实例销毁、原生销毁后释放依赖 | 复用/淘汰/交接测试、真实模型依赖与最终句柄归零 |
| 列表与扩展控件 | 模板列表、ScrollRect 虚拟列表、固定/可变尺寸、ScrollTo/RefreshAt/进入视区事件、模型 RT 池、HUD 跟随与池、拖拽复制与取消 | 1000 项列表、可视数量限制、模型会话释放、拖拽恢复测试 |
| C17–C18 工具链 | 可序列化组件拆分、按程序集生成 Registry、强类型绑定、Panel 节点绑定、Creator 模板及默认 Timeline、可扩展 Lint、StyleMenu、Debugger | 六个演示 Prefab 全部通过 Lint，终态和 Panel 布局错误测试 |
| C19 集成与关闭 | 外部资源系统所有权、可选自动 Root/EventSystem、KeepAlive 预加载、低内存处理、关闭重入隔离 | Shutdown 后外部系统仍有效；WaitClosed 延续中关闭的回归测试；独立 Player |

`Assets/GameUI/Components/UIComponents.cs` 已删除并拆为可序列化的独立组件；
Demo 使用生成的绑定与 Registry，已删除临时字段查找回退。重复旧截图已删除。
打包目录只保留当前 Manifest 引用的文件，避免重复构建把旧哈希资源一起带入 Player。

## 最终自动化结果

| 检查项 | 结果 |
| --- | --- |
| Unity 编译 | 通过，无编译错误 |
| GameUI EditMode | 51/51 通过 |
| GameUI PlayMode | 14/14 通过，其中 3 项使用真实 AssetBundle |
| 原资源框架 EditMode 回归 | 18/18 通过 |
| 原资源框架 PlayMode 回归 | 29/29 通过 |
| 合计 | 112 项通过，0 失败，0 跳过 |
| 演示 Prefab Lint | 6 个通过，无错误或警告 |
| Windows AssetBundle | 6 个地址，6 个当前版本资源包 |
| Windows 开发版 Player | 构建成功；Mono、D3D11 |
| Player 自动验收 | 通过，退出码 0；流程中出现 Error/Exception/Assert 会使验收失败 |

最终完整回归任务：

- EditMode：`6a8b41643f3741618040a45999a831b8`，69 项通过。
- PlayMode：`3d6d3f555ce946468fd835ab30a4a28f`，43 项通过。

Player 验收按顺序检查首页、共享遮罩弹窗、System Overlay、bool 返回值确认框、
虚拟列表和模型目标，并等待关闭后 Asset 与 Bundle 句柄全部归零。
验收过程中发现并修复了 `WaitClosed` 延续与关闭实例池之间的重入顺序问题，
对应新增 PlayMode 回归测试 `ShutdownFromWaitClosedContinuationDefersPoolDisposal`。

本地产物位于 Git 忽略的 `Build`：

- `UIPlayer/GameUIDemo.exe`：可交互运行的开发版。
- `Verification/ui-player-build.txt`：构建结果。
- `Verification/ui-player-smoke.json`：自动验收结果。
- `Verification/ui-player.log`：独立 Player 日志。
- `Verification/ui-widgets.png`、`Verification/ui-popup-verified.png`：已检查的编辑器运行画面，包含虚拟列表、透明模型目标和模态遮罩。

最终源码均小于 500 行；`git diff --check` 通过；UI 与随包资源的 `.meta` 无缺失或孤立项。
Player 日志未匹配到异常、错误或警告。Unity 构建产生的无关全局图形配置序列化差异已恢复。

```json
{
  "passed": true,
  "mode": "Bundle",
  "checkedViews": 5,
  "error": ""
}
```

可重现命令（PowerShell，项目根目录）：

```powershell
& ./Build/UIPlayer/GameUIDemo.exe -batchmode -force-d3d11 `
  --ui-smoke-result "$PWD/Build/Verification/ui-player-smoke.json" `
  -logFile "$PWD/Build/Verification/ui-player.log"
```

## 性能测量边界

EditMode 使用 `GC.GetAllocatedBytesForCurrentThread` 测量预热后的 100 次空闲 Kernel Tick，
以及简单 int 数据绑定下的 100 次虚拟列表跳转复用，两者均为 0 字节托管分配。
这不包括用户 OnBind 中的新建字符串、取消令牌、Scope 登记项或异步任务，
也不代表 Unity 渲染系统、所有业务列表或 IL2CPP 真机的整体帧分配为零。

## 验证边界

- 已验证 Windows 编辑器和独立 Mono Player；Android/iOS、IL2CPP、设备安全区、后台恢复和移动端性能仍需目标设备验证。
- HUD、拖拽和安全区已有运行实现；尚未在多种真实设备和业务场景中进行长时间交互验收。
- Timeline 终态 Lint 覆盖 Alpha Track 和 AnimationTrack 的序列化数值曲线；复杂混合及自定义 Track 需对应规则或运行验收。
- 按既定 ADR 未引入 HybridCLR。设计中的后续扩展、商业静态分析器和课程练习不是本次运行时功能缺项。

架构接入说明见 README；关闭重入、资源持有与编辑器约束见 ADR 0003。

# UI 运行时集成与编辑器约束

状态：已采纳，2026-11-01。

## 所有权与关闭顺序

`UIBootConfig.System` 必须由应用注入。应用负责初始化资源系统、驱动 Tick 和最终
`ShutdownAsync`；UI 负责自己的资源申请、实例池和会话。UI 不重复安装或关闭外部资源系统。
`UIBoot.Shutdown` 先结束内核会话，再清空实例池、拖拽、HUD 与模型舞台，最后恢复相机。
安装失败走同一条清理路径。关闭允许由 Root 销毁或应用主动调用，并防止销毁回调重入。
如果在生命周期回调或 `WaitClosed` 延续中关闭，内核先收敛完当前轮次，再发出 `Disposed`；
UIBoot 收到该事件后清空实例池，避免收敛中的条目仍引用已经销毁的逻辑对象。
此期间不能重新安装 UI；应用可在下一帧确认关闭完成后安装。

每个缓存 UI 实例持有一个 Prefab 资源句柄。GameObject 销毁使用 Unity 的延迟销毁语义，
原生对象消失以后才释放 Prefab 句柄，避免材质、Timeline 和模型依赖提前卸载。
预加载只完成绑定和 OnCreate；会话 Scope 在真正 Open 时创建。

自动创建的 UIRoot 和 EventSystem 由 UI 持久化并在关闭时销毁；注入的 Root 与 EventSystem
由场景拥有。UI 相机为独立对象，不能作为 Screen Space Camera Canvas 的子对象：
Canvas 会根据相机调整变换，子相机反过来依赖 Canvas 会使布局和射线计算失效。
更换世界相机时先恢复原相机，向新相机应用当前世界遮挡；无世界相机时使用独立 Base 相机。

## 节点、列表与失败隔离

节点树统一处理 Session/Resume 生命周期和反向收尾。一个 Item 的 Unbind 或 Destroy
抛出异常时，仍继续释放其他 Item 和原生对象。会话回调或刷新失败进入内核 Failed 路径；
故障实例销毁，不能进入缓存。Scope 逐项隔离取消和释放异常。

循环列表缓存累计尺寸并用二分查找定位可视区，实例数量取决于视区及两侧缓冲。
Item 监听在 OnCreate 中绑定一次，交互附带绑定版本；异步业务须捕获稳定业务身份。
简单数据绑定、预热后的滚动复用不分配托管内存；每次绑定创建业务任务、令牌、字符串或
Scope 登记项的成本由业务承担。测试和报告必须明确这一边界。

## 代码生成与资源检查

Registry 按业务程序集生成，运行时使用直接类型引用和构造函数，不扫描类型。
Binding 使用有序布局哈希，哈希忽略实例根节点名称，允许嵌套 Prefab 重命名。
生成器保留手写文件，保存 Prefab 时更新绑定；Lint 校验布局、生成代码和共享 Panel 一致性。
跨 View 复用的 Panel 使用嵌套 Prefab。

过渡终态 Lint 静态检查 Alpha Track 与 AnimationTrack 的序列化数值曲线，避免检查过程
执行 Signal 或音频。自定义 Track 的语义需要新增对应 Lint 规则；复杂混合动画仍须运行验收。
运行时对入场执行 Finish 后 Stop，对出场先 Finish、隐藏后 Stop。

## 平台范围

沿用 ADR 0002 的程序集边界与不引入 HybridCLR 的决定。当前验收使用 Windows 编辑器和
真实 AssetBundle Player。Android/iOS 设备性能、生命周期与 IL2CPP 构建需在对应设备验证。

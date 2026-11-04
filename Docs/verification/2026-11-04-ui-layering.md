# UI 运行时分层重构验证

日期：2026-11-04。分支：`ui-framework`。

## 已完成检查

| 检查 | 结果 |
| --- | --- |
| `dotnet build HaloRes2.sln --no-restore --nologo` | 成功，0 警告、0 错误 |
| `git diff --check` | 通过；仅提示改动文件的 LF/CRLF 转换 |
| 迁移脚本 GUID | 16 个原脚本 `.meta` 与新位置一致 |
| Assets GUID 唯一性 | 检查 174 个 `.meta`，0 个重复 GUID |
| UI 源码文件长度 | 29 个 C# 文件均不超过 500 行 |

复杂导航、加载、生命周期、子视图切换、静态节点借用和配置图校验处补充了中文注释。
目录职责决策见 `Docs/adr/0005-ui-runtime-layering.md`。

## Unity Test Runner

Unity 6000.0.32f1 已通过 Unity MCP 导入并编译最新脚本。最终 Console 查询为 0 条 Error、1 条
MCP 传输 Warning：`[WebSocket] Unexpected receive error: WebSocket is not initialised`。刷新后的
Editor 快照显示 Play Mode、测试、编译和 AssetDatabase 更新均空闲；`ready_for_tools` 因状态缓存过期仍为 false。

| 检查 | 结果 | Unity MCP job |
| --- | --- | --- |
| UI 与 AssetSystem EditMode | 35/35 通过 | `56ee51a958704134bf6b995726e53c28` |
| UI PlayMode（含真实 Bundle 集成） | 32/32 通过 | `de7f10ac4bb140c6a7fb22acd59a2283` |
| AssetSystem PlayMode | 29/29 通过 | `ca508de2369f4deab2faf9aa52e1d6a2` |
| 合计 | 96/96 通过，0 失败、0 跳过 | |

针对性回归确认：A-B-C-B 共用一个 B 实例并按访问顺序返回；重复访问的参数与页面状态分别恢复；
状态捕获异常不阻断可见栈更新；生命周期回调内 Dispose 会完成排队句柄；失败视图移除后背景页恢复。

2026-11-02 的 94/94 是分层重构前的历史基线；本轮 96/96 为最新结果。

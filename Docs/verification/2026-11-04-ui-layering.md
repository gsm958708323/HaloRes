# UI 运行时分层重构验证

日期：2026-11-04。分支：`ui-framework`。

## 已完成检查

| 检查 | 结果 |
| --- | --- |
| `dotnet build HaloRes2.sln --no-restore --nologo` | 成功，0 警告、0 错误 |
| `git diff --check` | 通过；仅报告既有文件换行符将转换为 CRLF |
| 迁移脚本 GUID | 16 个原脚本 `.meta` 与新位置一致 |
| Assets GUID 唯一性 | 检查 174 个 `.meta`，0 个重复 GUID |
| UI 源码文件长度 | 29 个 C# 文件均不超过 500 行 |

复杂导航、加载、生命周期、子视图切换、静态节点借用和配置图校验处补充了中文注释。
目录职责决策见 `Docs/adr/0005-ui-runtime-layering.md`。

## Unity Test Runner 状态

本轮通过 Unity MCP 读取到 Editor 正在运行 PlayMode，活动作业为
`d8fb3477d85a4bb78f51c6aa36b72923`，当前测试是
`GameFramework.UI.Tests.UINavigationTests.ABCBUsesOneBAndPreservesEveryReturn`。
该作业管理记录已被标记为手动清理，但 `mcpforunity://editor/state` 仍报告
`tests.is_running=true`，因此新 EditMode 测试请求返回 `tests_running`，没有启动新的测试。

旧作业记录为 38/61，包含 8 个超时和 1 个 TearDown `NullReferenceException`。这些结果来自
`UIHandle.Opened` 完成通知修复前的代码，且 Runner 尚未导入本轮文件；不能作为本轮代码的测试结论。
Editor 同时报告 `assets.external_changes_dirty=true`。`dotnet build` 编译成功不等于 Unity
已导入并编译这些最新文件。

要完成验收，需要先让当前 PlayMode Runner 结束或经授权停止，再等待 Unity 导入和编译最新文件，
然后通过 Unity MCP 运行 UI EditMode、UI PlayMode 及 AssetSystem 回归。2026-11-02 验收文档中的
94/94 是本次目录分层调整前的历史基线，不计入本轮结果。

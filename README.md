# Codex Quota Pill · 安全隔离版 0.1.0

Windows 11 WPF 桌面额度胶囊。横向 280 × 54 DIP，左右吸附时 154 × 100 DIP。无边框、置顶、不激活主窗口，支持托盘、位置记忆、DPI 缩放及约 140 ms 吸附动画。

**当前版本未接通真实额度。** 默认显示 `5h --`、`7d --`、`重置 · --`。这是按用户“不得修改 Codex 环境”约束保留的安全限制，不是查询成功。

## 运行

便携版在本项目 `03_最终交付/Windows-x64/` 中。双击 `CodexQuotaPill.exe`，不需要管理员权限或额外安装 .NET。必须保留整个目录，不能只复制 EXE。程序不会设置开机启动。

- 拖动胶囊；接近顶部保持横向，接近左右边缘切换纵向。拖离边缘恢复横向。
- 托盘右键：显示/隐藏、刷新、退出。重复启动直接退出，不激活首个窗口。
- 位置记录写入 EXE 旁 `data/position.json`。目录不可写时退化为内存状态。
- 鼠标悬停可查看状态和本地重置时间，不展开面板，不改变尺寸。
- `CodexQuotaPill.exe --demo`：仅界面演示，显示模拟 78% / 43%。需先退出正在运行的普通实例。Tooltip 和托盘注明模拟数据，模拟额度不会写入缓存。
- 默认模式完全不启动 Codex 子进程、不联网查询、不读取 Codex 配置或凭证。

## 数据语义

`QuotaParser` 支持官方 `account/rateLimits/read` 响应及其 JSON-RPC 包装。优先选择 `rateLimitsByLimitId.codex`；没有标准组时，不使用专用额度组冒充。兼容旧的未标注组及明确标为 codex 的 `rateLimits`。

按 `windowDurationMins=300/10080` 识别周期，不依赖 primary/secondary 顺序；剩余额度为 `clamp(100-usedPercent,0,100)`。缺失、歧义或其他周期显示 `--`。重置时间仅使用服务端 Unix 秒；按本地时区呈现 Tooltip。本地计时不额外请求接口。

数据调度模块支持 60 秒刷新、10 秒超时、60/120/240/480/900 秒失败退避、保留缓存、重置后查询及防请求风暴。过期额度后附 `*`，Tooltip 说明数据可能过期；不假设额度恢复到 100%。这些行为已通过模拟测试，尚未通过真实账户验证。

## 为什么真实接入暂不可用

检查到的 Codex CLI 是 `0.163.0-alpha.11.1`。即使 `--version` / `app-server --help` 也尝试清理共享临时目录和创建 PATH 别名，均被沙箱拒绝。发现后停止进一步调用该可执行文件。

官方托管登录支持自动刷新及保存凭证。当前无法证明独立 app-server 在复用登录时不会写入共享状态；因此没有启动真实 app-server，没有代理已有会话，没有复制凭证、登录、创建对话或发送模型请求。

`AppServerProtocol` 仅实现初始化和只读额度请求的流协议，不拥有进程启动或凭证访问能力。它通过本项目独立模拟子进程完成测试。发布版 `SafetyBlockedSource` 没有绕过开关；启用真实接入需要先补充经过验证的安全方案及新的兼容性测试。

## 构建与测试

源码使用 .NET 8、WPF、WinForms NotifyIcon 和 Windows 原生 API，无第三方 NuGet 包。开发机使用同一项目内的便携 SDK；`build.ps1` 不安装 SDK。其他机器可使用自行准备的 .NET 8 SDK。

```powershell
./build.ps1 -Test       # 48 项额度、协议、调度、存储及几何测试
./build.ps1             # 编译
./build.ps1 -Publish    # win-x64 自包含目录
```

构建脚本仅为当前进程设置环境变量，禁用 SDK 证书生成、遥测及工具路径注册。发布为目录，避免单文件原生库运行时自解压。发布版 `--ui-test` 和 `--integration-test` 只验证本组件窗口并自动退出，报告在旁边 `artifacts/`，不读取桌面或其他应用图像。

详见 [接口与安全说明](docs/INTERFACE.md) 和 [测试报告](docs/TEST_REPORT.md)。

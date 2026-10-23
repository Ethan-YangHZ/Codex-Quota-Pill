# 官方接口与安全边界

核对日期：2026-10-23。

官方文档：<https://developers.openai.com/codex/app-server/>（现重定向至 <https://learn.chatgpt.com/docs/app-server>）。官方实现参考：<https://github.com/openai/codex/blob/main/codex-rs/app-server/src/request_processors/account_processor.rs>。

流程为：建立专属 stdio 流 → `initialize`（clientInfo）→ 等待成功响应 → `initialized` 通知 → `account/rateLimits/read`（空参数）→ 按请求 id 匹配 result。允许忽略通知、无关响应；对服务端凭证或审批请求返回“不支持”。从不调用 thread/start、turn/start、登录、注销、写配置或兑换重置额度。

标准额度组优先选择 `codex`。只识别实际 300 和 10080 分钟窗口；不补齐缺失周期，也不跨不同额度组拼接。`resetsAt` 单位为 Unix 秒。响应原文、账户 ID、邮箱等不写入缓存或错误日志。

已确认官方文档存在该方法；**未运行当前安装版本的真实连接或生成协议 schema，不能声称当前安装版及当前账户已验证兼容。** 当前发布版没有真实进程适配器，只有经过模拟服务测试的独立流客户端。缓存与刷新机制也仅具备模拟测试证据。

终止真实测试的原因：Codex CLI 在最小版本/帮助命令中已出现共享目录写入尝试。托管认证本身也存在自动刷新及持久化机制。用户禁止修改共享凭证、会话和配置，高于接通额度的要求，因此没有继续启动 app-server，也没有使用任何非官方额度端点、凭证复制或屏幕读取作为替代。

模拟服务由测试程序通过 `Process.Start` 独立启动，使用私有管道。测试退出时先关闭 stdin，必要时只针对所持有 Process 对象终止其自身，不按名称扫描或清理系统进程。发布版正常运行时不启动任何子进程。

开发异常记录：SDK 首次运行曾输出创建 ASP.NET 开发证书及添加 PATH 的提示；PATH 添加明确失败。其后只读核查未找到 ASP.NET 开发证书，没有执行删除或信任操作。后续构建显式禁用证书生成和工具路径注册。此提示不应被隐瞒或解释为已经证实的零系统副作用。没有安装全局依赖、修改系统安全设置或添加开机启动。

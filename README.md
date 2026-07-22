# 别离检测工具（HardwareScope）

[![构建状态](https://github.com/shanyin119/HardwareScope/actions/workflows/build.yml/badge.svg)](https://github.com/shanyin119/HardwareScope/actions/workflows/build.yml)
[![许可证：GPL-3.0](https://img.shields.io/badge/License-GPL--3.0-blue.svg)](LICENSE)

别离检测工具是一款面向 Windows 10/11 x64 的中文硬件信息与温度监控工具。它以清晰的分类展示电脑硬件，提供可配置的桌面温度悬浮窗、游戏模式和硬件信息导出功能。

## 主要功能

- 汇总处理器、显卡、内存、主板、存储、网络和其他硬件信息。
- 实时显示温度以及本次运行期间的最低、最高温度。
- 桌面温度悬浮窗支持拖动、四角定位、靠边隐藏、固定和位置记忆。
- 可选择悬浮窗显示硬件规格或硬件类型，并自定义显示的传感器。
- 游戏模式可将程序收至通知区域，同时保持温度悬浮窗置顶。
- 支持全局快捷键、开机启动和可调刷新频率。
- 硬件信息可按选择范围导出为 TXT、DOCX 或 XLSX。
- 安装程序包含标准卸载入口。

## 系统要求

- Windows 10 或 Windows 11，64 位。
- 发布包自带 .NET 运行时，不需要另外安装 .NET。
- 读取部分处理器和主板温度需要管理员权限及可选的 PawnIO 2.2 驱动。软件只会在用户明确确认后，从 PawnIO 官方 GitHub 发布地址下载驱动，并在运行前验证 SHA-256。

## 构建

安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 后运行：

```powershell
dotnet restore .\HardwareScope.csproj -r win-x64
dotnet publish .\HardwareScope.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\publish
```

安装程序使用 Inno Setup 7.0.2 构建。仓库中的 GitHub Actions 会从官方不可变发布地址下载指定版本，验证哈希后编译安装程序。完整过程见 [自动构建配置](.github/workflows/build.yml)。

## 隐私

应用不会上传硬件信息、温度、设置或导出内容，也不包含遥测、广告和用户账户。只有在用户明确选择安装温度驱动时，应用才会访问 PawnIO 的 GitHub 下载地址。完整说明见 [隐私政策](PRIVACY.md)。

## Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

- Authors、Reviewers 与 Approvers：[shanyin119](https://github.com/shanyin119)。
- 所有来自其他贡献者的更改必须通过 Pull Request 审查。
- 每个正式版本的签名请求必须由 Approver 人工确认。
- 完整职责、构建来源和发布规则见 [Code signing policy](CODE_SIGNING_POLICY.md)。
- 隐私政策见 [PRIVACY.md](PRIVACY.md)。

当前签名状态：正在申请 SignPath Foundation 开源代码签名，获批前发布文件可能仍显示“未知发布者”。

## 许可证

本项目自身源代码和原创资源以 [GNU General Public License v3.0 only](LICENSE) 发布。第三方组件继续适用各自的许可证，详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

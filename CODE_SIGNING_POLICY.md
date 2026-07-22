# Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

## 项目与签名范围

- 项目：别离检测工具（HardwareScope）。
- 源代码仓库：<https://github.com/shanyin119/HardwareScope>。
- 只签名由本仓库源代码和仓库内构建脚本生成的本项目二进制文件。
- NuGet 依赖、.NET 运行时、PawnIO 和 Inno Setup 等上游组件不会以本项目身份重新签名。
- 正式签名只用于带版本标签、通过 GitHub 托管运行器构建且通过全部检查的发布产物。

## 团队角色

当前项目由一名维护者负责；随着团队扩大，成员名单将在此文件中更新。

| 角色 | 成员 | 职责 |
| --- | --- | --- |
| Authors / Committers | [shanyin119](https://github.com/shanyin119) | 维护源代码、构建脚本、依赖与发布配置 |
| Reviewers | [shanyin119](https://github.com/shanyin119) | 审查外部贡献者提交的 Pull Request，重点审查构建和签名配置 |
| Approvers | [shanyin119](https://github.com/shanyin119) | 核对版本、来源和构建结果，并人工批准每个 SignPath 签名请求 |

外部贡献者不能直接写入默认分支，其更改必须通过 Pull Request 并由 Reviewer 审查。

## 构建与发布控制

1. 正式产物由 `.github/workflows/build.yml` 在 GitHub 托管的 Windows 运行器上生成。
2. 发布标签必须与项目文件中的版本一致。
3. CI 固定 Inno Setup 下载地址和 SHA-256；NuGet 依赖版本在项目文件中明确指定。
4. 未签名产物先作为 GitHub Actions artifact 保存，以便 SignPath 验证构建来源。
5. SignPath 获批并配置后，每个正式版本都必须由 Approver 人工批准签名。
6. 签名配置只选择本项目生成的可执行文件；不会签名上游第三方 DLL 或驱动。
7. 正式下载页面必须链接到本政策和 [隐私政策](PRIVACY.md)。

## 账户安全

所有能够修改源代码、审查更改或批准签名的团队成员必须为 GitHub 和 SignPath 开启多重身份验证。访问权限不再需要时应立即撤销。

## 隐私

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

具体例外、PawnIO 用户主动下载行为以及本地数据位置见 [PRIVACY.md](PRIVACY.md)。

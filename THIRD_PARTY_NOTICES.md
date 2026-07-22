# 第三方开源组件声明

别离检测工具自身代码以 GPL-3.0-only 发布。以下组件不改变其原许可证，版权归各自作者所有。

## 直接依赖

| 组件 | 版本 | 许可证 | 项目地址 |
| --- | --- | --- | --- |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 | <https://github.com/LibreHardwareMonitor/LibreHardwareMonitor> |
| DocumentFormat.OpenXml | 3.5.1 | MIT | <https://github.com/dotnet/Open-XML-SDK> |
| System.Management | 10.0.2 | MIT | <https://github.com/dotnet/dotnet> |

## 传递依赖

| 组件 | 版本 | 许可证 |
| --- | --- | --- |
| BlackSharp.Core | 1.0.7 | MPL-2.0 |
| DiskInfoToolkit | 1.1.2 | MPL-2.0 |
| DocumentFormat.OpenXml.Framework | 3.5.1 | MIT |
| HidSharp | 2.6.4 | Apache-2.0 |
| Mono.Posix.NETStandard | 1.0.0 | 以 NuGet 包内许可证为准 |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0 |
| System.IO.Ports 及其运行时包 | 10.0.3 | MIT |

NuGet 还可能恢复由上述包声明的运行时组件。发布包中的许可证文件及对应 NuGet 元数据是其完整许可条款的权威来源。

## 构建和可选运行组件

- [.NET](https://github.com/dotnet/dotnet)：用于构建，并在自包含发布中提供运行时；适用其各组件许可证。
- [Inno Setup](https://jrsoftware.org/isinfo.php) 7.0.2：用于生成 Windows 安装程序；适用 Inno Setup License。
- [PawnIO](https://github.com/namazso/PawnIO.Setup) 2.2：用于可选的低层硬件访问。该安装程序不会捆绑在本项目中，只会在用户明确确认后从上游发布地址下载。

本文件用于提供清晰索引，不替代任何第三方组件随附的完整许可证文本。

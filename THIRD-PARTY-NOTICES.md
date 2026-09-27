# 第三方组件声明

本仓库的发行物包含以下第三方组件。各组件的版权与许可证归其各自作者所有。

## aria2（aria2c.exe）

- **组件**：aria2c.exe（aria2 命令行下载工具）
- **版本**：1.37.0
- **来源**：https://github.com/aria2/aria2
- **许可证**：GNU General Public License v2.0 或更高版本（GPL-2.0-or-later）
- **许可证全文**：https://www.gnu.org/licenses/old-licenses/gpl-2.0.html
- **使用方式**：`assets/aria2c/aria2c.exe` 为 aria2 官方发行版的**未修改二进制**，作为独立进程被 aria-gui 启动并由进程管道通信，与 aria-gui 自身代码不存在链接关系（聚合分发）。
- **再分发提示**：再分发包含该二进制的本项目时，须同时满足 GPLv2 的要求——保留版权与许可证声明、随附（或明确提供获取途径的）GPLv2 许可证全文，并确保接收者可获取对应源代码（来源见上）。

# Quiet Control Center

[中文](#中文) · [English](#english) · [Releases](https://github.com/cc282855/QuietControlCenter/releases)

> A modern Windows desktop client based on v2rayN, with a maintained custom interface and a signed update channel.
> 基于 v2rayN 的现代化 Windows 桌面客户端，提供持续维护的定制界面与签名更新通道。

## 中文

### 项目介绍

Quiet Control Center 是基于 [2dust/v2rayN](https://github.com/2dust/v2rayN) 开发的 Windows x64 完整客户端。它保留 v2rayN 的节点、订阅、路由、系统代理、TUN、Clash API、热键和内核管理能力，并重新设计主窗口、节点列表、连接状态和软件更新体验。

本项目不是 v2rayN 官方版本。官方 GUI 包只用于发现新版本，不会直接覆盖 Quiet Control Center 的界面。

### 主要功能

- 现代化主窗口，紧凑布局适配 1120×720 与更高分辨率。
- 完整的节点、订阅、路由、日志、系统代理和 TUN 管理。
- 按节点名称自动识别国家/地区，并记住上次选择的分组。
- 节点列表列显隐：协议、节点名称、地址、端口、传输、安全、延迟和速度。
- “测延迟”以及延迟与速度组合测试。
- 每秒刷新延迟、抖动、丢包、代理流量、直连流量和核心运行状态。
- 永久“软件更新”入口：显示当前版本、官方最新版本、定制版最新版本和上次检查时间。
- 每日自动检查上游 Release，并将完整 UI 与配套功能层迁移到新版本。
- P-256 签名、ZIP SHA-256、产品标记和逐文件哈希校验。
- 暂存安装、目录交换、启动确认和失败回滚；更新时保留用户配置目录。

### 安装

1. 在 [Releases](https://github.com/cc282855/QuietControlCenter/releases) 下载最新的 `QuietControlCenter-*-win-x64.zip`。
2. 解压到一个独立目录，不要覆盖官方 v2rayN 的安装目录。
3. 运行 `v2rayN.exe`。

这是一个完整代理客户端。多个客户端同时控制相同端口、系统代理或 TUN 时可能冲突，请自行规划端口与运行方式。

### 自动更新

GitHub Actions 每天检查官方 v2rayN Release。检测到新版本后，流水线会三方合并定制层、运行测试、导入经过摘要验证的官方内核文件、构建完整包并签名发布。客户端只安装来自 `cc282855/QuietControlCenter` 且通过内置公钥验证的完整包。

如果官方重构导致合并冲突或测试失败，流水线会停止发布，已安装客户端继续使用最后一个验证通过的版本。

### 隐私与安全

- 发布包不包含订阅、节点数据库、日志或运行时配置。
- 签名私钥只保存在 GitHub Actions Secret 中，不进入客户端和仓库。
- 手动替换文件前建议备份 `guiConfigs`；程序内签名更新会自动保留可变配置目录。

## English

### About

Quiet Control Center is a complete Windows x64 client derived from [2dust/v2rayN](https://github.com/2dust/v2rayN). It retains node, subscription, routing, system proxy, TUN, Clash API, hotkey, and core-management capabilities while redesigning the main window, profile list, connection status, and update experience.

This is not an official v2rayN distribution. Official GUI packages are used for version discovery only and never overwrite the Quiet Control Center interface.

### Features

- Modern main window with compact layouts for 1120×720 and larger displays.
- Full node, subscription, routing, logging, system proxy, and TUN management.
- Automatic country/region classification from node names with persistent group selection.
- Configurable profile columns for protocol, name, address, port, transport, security, latency, and speed.
- Dedicated latency testing plus a combined latency-and-speed test.
- Per-second latency, jitter, packet-loss, proxy traffic, direct traffic, and core-status updates.
- Persistent Software Update panel with current, upstream, and custom version information.
- Daily upstream Release discovery and automatic migration of the complete UI and supporting feature layer.
- P-256 signatures, ZIP SHA-256, product marker, and per-file hash verification.
- Staged replacement, startup acknowledgement, and rollback while preserving mutable user configuration.

### Installation

1. Download the latest `QuietControlCenter-*-win-x64.zip` from [Releases](https://github.com/cc282855/QuietControlCenter/releases).
2. Extract it into a separate directory. Do not overwrite an official v2rayN installation.
3. Run `v2rayN.exe`.

This is a full proxy client. Running multiple clients that control the same ports, system proxy, or TUN configuration can cause conflicts; plan the ports and runtime mode accordingly.

### Automatic updates

GitHub Actions checks upstream v2rayN Releases every day. For a new version, the workflow performs a three-way merge of the customization layer, runs the test suites, imports digest-verified upstream core files, builds the complete package, and publishes a signed Release. The client installs only complete packages from `cc282855/QuietControlCenter` that validate against its embedded public key.

If an upstream refactor causes an unresolved merge conflict or a test failure, publication stops and installed clients remain on the last verified version.

### Privacy and security

- Release archives contain no subscriptions, node databases, logs, or runtime configuration.
- The signing private key exists only as a GitHub Actions Secret and is never shipped or committed.
- Back up `guiConfigs` before manual file replacement; signed in-app updates preserve mutable configuration automatically.

## Build / 构建

See [BUILDING.md](BUILDING.md) for the reproducible build and signed-update design.
可复现构建和签名更新设计请参阅 [BUILDING.md](BUILDING.md)。

## License and attribution / 许可证与致谢

Quiet Control Center is derived from [v2rayN](https://github.com/2dust/v2rayN) and is distributed under the [GNU General Public License v3.0](LICENSE). The v2rayN name and upstream project belong to their respective maintainers.
Quiet Control Center 基于 [v2rayN](https://github.com/2dust/v2rayN) 开发，并按照 [GNU GPL v3.0](LICENSE) 发布。v2rayN 名称及上游项目归其维护者所有。

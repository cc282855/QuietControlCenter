# Quiet Control Center v7.24.4.1

## 中文

这是基于官方 v2rayN 7.24.4 的界面反馈修复版本。

- 修复点击“软件更新 → 立即检查”后更新面板立即消失的问题。
- 检查期间保持面板打开并显示“正在检查…”。
- 没有新版本时明确显示“当前已是最新版”。
- 官方已更新但定制包尚未完成时显示“定制版正在适配”。
- 检测到可安装的定制版本时显示对应版本号。
- 检查异常时显示“更新检查失败，请稍后重试”。

## English

This UI-feedback hotfix is based on upstream v2rayN 7.24.4.

- Keeps the Software Update panel open after clicking **Check now**.
- Shows a visible **Checking…** state while the request is running.
- Clearly reports when the installed build is already current.
- Explains when an upstream release exists but the customized build is still being adapted.
- Displays the available custom version when an update is ready.
- Shows an explicit retry message when the check fails.

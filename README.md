# SpotifyHotkey

全局快捷键 **Ctrl+Space**：无论 Spotify 在前台、后台还是托盘，都能播放 / 暂停。

- Spotify 未运行时不拦截按键，Ctrl+Space 仍可用于切换输入法
- 常驻系统托盘，右键菜单可开关快捷键、设置开机自启
- 单文件（约 10KB），基于 Windows 自带 .NET Framework 4，无需安装运行时
- 通过 `WM_APPCOMMAND` 直接发给 Spotify 窗口，不影响其他媒体播放器

## 使用

从 [Releases](../../releases) 下载 `SpotifyHotkey.exe`，双击运行；托盘图标右键勾选「开机自启」即可。

## 本地构建

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /out:SpotifyHotkey.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll Program.cs
```

## 发布

推送 `v*` 标签，GitHub Actions 会自动构建并创建 Release：

```bash
git tag v1.0.0 && git push origin v1.0.0
```

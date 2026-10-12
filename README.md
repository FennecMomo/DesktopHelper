# DesktopHelper 桌面空间

Windows 桌面上的分组启动区域。区域的位置和尺寸由你拖动，分组和图标随可用宽度自动换行，超出高度时滚动。普通应用窗口可以覆盖它。

## 使用

双击 `DesktopHelper.exe`。支持 Windows 10/11 x64，独立打包版无需安装 .NET。

- 拖动顶部工具条移动，拖动右边、底边或右下角改变尺寸。
- 右上角 **＋ 分组** 创建分组；组内 **＋** 添加应用、`.lnk` / `.url` 快捷方式或任意文件，也可以直接从资源管理器拖入。
- 分组 **⋯** 菜单支持重命名、添加文件夹、调整顺序和删除。
- 点击图标启动。右键图标可重命名、调整顺序、移动到其他组或移除；也可拖到另一个组。
- 删除分组或移除入口不会删除原应用、文件或快捷方式。原路径被移动后需要重新添加入口。
- 默认区域空白背景透明，分组卡片和紧凑工具条使用实色底色，文字不直接叠在壁纸上。菜单可切换区域底色、锁定位置和尺寸。
- **整窗鼠标穿透**使整个区域不再接收点击；从托盘菜单的“关闭鼠标穿透”恢复。锁定位置本身不会阻止点击图标。
- 托盘可显示/隐藏、进入临时整理窗口、退出。整理完成在右上角菜单选择“完成整理，返回桌面”。
- 开机启动默认关闭，可在右上角菜单开启。再次启动 EXE 会恢复现有实例。

## 数据与恢复

布局保存在 `%LOCALAPPDATA%\DesktopHelper\layout.json`，包括位置、尺寸、分组和入口。自动保存采用临时文件替换；上一次配置保存在 `.bak`。损坏原文件会另存 `.damaged-*` 后尝试恢复备份。卸载程序时保留该目录即可保留数据。

`status.json` 是本机运行诊断，记录进程、桌面模式、独立窗口策略、透明样式、尺寸和项目数量；不上传到服务器。程序无需网络服务。

## 构建与安装

需要 .NET 10 SDK（Windows）。

```powershell
dotnet build src/DesktopHelper -c Release
./scripts/build.ps1
./scripts/install.ps1
```

安装到 `%LOCALAPPDATA%\Programs\DesktopHelper\DesktopHelper.exe` 并创建桌面快捷方式。安装更新会先通知旧实例保存退出，保留上版 EXE，并核对复制后的 SHA-256。启动设置不随安装自动开启。

GitHub Actions 在 `main` 更新时构建独立 EXE，可在 Actions 的 `DesktopHelper-win-x64` artifact 下载。源码仓库不提交个人布局、测试文件或构建产物。

## 验证

```powershell
./artifacts/publish/DesktopHelper.exe --self-test
./artifacts/publish/DesktopHelper.exe --integration-test
./artifacts/publish/DesktopHelper.exe --quit
```

`--self-test` 使用隔离临时目录，验证持久化、备份恢复、路径去重及 WPF 换行。结果写入 EXE 同目录 `self-test-results.txt`。

`--integration-test` 需要登录状态的 Windows 桌面，会创建独立测试面板、临时计算器快捷方式并启动计算器，检查实际 HWND 独立顶层窗口、非置顶、低于前台应用、分层透明样式、鼠标穿透开关、控件排布、滚动和关闭重开。测试数据与渲染截图写入 EXE 同目录 `integration-results`，不改动个人布局。截图是 WPF 内容渲染，不能单独证明桌面合成效果、鼠标真实拖放或所有 Windows 版本的兼容性。

## 桌面与透明实现

桌面模式使用**独立分层窗口**，不调用 `SetParent`，不嵌入或修改 Explorer 窗口。普通应用在前台时将自身降到窗口底层；检测到桌面成为前台时恢复自身可见性并放到桌面前面，保持非置顶、不抢焦点。用户从托盘隐藏后不会被自动恢复。不同 Windows 版本的“显示桌面”、虚拟桌面及多屏混合 DPI 仍需实机验收。

0.1.0 的直接桌面子窗口方案在实机反馈中出现不可见及桌面图标显示异常，因此 0.1.1 已移除该路径。不能用早期的 HWND 检查结果代替实际显示验收。

透明及穿透方案参考 [FennecMomo/DesktopTools](https://github.com/FennecMomo/DesktopTools) 的 `desktoptools/native.py` 与 `overlay_window.pyw`（参考提交 `fd2f56a`）：分层透明和 `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`，在本项目通过 WPF `AllowsTransparency` 提供每像素 alpha。没有直接把 Tk 的颜色键逻辑套到 WPF；保留快捷方式可点击与可从托盘恢复的行为。

版本：0.1.2。

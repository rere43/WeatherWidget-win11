# 任务栏嵌入技术方案演进报告

## 1. 核心问题
在开发 Windows 任务栏嵌入式小组件时，面临的主要技术挑战是 **"保活" (Keep-Alive)**：
- **Explorer 干扰**：当用户悬停任务栏图标触发缩略图预览，或使用 Win+Tab 切换视图时，Windows Explorer 会主动调整窗口层级 (Z-Order) 或隐藏非核心子窗口，导致小组件闪烁或消失。
- **层级冲突**：为了防止被隐藏，通常采用 "TopMost" (置顶) 策略，但这会导致小组件遮挡任务栏右键菜单、通知中心等系统 UI，严重影响体验。
- **Win11 架构变化**：Win11 任务栏重构为 XAML Island 架构 (`DesktopWindowContentBridge`)，传统的 Win32 挂载方式失效或不稳。

## 2. 方案演进历程

### 第一代：TopMost 暴力保活
- **策略**：将窗口设为 `WS_EX_TOPMOST`，并使用高频定时器 (200ms) 强制 `SetWindowPos` 提层。
- **缺陷**：
  - 缩略图预览时仍会闪烁（与 Explorer 抢夺层级）。
  - 严重遮挡右键菜单和系统托盘弹窗。
  - CPU 占用偏高。

### 第二代：智能避让 (Smart Avoidance)
- **策略**：增加复杂的检测逻辑，检测到右键菜单 (`#32768`) 或全屏应用时暂停保活。
- **缺陷**：
  - 逻辑极其复杂，边界情况难以覆盖完全。
  - 检测存在延迟，偶尔仍会发生遮挡或闪烁。
  - 在 Win11 上表现不稳定。

### 第三代（最终方案）：SetParent 子窗口挂载
- **策略**：放弃与 Explorer 对抗，转为"融入"系统。
- **技术核心**：**SetParent Pattern**
  1. 创建一个标准的 `WS_POPUP` 层状窗口 (Layered Window)。
  2. 使用 `SetWindowLong` 动态修改样式，去除 `WS_POPUP`，添加 `WS_CHILD`。
  3. 调用 `SetParent` 将窗口强制挂载到任务栏主窗口 (`Shell_TrayWnd`) 下。
- **优势**：
  - **稳定性**：成为任务栏的子窗口后，Z-Order 由系统统一管理，缩略图预览时不会被隐藏。
  - **交互性**：自然处于系统菜单之下，**彻底解决遮挡右键菜单问题**。
  - **性能**：无需高频轮询保活，仅需低频检查位置偏移。
  - **兼容性**：经实测，该方案同时兼容 Win10 (`ReBarWindow32`) 和 Win11 (`XAML Bridge`) 架构。

## 3. 关键技术细节

### 3.1 窗口创建与挂载
直接创建 Child 窗口在 `UpdateLayeredWindow` 绘图时会受限，因此采用 "先 Popup 后 Child" 策略：

```csharp
// 1. 初始创建为 Popup
var hwnd = CreateWindowEx(..., WS_POPUP | WS_VISIBLE, ...);

// 2. 动态转为 Child
var style = GetWindowLong(hwnd, GWL_STYLE);
SetWindowLong(hwnd, GWL_STYLE, (style & ~WS_POPUP) | WS_CHILD);

// 3. 挂载到任务栏
var taskbar = FindWindow("Shell_TrayWnd", null);
SetParent(hwnd, taskbar);
```

### 3.2 坐标定位
放弃依赖特定的子窗口（如 `ReBarWindow32`，因为 Win11 已移除），改用通用算法：
1. 获取任务栏主窗口 `Shell_TrayWnd` 的 ClientRect。
2. 查找通知区域 `TrayNotifyWnd`，获取其屏幕坐标。
3. 将通知区坐标转换为任务栏客户区坐标。
4. 小组件定位在 **通知区左侧** (NotifyX - WidgetWidth - Padding)。

### 3.3 Explorer 重启恢复
Explorer.exe 崩溃或重启会导致窗口句柄失效。通过监听系统消息自动恢复：

```csharp
// 注册消息
_msgTaskbarCreated = RegisterWindowMessage("TaskbarCreated");

// 窗口过程中处理
if (msg == _msgTaskbarCreated) {
    // 重新查找句柄并挂载
    ReattachToTaskbar();
}
```

## 4. 实验验证
开发了专用实验工具 `TaskbarEmbedExperiment`，并行验证了 7 种挂载策略。结果表明：
- ❌ 直接挂载到 `TrayNotifyWnd` 会在预览时被系统隐藏。
- ✅ 挂载到 `Shell_TrayWnd` (Win10/11) 均稳定可见且不被裁剪。

## 5. 结论
新方案实现了 **"零闪烁、零遮挡、低功耗"** 的原生级嵌入体验，是目前最优的技术路径。

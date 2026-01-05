# settings.json 配置项（SchemaVersion=2）

配置文件路径：`%LOCALAPPDATA%\\WeatherWidget\\settings.json`

程序会在启动/保存时自动归一化配置：缺失字段补默认值、数值做范围裁剪。

## 顶层（Settings）

- `SchemaVersion`：配置结构版本号（当前为 `2`）
- `City`：城市名（用于地理编码/展示）
- `Latitude` / `Longitude`：坐标（用于 Open-Meteo 查询）
- `RefreshInterval`：刷新间隔（TimeSpan 字符串，例如 `00:10:00`）
- `ThemeMode`：面板主题（`0..18`，见 `WeatherWidget.App/Models/Settings.cs`）
- `AutoStart`：开机启动
- `StartHidden`：启动时默认隐藏面板
- `Embedded`：任务栏嵌入显示配置（见下）

## 嵌入显示（Embedded）

布局：
- `LineSpacing`：温度/湿度两行间距（`0..40`）
- `UvToIconGap`：UV 条与天气图标间距（`2..40`）
- `IconToTextGap`：天气图标与文字区域间距（`2..40`）
- `OffsetX`：整体水平偏移（`-300..300`）
- `IconScale`：天气图标缩放（`0.5..1.6`）

天气图标：
- `WeatherIconBackgroundEnabled`：图标圆角背景开关
- `WeatherIconOffsetX` / `WeatherIconOffsetY`：图标偏移（渲染内部会按图标尺寸缩放）

交互：
- `HoverDelayMs`：悬停触发面板延迟（`0..5000`）
- `HoverPinMs`：悬停打开后固定时间（`0..5000`）

字体与格式：
- `FontFamily`：字体（例如 `Segoe UI`）
- `TemperatureFontScale`：温度字号倍率（`0.5..3.0`）
- `HumidityFontScale`：湿度字号倍率（`0.5..3.0`）
- `UvNumberFontScale`：UV 数字号倍率（`0.5..6.0`）
- `TextStrokeWidth`：描边宽度（`0..8.0`）
- `TemperatureFormat` / `HumidityFormat` / `UvNumberFormat`：格式字符串，支持占位符 `{value}`

颜色：
- `TemperatureColor` / `HumidityColor` / `UvNumberColor`
- `UvBarFillColor` / `UvBarBackgroundColor`

颜色格式支持 `#RRGGBB` 或 `#AARRGGBB`（也可用 WPF 支持的命名色）。

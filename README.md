# 天气小组件（Win11 任务栏嵌入）

这是一个 WPF 小应用：在 Win11 任务栏中嵌入显示 **UV 条+数字、天气图标、气温、湿度**；悬停可打开小面板查看更多天气信息（未来 5 天 + 穿衣建议）。

## 运行

在仓库根目录：

```powershell
dotnet build .\WeatherWidget.sln
dotnet run --project .\WeatherWidget.App\WeatherWidget.App.csproj
```

运行后把该应用固定到任务栏即可。

## 配置

面板内有“设置”折叠区（仅针对任务栏嵌入组件）：
- 基础：城市、刷新频率、开机启动、启动时默认隐藏
- 布局：图标大小、左右偏移、UV-图标间距、图标-文字间距、行间距
- 交互：悬停延迟、悬停固定时间
- 样式：字体、温度/湿度/UV 字号、描边、格式、颜色、UV 条颜色

`settings.json` 字段说明见：`docs/settings.md`

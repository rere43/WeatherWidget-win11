# 天气小组件（Win11 任务栏图标）

这是一个 WPF 小应用：**固定到 Win11 任务栏后**，图标右上角显示温度（可选右下角 UV/湿度），点击图标弹出小面板（未来 5 天 + 穿衣建议）。

## 运行

在仓库根目录：

```powershell
dotnet build .\WeatherWidget.sln
dotnet run --project .\WeatherWidget.App\WeatherWidget.App.csproj
```

运行后把该应用固定到任务栏即可。

## 配置

面板内有“设置”折叠区：
- 城市名（仅用于显示）
- 经度/纬度（用于 Open-Meteo 查询）
- 图标角标：关闭 / UV / 湿度

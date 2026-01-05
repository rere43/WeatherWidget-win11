#pragma once

#ifdef TASKBAREMBED_EXPORTS
#define TASKBAREMBED_API __declspec(dllexport)
#else
#define TASKBAREMBED_API __declspec(dllimport)
#endif

extern "C" {
    // 创建嵌入窗口，返回窗口句柄，失败返回 NULL
    TASKBAREMBED_API HWND TaskbarEmbed_Create(int width, int height);

    // 销毁嵌入窗口
    TASKBAREMBED_API void TaskbarEmbed_Destroy(HWND hwnd);

    // 更新显示内容（传入 BGRA 位图数据）
    TASKBAREMBED_API BOOL TaskbarEmbed_UpdateBitmap(HWND hwnd, const void* bitmapData, int width, int height);

    // 设置窗口位置（相对于任务栏）
    TASKBAREMBED_API BOOL TaskbarEmbed_SetPosition(HWND hwnd, int x, int y);

    // 获取任务栏信息
    TASKBAREMBED_API BOOL TaskbarEmbed_GetTaskbarInfo(int* outWidth, int* outHeight, int* outLeft, int* outTop);

    // 检测是否为 Win11 任务栏
    TASKBAREMBED_API BOOL TaskbarEmbed_IsWindows11Taskbar();

    // 动态调整窗口位置（根据当前通知区位置重新计算，offsetX 为用户设置的左右偏移）
    TASKBAREMBED_API BOOL TaskbarEmbed_AdjustPosition(HWND hwnd, int width, int height, int offsetX);

    // 强制窗口置顶（用于对抗预览窗口遮挡）
    TASKBAREMBED_API void TaskbarEmbed_BringToTop(HWND hwnd);
}

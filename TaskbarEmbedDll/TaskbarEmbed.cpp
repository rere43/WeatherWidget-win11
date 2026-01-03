#define WIN32_LEAN_AND_MEAN
#define TASKBAREMBED_EXPORTS
#include <windows.h>
#include <string>
#include "TaskbarEmbed.h"

// 全局变量
static HINSTANCE g_hInstance = NULL;
static const wchar_t* WINDOW_CLASS_NAME = L"WeatherTaskbarEmbed";
static bool g_classRegistered = false;

// 窗口数据结构
struct EmbedWindowData {
    HWND hwnd;
    HWND taskbarHwnd;
    HBITMAP hBitmap;
    int bitmapWidth;
    int bitmapHeight;
    void* bitmapBits;
};

// 前向声明
static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);
static HWND FindTaskbarParent();
static bool IsWindows11();

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    switch (ul_reason_for_call) {
    case DLL_PROCESS_ATTACH:
        g_hInstance = hModule;
        DisableThreadLibraryCalls(hModule);
        break;
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

static bool RegisterWindowClass() {
    if (g_classRegistered) return true;

    WNDCLASSEXW wc = { 0 };
    wc.cbSize = sizeof(WNDCLASSEXW);
    wc.style = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc = WndProc;
    wc.cbClsExtra = 0;
    wc.cbWndExtra = sizeof(void*);
    wc.hInstance = g_hInstance;
    wc.hIcon = NULL;
    wc.hCursor = LoadCursor(NULL, IDC_ARROW);
    wc.hbrBackground = NULL;  // 透明背景
    wc.lpszMenuName = NULL;
    wc.lpszClassName = WINDOW_CLASS_NAME;
    wc.hIconSm = NULL;

    if (RegisterClassExW(&wc) == 0) {
        DWORD err = GetLastError();
        if (err != ERROR_CLASS_ALREADY_EXISTS) {
            return false;
        }
    }

    g_classRegistered = true;
    return true;
}

static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    EmbedWindowData* data = (EmbedWindowData*)GetWindowLongPtrW(hwnd, GWLP_USERDATA);

    switch (msg) {
    case WM_CREATE:
        return 0;

    case WM_PAINT: {
        // 对于 LAYERED 窗口，WM_PAINT 不再使用
        // 绘制通过 UpdateLayeredWindow 完成
        PAINTSTRUCT ps;
        BeginPaint(hwnd, &ps);
        EndPaint(hwnd, &ps);
        return 0;
    }

    case WM_ERASEBKGND:
        return 1;  // 不擦除背景

    case WM_DESTROY:
        if (data) {
            if (data->hBitmap) {
                DeleteObject(data->hBitmap);
            }
            delete data;
            SetWindowLongPtrW(hwnd, GWLP_USERDATA, 0);
        }
        return 0;

    case WM_LBUTTONDOWN:
    case WM_RBUTTONDOWN:
    case WM_LBUTTONUP:
    case WM_RBUTTONUP:
        // 忽略点击，防止窗口被隐藏
        return 0;

    case WM_MOUSEACTIVATE:
        // 防止激活窗口
        return MA_NOACTIVATE;

    case WM_NCHITTEST:
        // 让鼠标穿透
        return HTTRANSPARENT;
    }

    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

static HWND FindTaskbarParent() {
    // 查找主任务栏
    HWND taskbar = FindWindowW(L"Shell_TrayWnd", NULL);
    if (!taskbar) return NULL;

    // 尝试多种父窗口，按优先级
    // 1. ReBarWindow32 内的 MSTaskSwWClass
    HWND rebar = FindWindowExW(taskbar, NULL, L"ReBarWindow32", NULL);
    if (rebar) {
        HWND taskSw = FindWindowExW(rebar, NULL, L"MSTaskSwWClass", NULL);
        if (taskSw) {
            return taskSw;
        }
        // 2. ReBarWindow32 本身
        return rebar;
    }

    // 3. 直接使用 Shell_TrayWnd（Win11 主要使用这个）
    return taskbar;
}

static bool IsWindows11() {
    HWND taskbar = FindWindowW(L"Shell_TrayWnd", NULL);
    if (!taskbar) return false;

    // Win11 特征：存在 Windows.UI.Composition.DesktopWindowContentBridge 子窗口
    HWND child = FindWindowExW(taskbar, NULL, L"Windows.UI.Composition.DesktopWindowContentBridge", NULL);
    return (child != NULL);
}

// ============ 导出函数 ============

TASKBAREMBED_API HWND TaskbarEmbed_Create(int width, int height) {
    if (!RegisterWindowClass()) {
        OutputDebugStringW(L"TaskbarEmbed: RegisterWindowClass failed\n");
        return NULL;
    }

    // 查找任务栏
    HWND taskbar = FindWindowW(L"Shell_TrayWnd", NULL);
    if (!taskbar) {
        OutputDebugStringW(L"TaskbarEmbed: Shell_TrayWnd not found\n");
        return NULL;
    }

    // 获取任务栏屏幕位置
    RECT taskbarScreenRect;
    if (!GetWindowRect(taskbar, &taskbarScreenRect)) {
        OutputDebugStringW(L"TaskbarEmbed: GetWindowRect failed\n");
        return NULL;
    }

    int taskbarHeight = taskbarScreenRect.bottom - taskbarScreenRect.top;

    wchar_t dbg[256];
    swprintf_s(dbg, L"TaskbarEmbed: Taskbar screen rect=(%d,%d)-(%d,%d), height=%d\n",
        taskbarScreenRect.left, taskbarScreenRect.top,
        taskbarScreenRect.right, taskbarScreenRect.bottom, taskbarHeight);
    OutputDebugStringW(dbg);

    // 尝试获取通知区域位置
    // Win10: TrayNotifyWnd, Win11: 可能没有或位置不同
    HWND trayNotify = FindWindowExW(taskbar, NULL, L"TrayNotifyWnd", NULL);
    int notifyLeft = taskbarScreenRect.right - 400;  // 默认值

    if (trayNotify) {
        RECT trayRect;
        if (GetWindowRect(trayNotify, &trayRect)) {
            notifyLeft = trayRect.left;
            swprintf_s(dbg, L"TaskbarEmbed: TrayNotifyWnd at (%d,%d)-(%d,%d)\n",
                trayRect.left, trayRect.top, trayRect.right, trayRect.bottom);
            OutputDebugStringW(dbg);
        }
    }
    else {
        // Win11: 尝试找系统托盘时钟区域
        HWND clockWnd = FindWindowExW(taskbar, NULL, L"ClockButton", NULL);
        if (!clockWnd) {
            // 递归查找
            HWND child = GetWindow(taskbar, GW_CHILD);
            while (child) {
                wchar_t className[256];
                GetClassNameW(child, className, 256);
                if (wcscmp(className, L"Windows.UI.Composition.DesktopWindowContentBridge") == 0) {
                    // Win11 XAML 区域，通知区大约在右边 1/4
                    RECT childRect;
                    if (GetWindowRect(child, &childRect)) {
                        notifyLeft = childRect.right - (childRect.right - childRect.left) / 4;
                    }
                    break;
                }
                child = GetWindow(child, GW_HWNDNEXT);
            }
        }
    }

    // 计算屏幕坐标（放在通知区左边）
    int screenX = notifyLeft - width - 10;
    int screenY = taskbarScreenRect.top + (taskbarHeight - height) / 2;

    // 确保不超出任务栏左边界
    if (screenX < taskbarScreenRect.left + 100) {
        screenX = taskbarScreenRect.left + 100;
    }

    swprintf_s(dbg, L"TaskbarEmbed: Creating popup at screen (%d,%d), size=%dx%d\n", screenX, screenY, width, height);
    OutputDebugStringW(dbg);

    // 创建独立置顶窗口（使用 LAYERED 支持透明）
    HWND hwnd = CreateWindowExW(
        WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TRANSPARENT,
        WINDOW_CLASS_NAME,
        L"WeatherEmbed",
        WS_POPUP | WS_VISIBLE,
        screenX, screenY, width, height,
        NULL,
        NULL,
        g_hInstance,
        NULL
    );

    if (!hwnd) {
        DWORD err = GetLastError();
        swprintf_s(dbg, L"TaskbarEmbed: CreateWindowExW failed, error=%lu\n", err);
        OutputDebugStringW(dbg);
        return NULL;
    }

    swprintf_s(dbg, L"TaskbarEmbed: Created hwnd=0x%p\n", hwnd);
    OutputDebugStringW(dbg);

    // 创建窗口数据
    EmbedWindowData* data = new EmbedWindowData();
    data->hwnd = hwnd;
    data->taskbarHwnd = taskbar;  // 保存任务栏句柄用于后续定位
    data->hBitmap = NULL;
    data->bitmapWidth = 0;
    data->bitmapHeight = 0;
    data->bitmapBits = NULL;

    SetWindowLongPtrW(hwnd, GWLP_USERDATA, (LONG_PTR)data);

    // 强制重绘
    InvalidateRect(hwnd, NULL, TRUE);
    UpdateWindow(hwnd);

    return hwnd;
}

TASKBAREMBED_API void TaskbarEmbed_Destroy(HWND hwnd) {
    if (hwnd && IsWindow(hwnd)) {
        DestroyWindow(hwnd);
    }
}

TASKBAREMBED_API BOOL TaskbarEmbed_UpdateBitmap(HWND hwnd, const void* bitmapData, int width, int height) {
    if (!hwnd || !IsWindow(hwnd) || !bitmapData) {
        return FALSE;
    }

    EmbedWindowData* data = (EmbedWindowData*)GetWindowLongPtrW(hwnd, GWLP_USERDATA);
    if (!data) {
        return FALSE;
    }

    // 删除旧位图
    if (data->hBitmap) {
        DeleteObject(data->hBitmap);
        data->hBitmap = NULL;
    }

    // 创建 DIB Section（32位 BGRA，支持 alpha）
    BITMAPINFO bmi = { 0 };
    bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth = width;
    bmi.bmiHeader.biHeight = -height;  // Top-down
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 32;
    bmi.bmiHeader.biCompression = BI_RGB;

    HDC hdcScreen = GetDC(NULL);
    data->hBitmap = CreateDIBSection(hdcScreen, &bmi, DIB_RGB_COLORS, &data->bitmapBits, NULL, 0);

    if (!data->hBitmap) {
        ReleaseDC(NULL, hdcScreen);
        return FALSE;
    }

    // 复制位图数据
    memcpy(data->bitmapBits, bitmapData, width * height * 4);
    data->bitmapWidth = width;
    data->bitmapHeight = height;

    // 使用 UpdateLayeredWindow 绘制（支持 per-pixel alpha）
    HDC hdcMem = CreateCompatibleDC(hdcScreen);
    HBITMAP oldBmp = (HBITMAP)SelectObject(hdcMem, data->hBitmap);

    POINT ptSrc = { 0, 0 };
    SIZE sizeWnd = { width, height };
    BLENDFUNCTION blend = { AC_SRC_OVER, 0, 255, AC_SRC_ALPHA };

    BOOL result = UpdateLayeredWindow(hwnd, hdcScreen, NULL, &sizeWnd, hdcMem, &ptSrc, 0, &blend, ULW_ALPHA);

    SelectObject(hdcMem, oldBmp);
    DeleteDC(hdcMem);
    ReleaseDC(NULL, hdcScreen);

    return result;
}

TASKBAREMBED_API BOOL TaskbarEmbed_SetPosition(HWND hwnd, int x, int y) {
    if (!hwnd || !IsWindow(hwnd)) {
        return FALSE;
    }

    return SetWindowPos(hwnd, NULL, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
}

TASKBAREMBED_API BOOL TaskbarEmbed_GetTaskbarInfo(int* outWidth, int* outHeight, int* outLeft, int* outTop) {
    HWND taskbar = FindWindowW(L"Shell_TrayWnd", NULL);
    if (!taskbar) {
        return FALSE;
    }

    RECT rc;
    if (!GetWindowRect(taskbar, &rc)) {
        return FALSE;
    }

    if (outWidth) *outWidth = rc.right - rc.left;
    if (outHeight) *outHeight = rc.bottom - rc.top;
    if (outLeft) *outLeft = rc.left;
    if (outTop) *outTop = rc.top;

    return TRUE;
}

TASKBAREMBED_API BOOL TaskbarEmbed_IsWindows11Taskbar() {
    return IsWindows11() ? TRUE : FALSE;
}

// 计算通知区左边缘位置
static int GetNotifyAreaLeft(HWND taskbar, const RECT& taskbarRect) {
    int notifyLeft = taskbarRect.right - 400;  // 默认值

    // Win10: TrayNotifyWnd
    HWND trayNotify = FindWindowExW(taskbar, NULL, L"TrayNotifyWnd", NULL);
    if (trayNotify) {
        RECT trayRect;
        if (GetWindowRect(trayNotify, &trayRect)) {
            notifyLeft = trayRect.left;
        }
        return notifyLeft;
    }

    // Win11: 尝试多种方式获取通知区位置
    // 方法1: 查找 SystemTrayNotifyWindow (Win11 22H2+)
    HWND sysTray = FindWindowExW(taskbar, NULL, L"Windows.UI.Composition.DesktopWindowContentBridge", NULL);
    if (sysTray) {
        // Win11 的任务栏结构不同，通知区在右侧
        // 通过 TrayNotifyWnd 的替代方案
        RECT sysTrayRect;
        if (GetWindowRect(sysTray, &sysTrayRect)) {
            // Win11 任务栏：通知区大约占右侧 300-400 像素
            // 根据任务栏宽度动态计算
            int taskbarWidth = taskbarRect.right - taskbarRect.left;
            notifyLeft = taskbarRect.right - (int)(taskbarWidth * 0.15);  // 约 15% 给通知区
            if (notifyLeft < taskbarRect.left + taskbarWidth / 2) {
                notifyLeft = taskbarRect.right - 350;  // 保底值
            }
        }
    }

    return notifyLeft;
}

TASKBAREMBED_API BOOL TaskbarEmbed_AdjustPosition(HWND hwnd, int width, int height) {
    if (!hwnd || !IsWindow(hwnd)) {
        return FALSE;
    }

    // 查找任务栏
    HWND taskbar = FindWindowW(L"Shell_TrayWnd", NULL);
    if (!taskbar) {
        return FALSE;
    }

    // 获取任务栏屏幕位置
    RECT taskbarRect;
    if (!GetWindowRect(taskbar, &taskbarRect)) {
        return FALSE;
    }

    int taskbarHeight = taskbarRect.bottom - taskbarRect.top;

    // 获取通知区左边缘
    int notifyLeft = GetNotifyAreaLeft(taskbar, taskbarRect);

    // 计算新位置（通知区左边）
    int screenX = notifyLeft - width - 10;
    int screenY = taskbarRect.top + (taskbarHeight - height) / 2;

    // 确保不超出任务栏左边界
    if (screenX < taskbarRect.left + 100) {
        screenX = taskbarRect.left + 100;
    }

    // 获取当前窗口位置
    RECT currentRect;
    if (GetWindowRect(hwnd, &currentRect)) {
        // 只有位置变化时才更新
        if (currentRect.left != screenX || currentRect.top != screenY) {
            wchar_t dbg[256];
            swprintf_s(dbg, L"TaskbarEmbed: Adjusting position from (%d,%d) to (%d,%d)\n",
                currentRect.left, currentRect.top, screenX, screenY);
            OutputDebugStringW(dbg);

            SetWindowPos(hwnd, HWND_TOPMOST, screenX, screenY, 0, 0,
                SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
    }

    return TRUE;
}

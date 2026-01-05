using System.Runtime.InteropServices;
using System.Windows;

namespace WeatherWidget.App.Services;

public static class TaskbarAnchor
{
    private const int ABM_GETTASKBARPOS = 0x00000005;

    private enum ABE : uint
    {
        Left = 0,
        Top = 1,
        Right = 2,
        Bottom = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public ABE uEdge;
        public RECT rc;
        public int lParam;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    public static (double Left, double Top) GetTaskbarAnchor(double panelWidthDip, double panelHeightDip, double dpiScaleX, double dpiScaleY)
    {
        dpiScaleX = dpiScaleX <= 0 ? 1 : dpiScaleX;
        dpiScaleY = dpiScaleY <= 0 ? 1 : dpiScaleY;

        var data = new APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
        };

        var result = SHAppBarMessage(ABM_GETTASKBARPOS, ref data);
        if (result == 0)
        {
            return ClampToWorkArea(Left: 20, Top: 20, panelWidthDip, panelHeightDip);
        }

        var rc = data.rc;

        const double marginDip = 12;
        var marginPxX = marginDip * dpiScaleX;
        var marginPxY = marginDip * dpiScaleY;
        var panelWidthPx = panelWidthDip * dpiScaleX;
        var panelHeightPx = panelHeightDip * dpiScaleY;

        (double leftPx, double topPx) = data.uEdge switch
        {
            ABE.Bottom => (leftPx: rc.right - panelWidthPx - marginPxX, topPx: rc.top - panelHeightPx - marginPxY),
            ABE.Top => (leftPx: rc.right - panelWidthPx - marginPxX, topPx: rc.bottom + marginPxY),
            ABE.Left => (leftPx: rc.right + marginPxX, topPx: rc.bottom - panelHeightPx - marginPxY),
            ABE.Right => (leftPx: rc.left - panelWidthPx - marginPxX, topPx: rc.bottom - panelHeightPx - marginPxY),
            _ => (leftPx: 20 * dpiScaleX, topPx: 20 * dpiScaleY),
        };

        var leftDip = leftPx / dpiScaleX;
        var topDip = topPx / dpiScaleY;
        return ClampToWorkArea(leftDip, topDip, panelWidthDip, panelHeightDip);
    }

    public static (double Left, double Top) GetTaskbarAnchorNearPointPx(
        double panelWidthDip,
        double panelHeightDip,
        double dpiScaleX,
        double dpiScaleY,
        double anchorPxX,
        double anchorPxY)
    {
        dpiScaleX = dpiScaleX <= 0 ? 1 : dpiScaleX;
        dpiScaleY = dpiScaleY <= 0 ? 1 : dpiScaleY;

        var data = new APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
        };

        const double marginDip = 12;
        var marginPxX = marginDip * dpiScaleX;
        var marginPxY = marginDip * dpiScaleY;
        var panelWidthPx = panelWidthDip * dpiScaleX;
        var panelHeightPx = panelHeightDip * dpiScaleY;

        var result = SHAppBarMessage(ABM_GETTASKBARPOS, ref data);
        if (result == 0)
        {
            var leftFallback = anchorPxX / dpiScaleX - panelWidthDip / 2;
            var topFallback = anchorPxY / dpiScaleY - panelHeightDip / 2;
            return ClampToWorkArea(leftFallback, topFallback, panelWidthDip, panelHeightDip);
        }

        var rc = data.rc;

        (double leftPx, double topPx) = data.uEdge switch
        {
            ABE.Bottom => (leftPx: anchorPxX - panelWidthPx / 2, topPx: rc.top - panelHeightPx - marginPxY),
            ABE.Top => (leftPx: anchorPxX - panelWidthPx / 2, topPx: rc.bottom + marginPxY),
            ABE.Left => (leftPx: rc.right + marginPxX, topPx: anchorPxY - panelHeightPx / 2),
            ABE.Right => (leftPx: rc.left - panelWidthPx - marginPxX, topPx: anchorPxY - panelHeightPx / 2),
            _ => (leftPx: anchorPxX - panelWidthPx / 2, topPx: anchorPxY - panelHeightPx / 2),
        };

        var leftDip = leftPx / dpiScaleX;
        var topDip = topPx / dpiScaleY;
        return ClampToWorkArea(leftDip, topDip, panelWidthDip, panelHeightDip);
    }

    private static (double Left, double Top) ClampToWorkArea(double Left, double Top, double panelWidthDip, double panelHeightDip)
    {
        const double marginDip = 12;
        var wa = SystemParameters.WorkArea;

        var minLeft = wa.Left + marginDip;
        var maxLeft = wa.Right - panelWidthDip - marginDip;
        var minTop = wa.Top + marginDip;
        var maxTop = wa.Bottom - panelHeightDip - marginDip;

        // 当窗口尺寸大于工作区时，避免 Math.Clamp(min > max) 抛异常，直接贴边显示。
        if (maxLeft < minLeft)
        {
            maxLeft = minLeft;
        }

        if (maxTop < minTop)
        {
            maxTop = minTop;
        }

        return (
            Left: Math.Clamp(Left, minLeft, maxLeft),
            Top: Math.Clamp(Top, minTop, maxTop));
    }
}

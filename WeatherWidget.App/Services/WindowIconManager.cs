using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WeatherWidget.App.Services;

public sealed class WindowIconManager : IDisposable
{
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;

    private IntPtr _hwnd;
    private IntPtr _small;
    private IntPtr _big;

    public void Attach(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    public void Update(ImageSource bigIcon, ImageSource smallIcon)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var big = bigIcon as BitmapSource;
        var small = smallIcon as BitmapSource;
        if (big is null || small is null)
        {
            return;
        }

        var newBig = IntPtr.Zero;
        var newSmall = IntPtr.Zero;

        try
        {
            newBig = CreateIconFromBitmapSource(big);
            newSmall = CreateIconFromBitmapSource(small);

            if (newBig == IntPtr.Zero || newSmall == IntPtr.Zero)
            {
                return;
            }

            // Set icons (do not destroy returned previous handles; we only manage the ones we created)
            _ = SendMessage(_hwnd, WM_SETICON, (IntPtr)ICON_BIG, newBig);
            _ = SendMessage(_hwnd, WM_SETICON, (IntPtr)ICON_SMALL, newSmall);

            // Replace managed handles after successful set
            if (_big != IntPtr.Zero)
            {
                _ = DestroyIcon(_big);
            }

            if (_small != IntPtr.Zero)
            {
                _ = DestroyIcon(_small);
            }

            _big = newBig;
            _small = newSmall;
            newBig = IntPtr.Zero;
            newSmall = IntPtr.Zero;
        }
        finally
        {
            if (newBig != IntPtr.Zero)
            {
                _ = DestroyIcon(newBig);
            }

            if (newSmall != IntPtr.Zero)
            {
                _ = DestroyIcon(newSmall);
            }
        }
    }

    public void Dispose()
    {
        if (_big != IntPtr.Zero)
        {
            _ = DestroyIcon(_big);
            _big = IntPtr.Zero;
        }

        if (_small != IntPtr.Zero)
        {
            _ = DestroyIcon(_small);
            _small = IntPtr.Zero;
        }
    }

    public static IntPtr CreateBitmapHandle(BitmapSource source)
    {
        var formatted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var width = formatted.PixelWidth;
        var height = formatted.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return IntPtr.Zero;
        }

        var stride = width * 4;
        var pixels = new byte[height * stride];
        formatted.CopyPixels(pixels, stride, 0);

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down DIB
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
                biSizeImage = (uint)pixels.Length,
            },
        };

        var hColor = CreateDIBSection(IntPtr.Zero, ref bmi, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
        if (hColor == IntPtr.Zero || bits == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        Marshal.Copy(pixels, 0, bits, pixels.Length);
        return hColor;
    }

    public static void DestroyBitmapHandle(IntPtr hBitmap)
    {
        if (hBitmap != IntPtr.Zero)
        {
            _ = DeleteObject(hBitmap);
        }
    }

    private static IntPtr CreateIconFromBitmapSource(BitmapSource source)
    {
        var formatted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var width = formatted.PixelWidth;
        var height = formatted.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return IntPtr.Zero;
        }

        var stride = width * 4;
        var pixels = new byte[height * stride];
        formatted.CopyPixels(pixels, stride, 0);

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down DIB
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
                biSizeImage = (uint)pixels.Length,
            },
        };

        var hColor = CreateDIBSection(IntPtr.Zero, ref bmi, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
        if (hColor == IntPtr.Zero || bits == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        // AND mask (1 = transparent). Keep only fully transparent pixels as mask=1.
        // This avoids black corners on rounded icons if the shell doesn't fully respect alpha in some cases.
        var maskStride = ((width + 31) / 32) * 4;
        var mask = new byte[maskStride * height];
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * stride;
            var maskRowOffset = y * maskStride;
            for (var x = 0; x < width; x++)
            {
                var a = pixels[rowOffset + (x * 4) + 3];
                if (a != 0)
                {
                    continue;
                }

                var byteIndex = maskRowOffset + (x / 8);
                var bit = 7 - (x % 8);
                mask[byteIndex] |= (byte)(1 << bit);
            }
        }
        var maskHandle = GCHandle.Alloc(mask, GCHandleType.Pinned);
        IntPtr hMask = IntPtr.Zero;
        try
        {
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            hMask = CreateBitmap(width, height, 1, 1, maskHandle.AddrOfPinnedObject());
            if (hMask == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var iconInfo = new ICONINFO
            {
                fIcon = true,
                xHotspot = 0,
                yHotspot = 0,
                hbmMask = hMask,
                hbmColor = hColor,
            };

            var hIcon = CreateIconIndirect(ref iconInfo);
            return hIcon;
        }
        finally
        {
            maskHandle.Free();
            _ = DeleteObject(hColor);
            if (hMask != IntPtr.Zero)
            {
                _ = DeleteObject(hMask);
            }
        }
    }

    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO icon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc,
        [In] ref BITMAPINFO pbmi,
        uint iUsage,
        out IntPtr ppvBits,
        IntPtr hSection,
        uint dwOffset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint cPlanes, uint cBitsPerPel, IntPtr lpvBits);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);
}

using System;
using System.Runtime.InteropServices;

namespace KaihenShelf
{
    // Extension association only: never ask a shell thumbnail handler to read the user's file.
    public static class ShellFileIconReader
    {
        public const int Size = 32;
        public static byte[] Read(string extension)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return null;
            if (extension == null || extension.Length > 64 || extension.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0) return null;
            int com = CoInitializeEx(IntPtr.Zero, 0);
            if (com < 0 && com != unchecked((int)0x80010106)) return null;
            var info = new ShellInfo();
            try
            {
                if (SHGetFileInfo("file" + extension, 0x80, ref info, (uint)Marshal.SizeOf<ShellInfo>(), 0x110) == IntPtr.Zero || info.icon == IntPtr.Zero) return null;
                var black = Render(info.icon, 0);
                var white = Render(info.icon, 255);
                if (black == null || white == null) return null;
                var rgba = new byte[Size * Size * 4];
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        int src = (y * Size + x) * 4, dst = ((Size - 1 - y) * Size + x) * 4;
                        // Recover straight alpha from black/white compositing, including legacy masks.
                        int alpha = Math.Max(0, Math.Min(255, 255 - (white[src] - black[src])));
                        rgba[dst + 3] = (byte)alpha;
                        if (alpha == 0) continue;
                        for (int c = 0; c < 3; c++) rgba[dst + c] = (byte)Math.Min(255, (black[src + 2 - c] * 255 + alpha / 2) / alpha);
                    }
                return rgba;
            }
            finally
            {
                if (info.icon != IntPtr.Zero) DestroyIcon(info.icon);
                if (com >= 0) CoUninitialize();
            }
        }
        static byte[] Render(IntPtr icon, byte background)
        {
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero), bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            if (dc == IntPtr.Zero) return null;
            try
            {
                var header = new BitmapHeader { size = 40, width = Size, height = -Size, planes = 1, bits = 32 };
                bitmap = CreateDIBSection(dc, ref header, 0, out IntPtr pixels, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero || pixels == IntPtr.Zero) return null;
                previous = SelectObject(dc, bitmap);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1)) return null;
                var bytes = new byte[Size * Size * 4];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = background;
                Marshal.Copy(bytes, 0, pixels, bytes.Length);
                if (!DrawIconEx(dc, 0, 0, icon, Size, Size, 0, IntPtr.Zero, 3)) return null;
                GdiFlush();
                Marshal.Copy(pixels, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(dc, previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                DeleteDC(dc);
            }
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct ShellInfo
        {
            public IntPtr icon; public int index; public uint attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string displayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string typeName;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct BitmapHeader
        {
            public uint size; public int width, height; public ushort planes, bits;
            public uint compression, imageSize; public int xppm, yppm; public uint used, important;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")] static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShellInfo info, uint size, uint flags);
        [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved, uint mode);
        [DllImport("ole32.dll")] static extern void CoUninitialize();
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, uint step, IntPtr brush, uint flags);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapHeader info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool GdiFlush();
    }
}

using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Sidebar;

internal static class ShortcutIcons
{
    private static readonly SemaphoreSlim DecodeSlots = new(2);
    public static async Task<string> GetEmbeddedAsync(string target)
    {
        await DecodeSlots.WaitAsync();
        try { return await GetEmbeddedCoreAsync(target); }
        finally { DecodeSlots.Release(); }
    }
    private static async Task<string> GetEmbeddedCoreAsync(string target)
    {
        string bitmapData = await GetBitmapAsync(target);
        if (string.IsNullOrEmpty(bitmapData)) return "";
        byte[] bitmap = Convert.FromBase64String(bitmapData);
        int width = BitConverter.ToInt32(bitmap, 18);
        int height = Math.Abs(BitConverter.ToInt32(bitmap, 22));
        byte[] pixels = bitmap[54..];
        // Shell bitmaps contain premultiplied BGRA. BMP decoders commonly ignore their alpha;
        // encode those original pixels as PNG instead of flattening them through a BMP decoder.
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        var png = new byte[(int)stream.Size]; reader.ReadBytes(png);
        return Convert.ToBase64String(png);
    }

    private static Task<string> GetBitmapAsync(string target) => InstalledApps.RunSta(() =>
    {
        string parsingName = target switch
        {
            "calculator:" => "shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
            "ms-settings:" => "shell:AppsFolder\\windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel",
            _ => target
        };
        if (parsingName.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return "";
        IShellItemImageFactory? factory = null;
        IntPtr bitmap = IntPtr.Zero, dc = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out factory) < 0) return "";
            if (factory.GetImage(new Size { Width = 32, Height = 32 }, 4, out bitmap) < 0) return "";
            var native = new NativeBitmap();
            if (GetObject(bitmap, Marshal.SizeOf<NativeBitmap>(), ref native) == 0) return "";
            int width = native.Width, height = Math.Abs(native.Height);
            if (width < 1 || height < 1 || width > 256 || height > 256) return "";
            var info = new BitmapInfo { Header = new BitmapHeader { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32, SizeImage = (uint)(width * height * 4) } };
            byte[] pixels = new byte[width * height * 4];
            dc = GetDC(IntPtr.Zero);
            if (GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref info, 0) == 0) return "";
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((ushort)0x4d42); writer.Write(54 + pixels.Length); writer.Write(0); writer.Write(54);
            writer.Write(40); writer.Write(width); writer.Write(-height); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(0); writer.Write(pixels.Length); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(pixels);
            return Convert.ToBase64String(stream.ToArray());
        }
        catch (COMException) { return ""; }
        finally
        {
            if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (factory is not null) Marshal.FinalReleaseComObject(factory);
        }
    });

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { [PreserveSig] int GetImage(Size size, uint flags, out IntPtr bitmap); }
    [StructLayout(LayoutKind.Sequential)] private struct Size { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeBitmap { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public IntPtr Bits; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapHeader Header; public uint Color; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetObject(IntPtr bitmap, int size, ref NativeBitmap value);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] pixels, ref BitmapInfo info, uint usage);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
}

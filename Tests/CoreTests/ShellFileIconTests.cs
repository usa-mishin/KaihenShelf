using System.Diagnostics;
using System.Runtime.InteropServices;
using KaihenShelf;

internal static class ShellFileIconTests
{
    public static void Run(Action<string, Action> test)
    {
        test("shell icon lookup rejects paths and oversized extensions", () =>
        {
            foreach (string ext in new[] { "C:\\file.txt", "/tmp/a", "a\0b", new string('x', 65), null })
                if (ShellFileIconReader.Read(ext) != null) throw new Exception("Unexpected icon for invalid extension");
        });
        if (!OperatingSystem.IsWindows()) return;
        test("Windows association icons include RGBA pixels and transparent margins", () =>
        {
            foreach (string ext in new[] { ".txt", ".blend", ".fbx", ".pdf", ".psd", ".vrcunknown", "" })
            {
                var pixels = ShellFileIconReader.Read(ext);
                if (pixels == null || pixels.Length != 32 * 32 * 4) throw new Exception("Missing icon: " + ext);
                var alpha = Enumerable.Range(0, 1024).Select(i => pixels[i * 4 + 3]).ToArray();
                if (!alpha.Any(x => x > 0) || !alpha.Any(x => x == 0)) throw new Exception("Invalid transparency: " + ext);
            }
        });
        test("repeated shell icon reads release GDI and icon handles", () =>
        {
            using var process = Process.GetCurrentProcess();
            for (int i = 0; i < 3; i++) ShellFileIconReader.Read(".txt");
            uint gdi = GetGuiResources(process.Handle, 0), user = GetGuiResources(process.Handle, 1);
            for (int i = 0; i < 80; i++) ShellFileIconReader.Read(".txt");
            if (GetGuiResources(process.Handle, 0) > gdi + 2 || GetGuiResources(process.Handle, 1) > user + 2)
                throw new Exception("Native icon handles were retained");
        });
    }
    [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process, uint flag);
}

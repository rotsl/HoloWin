using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

// WinUI requires loose native libraries and resources. This self-contained
// launcher carries the complete app and extracts it before Windows loads WinUI.
internal static class Program
{
    private static int Main(string[] args)
    {
        bool extractOnly = args.Contains("--extract-only", StringComparer.Ordinal);
        bool smokeTest = args.Contains("--smoke-test", StringComparer.Ordinal);
        string cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoloWin", "releases");
        try
        {
            using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("HoloWin.Payload.zip")
                ?? throw new InvalidDataException("The application payload is missing.");
            string hash = Convert.ToHexString(SHA256.HashData(payload));
            string destination = Path.Combine(cacheRoot, hash);
            string executable = Path.Combine(destination, "HoloWin.exe");
            using var mutex = new Mutex(false, "Local\\HoloWin.Extract." + hash);
            bool acquired;
            try { acquired = mutex.WaitOne(TimeSpan.FromMinutes(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new TimeoutException("Another HoloWin instance is still preparing this release. Try again shortly.");
            try
            {
                if (!File.Exists(Path.Combine(destination, ".complete")) || !File.Exists(executable))
                {
                    Directory.CreateDirectory(cacheRoot);
                    string staging = Path.Combine(cacheRoot, hash + ".tmp-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        payload.Position = 0;
                        ZipFile.ExtractToDirectory(payload, staging);
                        foreach (string required in new[] { "HoloWin.exe", "HoloWin.dll", "coreclr.dll", "Microsoft.ui.xaml.dll", "resources.pri", "Microsoft.UI.pri", "Microsoft.UI.Xaml.Controls.pri", "OpenCvSharpExtern.dll", "onnxruntime.dll" })
                            if (!File.Exists(Path.Combine(staging, required)))
                                throw new InvalidDataException("The application payload is missing " + required);
                        File.WriteAllText(Path.Combine(staging, ".complete"), hash);
                        // Preserve any incomplete previous extraction for diagnosis.
                        if (Directory.Exists(destination))
                            Directory.Move(destination, destination + ".incomplete-" + Guid.NewGuid().ToString("N"));
                        Directory.Move(staging, destination);
                    }
                    finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                }
            }
            finally { mutex.ReleaseMutex(); }

            if (extractOnly) return 0;
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = destination };
            foreach (string arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows could not start HoloWin.");
            if (smokeTest)
            {
                if (!process.WaitForExit(30000)) { process.Kill(true); throw new TimeoutException("UI startup check timed out."); }
                return process.ExitCode;
            }
            if (process.WaitForExit(3000) && process.ExitCode != 0)
                throw new InvalidOperationException($"HoloWin exited during startup (0x{process.ExitCode:X8}). See %TEMP%\\HoloWin-startup.log for details.");
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(cacheRoot);
                File.WriteAllText(Path.Combine(cacheRoot, "launcher-error.log"), ex.ToString());
            }
            catch { /* A disk error must not hide the original failure. */ }
            if (!extractOnly && !smokeTest) MessageBox(IntPtr.Zero, "HoloWin could not start.\n\n" + ex.Message, "HoloWin", 0x10);
            return 1;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr hwnd, string text, string caption, uint type);
}

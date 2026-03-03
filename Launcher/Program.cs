using System.Diagnostics;
using System.Windows.Forms;

namespace SegmentPlayerLauncher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var root = AppContext.BaseDirectory;
        var appDir = Path.Combine(root, "app");
        var appExe = Path.Combine(appDir, "SegmentPlayer.exe");
        if (!File.Exists(appExe))
        {
            MessageBox.Show(
                $"SegmentPlayer runtime not found:\n{appExe}",
                "SegmentPlayer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = appExe,
                WorkingDirectory = appDir,
                UseShellExecute = true,
                Arguments = BuildArguments(args),
            };
            Process.Start(startInfo);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start SegmentPlayer.\n\n{ex.Message}",
                "SegmentPlayer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 2;
        }
    }

    private static string BuildArguments(IEnumerable<string> args)
    {
        return string.Join(" ", args.Select(EscapeArgument));
    }

    private static string EscapeArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (value.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}

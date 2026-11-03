using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace FileExplorer.Core.Services;

public static class NotificationService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static async Task<bool> ShowAsync(string title, string body, string? iconPath = null)
    {
        string icon = iconPath ?? "";
        string[] gdbus =
        [
            "call", "--session",
            "--dest", "org.freedesktop.Notifications",
            "--object-path", "/org/freedesktop/Notifications",
            "--method", "org.freedesktop.Notifications.Notify",
            ToVariantString("Ferret"), "0", ToVariantString(icon), ToVariantString(title), ToVariantString(body), "[]", "{}", "-1",
        ];
        if (await RunAsync("gdbus", gdbus))
        {
            return true;
        }

        List<string> notifySend = ["--app-name=Ferret"];
        if (icon.Length > 0)
        {
            notifySend.Add($"--icon={icon}");
        }

        notifySend.Add("--");
        notifySend.Add(title);
        notifySend.Add(body);
        return await RunAsync("notify-send", notifySend);
    }

    private static string ToVariantString(string text)
    {
        StringBuilder builder = new("\"");
        foreach (char c in text)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.Append('"').ToString();
    }

    private static async Task<bool> RunAsync(string fileName, IEnumerable<string> arguments)
    {
        ProcessStartInfo psi = new()
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        try
        {
            using Process? process = Process.Start(psi);
            if (process == null)
            {
                return false;
            }

            using CancellationTokenSource cts = new(Timeout);
            await process.WaitForExitAsync(cts.Token);
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            return false;
        }
    }
}

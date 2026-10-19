using System.Text;

namespace FileExplorer.Portal;

public static class FileUri
{
    private const string AllowedChars = "/!$&'()*+,;=:@-._~";

    public static string FromPath(string path)
    {
        StringBuilder builder = new("file://");
        foreach (byte b in Encoding.UTF8.GetBytes(path))
        {
            char c = (char) b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || AllowedChars.Contains(c)))
            {
                builder.Append(c);
                continue;
            }

            builder.Append('%').Append(b.ToString("X2"));
        }

        return builder.ToString();
    }
}

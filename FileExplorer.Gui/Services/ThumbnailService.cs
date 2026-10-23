using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FileExplorer.Core.Entries;
using FileExplorer.Core.Utilities;
using FileExplorer.Gui.ViewModels;
using SkiaSharp;
using Svg.Skia;

namespace FileExplorer.Gui.Services;

public static class ThumbnailService
{
    private const int ThumbnailSize = 128;
    private const int MaxMemoryEntries = 4000;
    private const int JpegHeaderReadSize = 256 * 1024;
    private const int RemoteWorkerCount = 2;
    private const long MaxRemoteImageBytes = 64L * 1024 * 1024;
    private const long MaxRemoteThumbnailerCopyBytes = 32L * 1024 * 1024;
    private const string AllowedUriChars = "/!$&'()*+,;=:@-._~";
    private static readonly TimeSpan ThumbnailerTimeout = TimeSpan.FromSeconds(15);

    private static readonly ConcurrentDictionary<string, CachedThumbnail> _memoryCache = new(StringComparer.Ordinal);
    private static readonly Channel<ThumbnailRequest> _queue = Channel.CreateUnbounded<ThumbnailRequest>();
    private static readonly Channel<ThumbnailRequest> _remoteQueue = Channel.CreateUnbounded<ThumbnailRequest>();
    private static readonly string _cacheRoot = Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } xdgCache
            ? xdgCache
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"),
        "thumbnails");
    private static readonly uint[] _crcTable = BuildCrcTable();
    private static readonly Lazy<Dictionary<string, List<string[]>>> _thumbnailers = new(LoadThumbnailers);

    private readonly record struct ThumbnailRequest(FileItemViewModel Item, long MTime, CancellationToken Token);

    private readonly record struct CachedThumbnail(long MTime, Bitmap? Bitmap);

    static ThumbnailService()
    {
        int workerCount = Math.Clamp(Environment.ProcessorCount / 2, 1, 3);
        for (int i = 0; i < workerCount; i++)
        {
            StartWorker(_queue.Reader, $"ThumbnailWorker{i}");
        }

        for (int i = 0; i < RemoteWorkerCount; i++)
        {
            StartWorker(_remoteQueue.Reader, $"RemoteThumbnailWorker{i}");
        }
    }

    private static void StartWorker(ChannelReader<ThumbnailRequest> reader, string name)
    {
        Thread worker = new(() => ProcessQueue(reader))
        {
            IsBackground = true,
            Name = name,
            Priority = ThreadPriority.BelowNormal,
        };
        worker.Start();
    }

    public static bool CanThumbnail(string? mimeType)
    {
        return mimeType != null && _thumbnailers.Value.ContainsKey(mimeType);
    }

    public static void Request(FileItemViewModel item, DateTime lastWriteTime, CancellationToken token)
    {
        long mtime = ToUnixSeconds(lastWriteTime);
        if (_memoryCache.TryGetValue(item.FullPath, out CachedThumbnail cached) && cached.MTime == mtime)
        {
            item.Thumbnail = cached.Bitmap;
            return;
        }

        Channel<ThumbnailRequest> queue = PathManager.IsRemote(item.FullPath) ? _remoteQueue : _queue;
        queue.Writer.TryWrite(new(item, mtime, token));
    }

    private static void ProcessQueue(ChannelReader<ThumbnailRequest> reader)
    {
        while (true)
        {
            if (!reader.TryRead(out ThumbnailRequest request))
            {
                if (!reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
                {
                    return;
                }

                continue;
            }

            if (request.Token.IsCancellationRequested)
            {
                continue;
            }

            string path = request.Item.FullPath;
            if (_memoryCache.TryGetValue(path, out CachedThumbnail cached) && cached.MTime == request.MTime)
            {
                PostThumbnail(request, cached.Bitmap);
                continue;
            }

            Bitmap? bitmap = null;
            try
            {
                bitmap = LoadThumbnail(request.Item, request.MTime);
            }
            catch
            {
            }

            StoreInMemory(path, request.MTime, bitmap);
            PostThumbnail(request, bitmap);
        }
    }

    private static void PostThumbnail(ThumbnailRequest request, Bitmap? bitmap)
    {
        if (bitmap == null || request.Token.IsCancellationRequested)
        {
            return;
        }

        FileItemViewModel item = request.Item;
        Dispatcher.UIThread.Post(() => item.Thumbnail = bitmap, DispatcherPriority.Background);
    }

    private static void StoreInMemory(string path, long mtime, Bitmap? bitmap)
    {
        if (_memoryCache.Count >= MaxMemoryEntries)
        {
            _memoryCache.Clear();
        }

        _memoryCache[path] = new(mtime, bitmap);
    }

    private static Bitmap? LoadThumbnail(FileItemViewModel item, long mtime)
    {
        string path = item.FullPath;
        string uri = ToFileUri(path);
        string hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(uri))).ToLowerInvariant();

        Bitmap? cached = LoadFromFreedesktopCache(hash, mtime);
        if (cached != null)
        {
            return cached;
        }

        if (item.FileType != FileType.Image)
        {
            return RunThumbnailer(path, uri, hash, mtime, item.MimeType);
        }

        Bitmap? embedded = LoadEmbeddedJpegThumbnail(path);
        if (embedded != null)
        {
            return embedded;
        }

        if (PathManager.IsRemote(path) && item.Entry is FileEntry { FileSize: > MaxRemoteImageBytes })
        {
            return null;
        }

        return GenerateThumbnail(path, uri, hash, mtime);
    }

    private static Bitmap? LoadFromFreedesktopCache(string hash, long mtime)
    {
        foreach (string size in new[] { "normal", "large" })
        {
            string cachePath = Path.Combine(_cacheRoot, size, hash + ".png");
            if (!File.Exists(cachePath))
            {
                continue;
            }

            byte[] png = File.ReadAllBytes(cachePath);
            string? cachedMTime = ReadPngText(png, "Thumb::MTime");
            if (cachedMTime != null && cachedMTime != mtime.ToString())
            {
                continue;
            }

            using MemoryStream stream = new(png);
            return Bitmap.DecodeToWidth(stream, ThumbnailSize);
        }

        return null;
    }

    private static Bitmap? LoadEmbeddedJpegThumbnail(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".jpe" or ".jfif"))
        {
            return null;
        }

        byte[] header = new byte[JpegHeaderReadSize];
        int length;
        using (FileStream stream = File.OpenRead(path))
        {
            length = stream.ReadAtLeast(header, header.Length, false);
        }

        if (length < 4 || header[0] != 0xFF || header[1] != 0xD8)
        {
            return null;
        }

        (int thumbOffset, int thumbLength) = FindExifThumbnail(header, length);
        if (thumbLength <= 0)
        {
            return null;
        }

        Bitmap thumbnail;
        using (MemoryStream thumbStream = new(header, thumbOffset, thumbLength))
        {
            thumbnail = new(thumbStream);
        }

        (int imageWidth, int imageHeight) = FindJpegDimensions(header, length);
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return thumbnail;
        }

        return CropLetterbox(thumbnail, (double) imageWidth / imageHeight);
    }

    private static (int Offset, int Length) FindExifThumbnail(byte[] data, int length)
    {
        int pos = 2;
        while (pos + 4 <= length && data[pos] == 0xFF)
        {
            byte marker = data[pos + 1];
            int segmentLength = (data[pos + 2] << 8) | data[pos + 3];
            if (marker == 0xDA || segmentLength < 2)
            {
                break;
            }

            bool isExif = marker == 0xE1
                          && pos + 10 <= length
                          && data[pos + 4] == 'E' && data[pos + 5] == 'x' && data[pos + 6] == 'i' && data[pos + 7] == 'f';
            if (isExif)
            {
                return ParseExifThumbnail(data, pos + 10, Math.Min(length, pos + 2 + segmentLength));
            }

            pos += 2 + segmentLength;
        }

        return (0, 0);
    }

    private static (int Offset, int Length) ParseExifThumbnail(byte[] data, int tiffStart, int end)
    {
        if (tiffStart + 8 > end)
        {
            return (0, 0);
        }

        bool littleEndian = data[tiffStart] == 'I';
        uint ifd0 = ReadUInt32(data, tiffStart + 4, littleEndian);
        int ifd0Pos = tiffStart + (int) ifd0;
        if (ifd0Pos + 2 > end)
        {
            return (0, 0);
        }

        int entryCount = ReadUInt16(data, ifd0Pos, littleEndian);
        int nextIfdPos = ifd0Pos + 2 + entryCount * 12;
        if (nextIfdPos + 4 > end)
        {
            return (0, 0);
        }

        uint ifd1 = ReadUInt32(data, nextIfdPos, littleEndian);
        int ifd1Pos = tiffStart + (int) ifd1;
        if (ifd1 == 0 || ifd1Pos + 2 > end)
        {
            return (0, 0);
        }

        int ifd1Count = ReadUInt16(data, ifd1Pos, littleEndian);
        uint thumbOffset = 0;
        uint thumbLength = 0;
        for (int i = 0; i < ifd1Count; i++)
        {
            int entry = ifd1Pos + 2 + i * 12;
            if (entry + 12 > end)
            {
                break;
            }

            int tag = ReadUInt16(data, entry, littleEndian);
            if (tag == 0x0201)
            {
                thumbOffset = ReadUInt32(data, entry + 8, littleEndian);
            }
            else if (tag == 0x0202)
            {
                thumbLength = ReadUInt32(data, entry + 8, littleEndian);
            }
        }

        int start = tiffStart + (int) thumbOffset;
        if (thumbOffset == 0 || thumbLength == 0 || start + thumbLength > end)
        {
            return (0, 0);
        }

        return (start, (int) thumbLength);
    }

    private static (int Width, int Height) FindJpegDimensions(byte[] data, int length)
    {
        int pos = 2;
        while (pos + 9 <= length && data[pos] == 0xFF)
        {
            byte marker = data[pos + 1];
            int segmentLength = (data[pos + 2] << 8) | data[pos + 3];
            bool isStartOfFrame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (isStartOfFrame)
            {
                int height = (data[pos + 5] << 8) | data[pos + 6];
                int width = (data[pos + 7] << 8) | data[pos + 8];
                return (width, height);
            }

            if (marker == 0xDA || segmentLength < 2)
            {
                break;
            }

            pos += 2 + segmentLength;
        }

        return (0, 0);
    }

    private static Bitmap CropLetterbox(Bitmap thumbnail, double imageAspect)
    {
        int width = thumbnail.PixelSize.Width;
        int height = thumbnail.PixelSize.Height;
        double thumbAspect = (double) width / height;
        if (Math.Abs(thumbAspect - imageAspect) < 0.02)
        {
            return thumbnail;
        }

        PixelRect content = thumbAspect < imageAspect
            ? CenteredRect(width, (int) Math.Round(width / imageAspect), width, height)
            : CenteredRect((int) Math.Round(height * imageAspect), height, width, height);

        int stride = content.Width * 4;
        int bufferSize = stride * content.Height;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            thumbnail.CopyPixels(content, buffer, bufferSize, stride);
            Bitmap cropped = new(PixelFormat.Bgra8888, AlphaFormat.Premul, buffer, content.Size, thumbnail.Dpi, stride);
            thumbnail.Dispose();
            return cropped;
        }
        catch
        {
            return thumbnail;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static PixelRect CenteredRect(int contentWidth, int contentHeight, int width, int height)
    {
        contentWidth = Math.Clamp(contentWidth, 1, width);
        contentHeight = Math.Clamp(contentHeight, 1, height);
        return new((width - contentWidth) / 2, (height - contentHeight) / 2, contentWidth, contentHeight);
    }

    private static Bitmap? RunThumbnailer(string path, string uri, string hash, long mtime, string? mimeType)
    {
        if (mimeType == null || !_thumbnailers.Value.TryGetValue(mimeType, out List<string[]>? candidates))
        {
            return null;
        }

        string output = Path.Combine(Path.GetTempPath(), $"fe-thumb-{Environment.ProcessId}-{Guid.NewGuid():N}.png");
        string? localCopy = null;
        try
        {
            bool created = candidates.Any(exec => TryRunThumbnailer(exec, path, uri, output));
            if (!created && TryCopyRemoteFile(path, out localCopy))
            {
                string localUri = ToFileUri(localCopy);
                created = candidates.Any(exec => TryRunThumbnailer(exec, localCopy, localUri, output));
            }

            return created ? ScaleAndCache(output, uri, hash, mtime) : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            TryDelete(output);
            if (localCopy != null)
            {
                TryDelete(localCopy);
            }
        }
    }

    private static bool TryRunThumbnailer(string[] exec, string path, string uri, string output)
    {
        ProcessStartInfo psi = new()
        {
            FileName = exec[0],
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in exec.Skip(1))
        {
            psi.ArgumentList.Add(arg
                .Replace("%i", path)
                .Replace("%u", uri)
                .Replace("%o", output)
                .Replace("%s", ThumbnailSize.ToString())
                .Replace("%%", "%"));
        }

        using Process? process = Process.Start(psi);
        if (process == null)
        {
            return false;
        }

        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(ThumbnailerTimeout))
        {
            process.Kill(true);
            return false;
        }

        return process.ExitCode == 0 && File.Exists(output);
    }

    private static bool TryCopyRemoteFile(string path, out string localCopy)
    {
        localCopy = "";
        if (!PathManager.IsRemote(path))
        {
            return false;
        }

        try
        {
            if (new FileInfo(path).Length > MaxRemoteThumbnailerCopyBytes)
            {
                return false;
            }

            string copy = Path.Combine(Path.GetTempPath(), $"fe-thumb-src-{Environment.ProcessId}-{Guid.NewGuid():N}{Path.GetExtension(path)}");
            File.Copy(path, copy);
            localCopy = copy;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static Dictionary<string, List<string[]>> LoadThumbnailers()
    {
        Dictionary<string, List<string[]>> result = new(StringComparer.OrdinalIgnoreCase);
        string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } home
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        string dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } dirs ? dirs : "/usr/local/share:/usr/share";

        foreach (string dataDir in dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries).Reverse().Append(dataHome))
        {
            string dir = Path.Combine(dataDir, "thumbnailers");
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(dir, "*.thumbnailer").Order(StringComparer.Ordinal))
            {
                ParseThumbnailer(file, result);
            }
        }

        return result;
    }

    private static void ParseThumbnailer(string file, Dictionary<string, List<string[]>> result)
    {
        string? tryExec = null;
        string? exec = null;
        string[] mimeTypes = [];
        try
        {
            foreach (string rawLine in File.ReadLines(file))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("TryExec=", StringComparison.Ordinal))
                {
                    tryExec = line[8..].Trim();
                }
                else if (line.StartsWith("Exec=", StringComparison.Ordinal))
                {
                    exec = line[5..].Trim();
                }
                else if (line.StartsWith("MimeType=", StringComparison.Ordinal))
                {
                    mimeTypes = line[9..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }
            }
        }
        catch
        {
            return;
        }

        if (string.IsNullOrEmpty(exec))
        {
            return;
        }

        string[] args = exec.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!IsExecutableAvailable(tryExec ?? args[0]))
        {
            return;
        }

        foreach (string mimeType in mimeTypes)
        {
            if (!result.TryGetValue(mimeType, out List<string[]>? candidates))
            {
                candidates = new();
                result[mimeType] = candidates;
            }

            candidates.Insert(0, args);
        }
    }

    private static bool IsExecutableAvailable(string program)
    {
        if (Path.IsPathRooted(program))
        {
            return File.Exists(program);
        }

        string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        return pathVariable.Split(':', StringSplitOptions.RemoveEmptyEntries).Any(dir => File.Exists(Path.Combine(dir, program)));
    }

    private static Bitmap? ScaleAndCache(string imagePath, string uri, string hash, long mtime)
    {
        Bitmap thumbnail;
        using (FileStream stream = File.OpenRead(imagePath))
        {
            thumbnail = Bitmap.DecodeToWidth(stream, ThumbnailSize);
        }

        return FinishThumbnail(thumbnail, uri, hash, mtime);
    }

    private static Bitmap? GenerateThumbnail(string path, string uri, string hash, long mtime)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".svg" or ".svgz")
        {
            Bitmap? rendered = RenderSvg(path);
            return rendered != null ? FinishThumbnail(rendered, uri, hash, mtime) : null;
        }

        Bitmap thumbnail;
        using (FileStream stream = File.OpenRead(path))
        {
            thumbnail = Bitmap.DecodeToWidth(stream, ThumbnailSize);
        }

        return FinishThumbnail(thumbnail, uri, hash, mtime);
    }

    private static Bitmap? RenderSvg(string path)
    {
        using SKSvg svg = new();
        SKPicture? picture = svg.Load(path);
        if (picture == null)
        {
            return null;
        }

        SKRect bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        float scale = Math.Min(ThumbnailSize / bounds.Width, ThumbnailSize / bounds.Height);
        int width = Math.Max(1, (int) Math.Round(bounds.Width * scale));
        int height = Math.Max(1, (int) Math.Round(bounds.Height * scale));

        using SKBitmap surface = new(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (SKCanvas canvas = new(surface))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
        }

        return new(PixelFormat.Bgra8888, AlphaFormat.Premul, surface.GetPixels(), new(width, height), new(96, 96), surface.RowBytes);
    }

    private static Bitmap? FinishThumbnail(Bitmap thumbnail, string uri, string hash, long mtime)
    {
        if (thumbnail.PixelSize.Height > ThumbnailSize)
        {
            int scaledWidth = Math.Max(1, thumbnail.PixelSize.Width * ThumbnailSize / thumbnail.PixelSize.Height);
            Bitmap scaled = thumbnail.CreateScaledBitmap(new(scaledWidth, ThumbnailSize));
            thumbnail.Dispose();
            thumbnail = scaled;
        }

        try
        {
            WriteFreedesktopCache(thumbnail, uri, hash, mtime);
        }
        catch
        {
        }

        return thumbnail;
    }

    private static void WriteFreedesktopCache(Bitmap thumbnail, string uri, string hash, long mtime)
    {
        string directory = Path.Combine(_cacheRoot, "normal");
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        byte[] png;
        using (MemoryStream stream = new())
        {
            thumbnail.Save(stream);
            png = stream.ToArray();
        }

        byte[] withText = InsertPngTextChunks(png, [
            ("Thumb::URI", uri),
            ("Thumb::MTime", mtime.ToString()),
            ("Software", "Ferret"),
        ]);

        string target = Path.Combine(directory, hash + ".png");
        string temp = Path.Combine(directory, $"{hash}.{Environment.ProcessId}.tmp");
        File.WriteAllBytes(temp, withText);
        File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, target, true);
    }

    private static string? ReadPngText(byte[] png, string key)
    {
        int pos = 8;
        while (pos + 12 <= png.Length)
        {
            int length = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
            if (length < 0 || pos + 12 + length > png.Length)
            {
                return null;
            }

            bool isText = png[pos + 4] == 't' && png[pos + 5] == 'E' && png[pos + 6] == 'X' && png[pos + 7] == 't';
            if (isText)
            {
                ReadOnlySpan<byte> chunk = png.AsSpan(pos + 8, length);
                int separator = chunk.IndexOf((byte) 0);
                if (separator > 0 && Encoding.Latin1.GetString(chunk[..separator]) == key)
                {
                    return Encoding.Latin1.GetString(chunk[(separator + 1)..]);
                }
            }

            if (png[pos + 4] == 'I' && png[pos + 5] == 'D' && png[pos + 6] == 'A' && png[pos + 7] == 'T')
            {
                return null;
            }

            pos += 12 + length;
        }

        return null;
    }

    private static byte[] InsertPngTextChunks(byte[] png, (string Key, string Value)[] entries)
    {
        const int headerEnd = 8 + 25;
        using MemoryStream output = new();
        output.Write(png, 0, headerEnd);

        foreach ((string key, string value) in entries)
        {
            byte[] keyBytes = Encoding.Latin1.GetBytes(key);
            byte[] valueBytes = Encoding.UTF8.GetBytes(value);
            byte[] chunk = new byte[4 + keyBytes.Length + 1 + valueBytes.Length];
            chunk[0] = (byte) 't';
            chunk[1] = (byte) 'E';
            chunk[2] = (byte) 'X';
            chunk[3] = (byte) 't';
            keyBytes.CopyTo(chunk, 4);
            valueBytes.CopyTo(chunk, 5 + keyBytes.Length);

            WriteBigEndian(output, (uint) (chunk.Length - 4));
            output.Write(chunk);
            WriteBigEndian(output, Crc32(chunk));
        }

        output.Write(png, headerEnd, png.Length - headerEnd);
        return output.ToArray();
    }

    private static string ToFileUri(string path)
    {
        StringBuilder builder = new("file://");
        foreach (byte b in Encoding.UTF8.GetBytes(path))
        {
            bool allowed = (b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z') || (b >= '0' && b <= '9') || AllowedUriChars.IndexOf((char) b) >= 0;
            if (allowed)
            {
                builder.Append((char) b);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2"));
            }
        }

        return builder.ToString();
    }

    private static long ToUnixSeconds(DateTime lastWriteTime)
    {
        if (lastWriteTime == default)
        {
            return 0;
        }

        return new DateTimeOffset(lastWriteTime).ToUnixTimeSeconds();
    }

    private static int ReadUInt16(byte[] data, int pos, bool littleEndian)
    {
        return littleEndian
            ? data[pos] | (data[pos + 1] << 8)
            : (data[pos] << 8) | data[pos + 1];
    }

    private static uint ReadUInt32(byte[] data, int pos, bool littleEndian)
    {
        return littleEndian
            ? (uint) (data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24))
            : (uint) ((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);
    }

    private static void WriteBigEndian(Stream stream, uint value)
    {
        stream.WriteByte((byte) (value >> 24));
        stream.WriteByte((byte) (value >> 16));
        stream.WriteByte((byte) (value >> 8));
        stream.WriteByte((byte) value);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}

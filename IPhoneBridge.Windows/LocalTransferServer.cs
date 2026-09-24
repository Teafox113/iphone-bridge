using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IPhoneBridge.Windows;

public sealed record ReceivedFile(string Name, string? SizeDescription, DateTime Time, string? Details = null);

public sealed class LocalTransferServer
{
    private const long MaxFileBytes = 2L * 1024 * 1024 * 1024;
    private WebApplication? _app;
    private string _pairingCode = string.Empty;
    private string _sessionToken = string.Empty;
    private string _destination = string.Empty;
    private string _theme = "clean";
    private int _activeUploads;
    private readonly ConcurrentDictionary<string, AttemptWindow> _attempts = new();

    public event Action<ReceivedFile>? FileReceived;
    public event Action<int>? ActiveUploadsChanged;
    public bool IsRunning => _app is not null;

    public void SetTheme(string theme)
    {
        _theme = theme is "clean" or "studio" or "warm" or "gba" ? theme : "clean";
    }

    public async Task<IReadOnlyList<string>> StartAsync(string destination, string pairingCode, IReadOnlyList<IPAddress> addresses, string theme)
    {
        if (_app is not null) throw new InvalidOperationException("接收服務已經啟動。");

        _destination = Path.GetFullPath(destination);
        _pairingCode = pairingCode;
        SetTheme(theme);
        _sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Directory.CreateDirectory(_destination);

        var port = FindAvailablePort();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = typeof(LocalTransferServer).Assembly.FullName,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = MaxFileBytes + 1024 * 1024;
            foreach (var address in addresses) options.Listen(address, port);
        });
        builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = MaxFileBytes + 1024 * 1024);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") && context.Request.Path != "/api/connect")
            {
                var supplied = context.Request.Headers["X-Session-Key"].ToString();
                if (!FixedTimeEquals(supplied, _sessionToken))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { error = "連線已逾期，請重新輸入連線碼。" });
                    return;
                }
            }
            await next();
        });

        var mobilePage = ReadMobilePage();
        app.MapGet("/", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(mobilePage);
        });
        app.MapGet("/index.html", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(mobilePage);
        });
        app.MapPost("/api/connect", async context =>
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (IsRateLimited(ip))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsJsonAsync(new { error = "嘗試次數過多，請稍候一分鐘再試。" });
                return;
            }

            ConnectRequest? request;
            try { request = await context.Request.ReadFromJsonAsync<ConnectRequest>(); }
            catch { request = null; }

            if (request is null || !FixedTimeEquals(request.Code?.Trim() ?? string.Empty, _pairingCode))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "連線碼不正確，請確認 Windows 上顯示的 8 位數連線碼。" });
                return;
            }

            _attempts.TryRemove(ip, out _);
            await context.Response.WriteAsJsonAsync(new { token = _sessionToken, computer = Environment.MachineName, theme = _theme });
        });
        app.MapGet("/api/status", () => Results.Ok(new { computer = Environment.MachineName, theme = _theme }));
        app.MapPost("/api/upload", (Delegate)UploadAsync);

        _app = app;
        await app.StartAsync();
        return addresses.Select(address => $"http://{address}:{port}/").ToArray();

        static string ReadMobilePage()
        {
            var assembly = typeof(LocalTransferServer).Assembly;
            using var stream = assembly.GetManifestResourceStream("IPhoneBridge.Windows.wwwroot.index.html")
                ?? throw new InvalidOperationException("找不到內嵌的手機網頁。");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }

    private async Task<IResult> UploadAsync(HttpContext context)
    {
        var active = Interlocked.Increment(ref _activeUploads);
        ActiveUploadsChanged?.Invoke(active);
        try
        {
            return await UploadCoreAsync(context);
        }
        finally
        {
            active = Interlocked.Decrement(ref _activeUploads);
            ActiveUploadsChanged?.Invoke(active);
        }
    }

    private async Task<IResult> UploadCoreAsync(HttpContext context)
    {
        if (!context.Request.HasFormContentType) return Results.BadRequest(new { error = "請使用檔案上傳表單。" });
        IFormCollection form;
        try { form = await context.Request.ReadFormAsync(context.RequestAborted); }
        catch (InvalidDataException) { return Results.BadRequest(new { error = "無法讀取上傳資料。" }); }
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0) return Results.BadRequest(new { error = "沒有收到檔案。" });
        if (file.Length > MaxFileBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        var originalName = Path.GetFileName(file.FileName.Replace('\\', '/'));
        var safeName = SanitizeFileName(originalName);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "received-file";

        string targetPath;
        FileStream output;
        while (true)
        {
            targetPath = CreateUniquePath(_destination, safeName);
            try
            {
                output = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                break;
            }
            catch (IOException) when (File.Exists(targetPath)) { }
        }

        try
        {
            await using (output)
                await file.CopyToAsync(output, context.RequestAborted);
        }
        catch
        {
            try { File.Delete(targetPath); } catch { }
            throw;
        }

        var savedName = Path.GetFileName(targetPath);
        var size = FormatSize(file.Length);
        FileReceived?.Invoke(new ReceivedFile(savedName, size, DateTime.Now));
        return Results.Ok(new { name = savedName, size = file.Length });
    }

    private bool IsRateLimited(string ip)
    {
        var now = DateTimeOffset.UtcNow;
        var attempt = _attempts.AddOrUpdate(ip,
            _ => new AttemptWindow(now, 1),
            (_, old) => now - old.StartedAt > TimeSpan.FromMinutes(1)
                ? new AttemptWindow(now, 1)
                : old with { Count = old.Count + 1 });
        return attempt.Count > 5;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(ch => char.IsControl(ch) || invalid.Contains(ch) ? '_' : ch).ToArray()).Trim().TrimEnd('.');
        return safe is "." or ".." ? string.Empty : safe;
    }

    private static string CreateUniquePath(string directory, string fileName)
    {
        var full = Path.Combine(directory, fileName);
        if (!File.Exists(full) && !Directory.Exists(full)) return full;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var number = 2; ; number++)
        {
            full = Path.Combine(directory, $"{stem} ({number}){extension}");
            if (!File.Exists(full) && !Directory.Exists(full)) return full;
        }
    }

    private static bool FixedTimeEquals(string supplied, string expected)
    {
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected));
    }

    private static int FindAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024 * 1024) return $"{Math.Max(1, bytes / 1024)} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024d / 1024d:0.0} MB";
        return $"{bytes / 1024d / 1024d / 1024d:0.00} GB";
    }

    public async Task StopAsync()
    {
        var app = _app;
        _app = null;
        if (app is not null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
        _pairingCode = string.Empty;
        _sessionToken = string.Empty;
        _theme = "clean";
        _attempts.Clear();
    }

    private sealed record AttemptWindow(DateTimeOffset StartedAt, int Count);
    private sealed record ConnectRequest(string? Code);
}

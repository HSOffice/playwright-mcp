using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using ModelContextProtocol.Server;

namespace PlaywrightMcpServer;

[McpServerToolType]
public sealed class PlaywrightTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly object Gate = new();
    private static IPlaywright? _playwright;
    private static IBrowser? _browser;
    private static IBrowserContext? _context;
    private static IPage? _page;
    private static bool _isChromium;

    private static bool Headless =>
        (Environment.GetEnvironmentVariable("MCP_PLAYWRIGHT_HEADLESS") ?? "false")
        .Equals("true", StringComparison.OrdinalIgnoreCase);

    private static string DownloadsDir =>
        Environment.GetEnvironmentVariable("MCP_PLAYWRIGHT_DOWNLOADS_DIR") ??
        Path.GetFullPath("./downloads");

    private static string VideosDir =>
        Environment.GetEnvironmentVariable("MCP_PLAYWRIGHT_VIDEOS_DIR") ??
        Path.GetFullPath("./videos");

    private static string ShotsDir =>
        Path.GetFullPath("./shots");

    private static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private static async Task EnsureLaunchedAsync(CancellationToken cancellationToken)
    {
        if (_page is { IsClosed: true })
            _page = null;
        if (_page is not null)
            return;

        lock (Gate)
        {
            Directory.CreateDirectory(DownloadsDir);
            Directory.CreateDirectory(VideosDir);
            Directory.CreateDirectory(ShotsDir);
        }

        _playwright ??= await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);

        if (_browser is null)
        {
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = Headless,
                Channel = "msedge"
            }).ConfigureAwait(false);
            _isChromium = true;
        }

        if (_context is not null)
        {
            try { await _context.CloseAsync().ConfigureAwait(false); }
            catch { }
            _context = null;
        }

        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            AcceptDownloads = true,
            RecordVideoDir = VideosDir,
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            Geolocation = new Geolocation { Latitude = 0, Longitude = 0 }
        }).ConfigureAwait(false);

        await _context.GrantPermissionsAsync(new[]
        {
            "geolocation", "notifications", "camera", "microphone", "clipboard-read", "clipboard-write"
        }).ConfigureAwait(false);

        _page = await _context.NewPageAsync().ConfigureAwait(false);
    }

    [McpServerTool]
    [Description("Close and dispose Playwright browser resources.")]
    public static async Task<string> CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_page is not null)
        {
            try { await _page.CloseAsync().ConfigureAwait(false); }
            catch { }
            _page = null;
        }

        if (_context is not null)
        {
            try { await _context.CloseAsync().ConfigureAwait(false); }
            catch { }
            _context = null;
        }

        if (_browser is not null)
        {
            try { await _browser.CloseAsync().ConfigureAwait(false); }
            catch { }
            _browser = null;
        }

        if (_playwright is not null)
        {
            _playwright.Dispose();
            _playwright = null;
        }

        _isChromium = false;
        return Serialize(new { closed = true });
    }

    [McpServerTool]
    [Description("(Re)launch browser and open a fresh page.")]
    public static async Task<string> RelaunchAsync(CancellationToken cancellationToken = default)
    {
        await CloseAsync(cancellationToken).ConfigureAwait(false);
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        return Serialize(new { relaunched = true, headless = Headless, engine = _isChromium ? "chromium" : "unknown" });
    }

    [McpServerTool]
    [Description("Navigate to a URL and wait for 'load' state.")]
    public static async Task<string> GotoAsync(
        [Description("URL to navigate to")] string url,
        [Description("Timeout ms (default 30000)")] int? timeoutMs = 30000,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        var response = await _page!.GotoAsync(url, new PageGotoOptions
        {
            Timeout = timeoutMs,
            WaitUntil = WaitUntilState.Load
        }).ConfigureAwait(false);

        return Serialize(new
        {
            url = _page.Url,
            status = response?.Status,
            ok = response?.Ok,
            requestUrl = response?.Request?.Url
        });
    }

    [McpServerTool]
    [Description("Click an element by CSS/XPath/text selector.")]
    public static async Task<string> ClickAsync(
        [Description("Selector (CSS default; prefix with xpath= for XPath, text= for text)")] string selector,
        [Description("Timeout ms (default 10000)")] int? timeoutMs = 10000,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        await _page!.ClickAsync(selector, new PageClickOptions
        {
            Timeout = timeoutMs
        }).ConfigureAwait(false);
        return Serialize(new { clicked = selector });
    }

    [McpServerTool]
    [Description("Fill input/textarea by selector with given text.")]
    public static async Task<string> FillAsync(
        [Description("Selector")] string selector,
        [Description("Text to fill")] string text,
        [Description("Timeout ms (default 10000)")] int? timeoutMs = 10000,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        await _page!.FillAsync(selector, text ?? string.Empty, new PageFillOptions
        {
            Timeout = timeoutMs
        }).ConfigureAwait(false);
        return Serialize(new { filled = selector, length = text?.Length ?? 0 });
    }

    [McpServerTool]
    [Description("Get innerText from the first matched element.")]
    public static async Task<string> InnerTextAsync(
        [Description("Selector")] string selector,
        [Description("Timeout ms (default 10000)")] int? timeoutMs = 10000,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        var locator = _page!.Locator(selector).First;
        await locator.WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = timeoutMs
        }).ConfigureAwait(false);
        var text = await locator.InnerTextAsync().ConfigureAwait(false);
        return Serialize(new { selector, text });
    }

    [McpServerTool]
    [Description("Take a screenshot of the page or a selector.")]
    public static async Task<string> ScreenshotAsync(
        [Description("Output path (PNG/JPEG). If relative, saved under ./shots")] string outputPath,
        [Description("Optional selector to clip to element")] string? selector = null,
        [Description("Full page (ignored when selector provided)")] bool fullPage = false,
        [Description("Quality 0-100 for JPEG")] int? quality = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        var absolutePath = Path.GetFullPath(Path.IsPathRooted(outputPath)
            ? outputPath
            : Path.Combine(ShotsDir, outputPath));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        if (!string.IsNullOrWhiteSpace(selector))
        {
            var locator = _page!.Locator(selector).First;
            await locator.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 }).ConfigureAwait(false);
            await locator.ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = absolutePath,
                Timeout = 15000,
                Quality = quality
            }).ConfigureAwait(false);
        }
        else
        {
            await _page!.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = absolutePath,
                FullPage = fullPage,
                Quality = quality
            }).ConfigureAwait(false);
        }

        var fileInfo = new FileInfo(absolutePath);
        return Serialize(new
        {
            path = absolutePath,
            bytes = fileInfo.Exists ? fileInfo.Length : 0,
            fullPage,
            selector
        });
    }

    [McpServerTool]
    [Description("Export current page as PDF (Chromium only).")]
    public static async Task<string> PdfAsync(
        [Description("Output path (*.pdf). If relative, saved under ./shots")] string outputPath,
        [Description("Paper format: A4/Letter... (optional)")] string? format = "A4",
        [Description("Print background")] bool printBackground = true,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);

        if (!_isChromium)
            throw new NotSupportedException("PDF export is only supported in Chromium.");

        var absolutePath = Path.GetFullPath(Path.IsPathRooted(outputPath)
            ? outputPath
            : Path.Combine(ShotsDir, outputPath));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await _page!.PdfAsync(new PagePdfOptions
        {
            Path = absolutePath,
            Format = format,
            PrintBackground = printBackground
        }).ConfigureAwait(false);

        var fileInfo = new FileInfo(absolutePath);
        return Serialize(new
        {
            path = absolutePath,
            bytes = fileInfo.Length,
            format,
            printBackground
        });
    }

    [McpServerTool]
    [Description("Wait for selector to be attached/visible/hidden/detached.")]
    public static async Task<string> WaitForSelectorAsync(
        [Description("Selector")] string selector,
        [Description("State: attached|visible|hidden|detached")] string? state = "visible",
        [Description("Timeout ms (default 15000)")] int? timeoutMs = 15000,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        var targetState = state?.ToLowerInvariant() switch
        {
            "attached" => WaitForSelectorState.Attached,
            "visible" => WaitForSelectorState.Visible,
            "hidden" => WaitForSelectorState.Hidden,
            "detached" => WaitForSelectorState.Detached,
            _ => WaitForSelectorState.Visible
        };

        await _page!.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions
        {
            State = targetState,
            Timeout = timeoutMs
        }).ConfigureAwait(false);

        return Serialize(new { waited = selector, state = targetState.ToString() });
    }

    [McpServerTool]
    [Description("Evaluate JavaScript in page and return JSON-serializable result.")]
    public static async Task<string> EvalAsync(
        [Description("JS expression: must return JSON-serializable value")] string jsExpression,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        var result = await _page!.EvaluateAsync<object?>(jsExpression).ConfigureAwait(false);
        return Serialize(new { result });
    }

    [McpServerTool]
    [Description("Get current page URL.")]
    public static async Task<string> GetUrlAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        return Serialize(new { url = _page!.Url });
    }
}

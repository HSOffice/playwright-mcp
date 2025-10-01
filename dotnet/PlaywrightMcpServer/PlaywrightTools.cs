using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatUiTest.MCP.Playwright;
using Microsoft.Playwright;

namespace PlaywrightMcpServer;

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
    private static bool _tracingActive;

    private static readonly List<ConsoleMessageEntry> ConsoleMessages = new();
    private static readonly List<NetworkRequestEntry> NetworkRequests = new();
    private static readonly Dictionary<string, NetworkRequestEntry> NetworkRequestMap = new();

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

    private static string TracesDir =>
        Path.GetFullPath("./traces");

    private static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private static async Task EnsureLaunchedAsync(CancellationToken cancellationToken)
    {
        if (_page is { IsClosed: true })
            _page = null;
        if (_page is not null)
            return;

        EnsureDirectories();

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

        await _context.GrantAllPermissionsAsync().ConfigureAwait(false);

        var page = await _context.NewPageAsync().ConfigureAwait(false);
        SetActivePage(page);
    }

    private static void SetActivePage(IPage page)
    {
        if (_page == page)
            return;

        if (_page is not null)
            DetachPageEventHandlers(_page);

        _page = page;

        lock (ConsoleMessages)
        {
            ConsoleMessages.Clear();
        }

        lock (NetworkRequests)
        {
            NetworkRequests.Clear();
            NetworkRequestMap.Clear();
        }

        AttachPageEventHandlers(page);
    }

    private static void AttachPageEventHandlers(IPage page)
    {
        page.Console += PageOnConsole;
        page.Request += PageOnRequest;
        page.Response += PageOnResponse;
        page.RequestFailed += PageOnRequestFailed;
    }

    private static void DetachPageEventHandlers(IPage page)
    {
        page.Console -= PageOnConsole;
        page.Request -= PageOnRequest;
        page.Response -= PageOnResponse;
        page.RequestFailed -= PageOnRequestFailed;
    }

    private static void PageOnConsole(object? sender, IConsoleMessage message)
    {
        var entry = new ConsoleMessageEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Type = message.Type,
            Text = message.Text,
            Args = message.Args.Select(arg => arg.ToString()).Where(arg => arg is not null).Cast<string>().ToArray()
        };

        lock (ConsoleMessages)
        {
            ConsoleMessages.Add(entry);
            if (ConsoleMessages.Count > 200)
                ConsoleMessages.RemoveAt(0);
        }
    }

    private static void PageOnRequest(object? sender, IRequest request)
    {
        var entry = new NetworkRequestEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Method = request.Method,
            Url = request.Url,
            ResourceType = request.ResourceType,
            Failure = null,
            Status = null
        };

        lock (NetworkRequests)
        {
            NetworkRequests.Add(entry);
            NetworkRequestMap[request.Guid] = entry;
            if (NetworkRequests.Count > 500)
            {
                var first = NetworkRequests.First();
                NetworkRequests.RemoveAt(0);
                var keyToRemove = NetworkRequestMap.FirstOrDefault(kvp => kvp.Value == first).Key;
                if (keyToRemove is not null)
                    NetworkRequestMap.Remove(keyToRemove);
            }
        }
    }

    private static void PageOnResponse(object? sender, IResponse response)
    {
        lock (NetworkRequests)
        {
            if (NetworkRequestMap.TryGetValue(response.Request.Guid, out var entry))
            {
                entry.Status = response.Status;
            }
        }
    }

    private static void PageOnRequestFailed(object? sender, IRequest request)
    {
        lock (NetworkRequests)
        {
            if (NetworkRequestMap.TryGetValue(request.Guid, out var entry))
            {
                entry.Failure = request.Failure;
            }
        }
    }

    private static ConsoleMessageEntry[] SnapshotConsoleMessages()
    {
        lock (ConsoleMessages)
        {
            return ConsoleMessages.ToArray();
        }
    }

    private static NetworkRequestEntry[] SnapshotNetworkRequests()
    {
        lock (NetworkRequests)
        {
            return NetworkRequests.Select(r => r.Clone()).ToArray();
        }
    }

    private static void EnsureDirectories()
    {
        lock (Gate)
        {
            Directory.CreateDirectory(DownloadsDir);
            Directory.CreateDirectory(VideosDir);
            Directory.CreateDirectory(ShotsDir);
            Directory.CreateDirectory(TracesDir);
        }
    }

    [Description("Close and dispose Playwright browser resources.")]
    public static async Task<string> CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_page is not null)
        {
            DetachPageEventHandlers(_page);
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
        _tracingActive = false;
        return Serialize(new { closed = true });
    }

    [Description("(Re)launch browser and open a fresh page.")]
    public static async Task<string> RelaunchAsync(CancellationToken cancellationToken = default)
    {
        await CloseAsync(cancellationToken).ConfigureAwait(false);
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        return Serialize(new { relaunched = true, headless = Headless, engine = _isChromium ? "chromium" : "unknown" });
    }

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

    [Description("Go back in browser history if possible.")]
    public static async Task<string> GoBackAsync(
        [Description("Timeout ms (default 15000)")] int? timeoutMs = 15000,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        var response = await _page!.GoBackAsync(new PageGoBackOptions
        {
            Timeout = timeoutMs,
            WaitUntil = WaitUntilState.Load
        }).ConfigureAwait(false);

        return Serialize(new
        {
            url = _page.Url,
            status = response?.Status,
            ok = response?.Ok
        });
    }

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

    [Description("Hover over an element by selector.")]
    public static async Task<string> HoverAsync(
        [Description("Selector")] string selector,
        [Description("Timeout ms (default 10000)")] int? timeoutMs = 10000,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        await _page!.HoverAsync(selector, new PageHoverOptions
        {
            Timeout = timeoutMs
        }).ConfigureAwait(false);
        return Serialize(new { hovered = selector });
    }

    [Description("Drag an element onto another selector.")]
    public static async Task<string> DragAndDropAsync(
        [Description("Source selector")] string sourceSelector,
        [Description("Target selector")] string targetSelector,
        [Description("Timeout ms (default 15000)")] int? timeoutMs = 15000,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        await _page!.DragAndDropAsync(sourceSelector, targetSelector, new PageDragAndDropOptions
        {
            Timeout = timeoutMs
        }).ConfigureAwait(false);
        return Serialize(new { dragged = sourceSelector, droppedOn = targetSelector });
    }

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

    [Description("Type text into an element, optionally pressing Enter.")]
    public static async Task<string> TypeAsync(
        [Description("Selector")] string selector,
        [Description("Text to type")] string text,
        [Description("Submit (press Enter) after typing")] bool submit = false,
        [Description("Delay between keystrokes in ms")] int? delayMs = null,
        [Description("Timeout ms (default 10000)")] int? timeoutMs = 10000,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        var locator = _page!.Locator(selector).First;
        await locator.WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs }).ConfigureAwait(false);
        await locator.TypeAsync(text ?? string.Empty, new LocatorTypeOptions
        {
            Delay = delayMs,
            Timeout = timeoutMs
        }).ConfigureAwait(false);

        if (submit)
            await locator.PressAsync("Enter").ConfigureAwait(false);

        return Serialize(new { typed = selector, length = text?.Length ?? 0, submit });
    }

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

    [Description("Press a single keyboard key on the active page.")]
    public static async Task<string> PressKeyAsync(
        [Description("Key, e.g. Enter or Control+S")] string key,
        [Description("Delay between keydown and keyup in ms")] int? delayMs = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        await _page!.Keyboard.PressAsync(key, new KeyboardPressOptions
        {
            Delay = delayMs
        }).ConfigureAwait(false);

        return Serialize(new { pressed = key, delayMs });
    }

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

    [Description("Select one or more option values in a select element.")]
    public static async Task<string> SelectOptionAsync(
        [Description("Selector for the <select> element")] string selector,
        [Description("Values to select")] string[] values,
        [Description("Timeout ms (default 10000)")] int? timeoutMs = 10000,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        if (values is null || values.Length == 0)
            throw new ArgumentException("At least one value must be provided.", nameof(values));

        var locator = _page!.Locator(selector).First;
        await locator.WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs }).ConfigureAwait(false);
        var selected = await locator.SelectOptionAsync(values.Select(v => new SelectOptionValue { Value = v }).ToArray(), new LocatorSelectOptionOptions
        {
            Timeout = timeoutMs
        }).ConfigureAwait(false);

        return Serialize(new { selector, selected });
    }

    [Description("Upload local files to an <input type=\"file\"> element.")]
    public static async Task<string> UploadFilesAsync(
        [Description("Selector for file input")] string selector,
        [Description("Absolute or relative file paths")] string[] paths,
        [Description("Timeout ms (default 10000)")] int? timeoutMs = 10000,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        if (paths is null || paths.Length == 0)
            throw new ArgumentException("At least one path must be provided.", nameof(paths));

        var locator = _page!.Locator(selector).First;
        await locator.WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs }).ConfigureAwait(false);

        var absolutePaths = paths.Select(path => Path.GetFullPath(path)).ToArray();
        foreach (var filePath in absolutePaths)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }
        await locator.SetInputFilesAsync(absolutePaths);

        return Serialize(new { selector, count = absolutePaths.Length });
    }

    [Description("Fill multiple form fields by selector.")]
    public static async Task<string> FillFormAsync(
        [Description("Fields to fill")] FormField[] fields,
        [Description("Timeout ms per field (default 10000)")] int? timeoutMs = 10000,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        if (fields is null || fields.Length == 0)
            throw new ArgumentException("At least one field must be provided.", nameof(fields));

        var results = new List<object>();

        foreach (var field in fields)
        {
            var locator = _page!.Locator(field.Selector).First;
            await locator.WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs }).ConfigureAwait(false);

            if (string.Equals(field.Action, "select", StringComparison.OrdinalIgnoreCase))
            {
                var optionValues = (field.Values ?? Array.Empty<string>()).Select(v => new SelectOptionValue { Value = v }).ToArray();
                var selected = await locator.SelectOptionAsync(optionValues, new LocatorSelectOptionOptions { Timeout = timeoutMs }).ConfigureAwait(false);
                results.Add(new { field.Selector, action = "select", selected });
            }
            else
            {
                await locator.FillAsync(field.Value ?? string.Empty, new LocatorFillOptions { Timeout = timeoutMs }).ConfigureAwait(false);
                results.Add(new { field.Selector, action = "fill", length = field.Value?.Length ?? 0 });
            }
        }

        return Serialize(new { count = results.Count, results });
    }

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

    [Description("Handle the next dialog by accepting or dismissing it.")]
    public static async Task<string> HandleDialogAsync(
        [Description("Accept dialog (true) or dismiss (false)")] bool accept = true,
        [Description("Prompt text when accepting")] string? promptText = null,
        [Description("Timeout ms (default 15000)")] int? timeoutMs = 15000,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);

        var page = _page!;
        var tcs = new TaskCompletionSource<DialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutMs is > 0)
            cts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs.Value));

        void Handler(object? sender, IDialog dialog)
        {
            page.Dialog -= Handler;
            _ = Task.Run(async () =>
            {
                try
                {
                    if (accept)
                        await dialog.AcceptAsync(promptText).ConfigureAwait(false);
                    else
                        await dialog.DismissAsync().ConfigureAwait(false);

                    tcs.TrySetResult(new DialogResult(dialog.Type, dialog.Message, dialog.DefaultValue));
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
        }

        page.Dialog += Handler;

        using var registration = cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));

        try
        {
            var result = await tcs.Task.ConfigureAwait(false);
            return Serialize(new
            {
                handled = true,
                accepted = accept,
                result.Type,
                result.Message,
                result.DefaultValue
            });
        }
        catch (OperationCanceledException)
        {
            return Serialize(new { handled = false, timeout = true });
        }
        finally
        {
            page.Dialog -= Handler;
        }
    }

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

    [Description("Wait for text to appear/disappear or for a specific time.")]
    public static async Task<string> WaitForAsync(
        [Description("Text to appear")] string? text = null,
        [Description("Text to disappear")] string? textGone = null,
        [Description("Wait this many ms regardless of text")] int? timeMs = null,
        [Description("Timeout ms for text waits (default 15000)")] int? timeoutMs = 15000,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        var page = _page!;
        bool? appeared = null;
        bool? disappeared = null;

        if (timeMs is > 0)
            await Task.Delay(timeMs.Value, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(text))
        {
            await page.WaitForFunctionAsync(
                "(expected) => document.body && document.body.innerText.includes(expected)",
                text,
                new PageWaitForFunctionOptions { Timeout = timeoutMs }
            ).ConfigureAwait(false);
            appeared = true;
        }

        if (!string.IsNullOrWhiteSpace(textGone))
        {
            await page.WaitForFunctionAsync(
                "(expected) => !document.body || !document.body.innerText.includes(expected)",
                textGone,
                new PageWaitForFunctionOptions { Timeout = timeoutMs }
            ).ConfigureAwait(false);
            disappeared = true;
        }

        return Serialize(new
        {
            waited = true,
            appeared = appeared == true ? text : null,
            disappeared = disappeared == true ? textGone : null,
            timeMs
        });
    }

    [Description("Evaluate JavaScript in page and return JSON-serializable result.")]
    public static async Task<string> EvalAsync(
        [Description("JS expression: must return JSON-serializable value")] string jsExpression,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        var result = await _page!.EvaluateAsync<object?>(jsExpression).ConfigureAwait(false);
        return Serialize(new { result });
    }

    [Description("Get current page URL.")]
    public static async Task<string> GetUrlAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        return Serialize(new { url = _page!.Url });
    }

    [Description("Get captured console messages since the current page was opened/switch.")]
    public static async Task<string> ConsoleMessagesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        var messages = SnapshotConsoleMessages();
        return Serialize(new { messages });
    }

    [Description("List captured network requests since the current page was opened/switch.")]
    public static async Task<string> NetworkRequestsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        var requests = SnapshotNetworkRequests();
        return Serialize(new { requests });
    }

    [Description("Resize browser viewport.")]
    public static async Task<string> ResizeAsync(
        [Description("Viewport width")] int width,
        [Description("Viewport height")] int height,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        await _page!.SetViewportSizeAsync(new ViewportSize { Width = width, Height = height }).ConfigureAwait(false);
        return Serialize(new { width, height });
    }

    [Description("Capture an accessibility snapshot of the page.")]
    public static async Task<string> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await _page!.Accessibility.SnapshotAsync().ConfigureAwait(false);
        return Serialize(new { snapshot });
    }

    [Description("Manage tabs: list, create, close or switch.")]
    public static async Task<string> TabsAsync(
        [Description("Action: list|new|close|switch")] string action,
        [Description("Zero-based tab index (for close/switch)")] int? index = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);

        if (_context is null)
            throw new InvalidOperationException("Browser context not initialized.");

        switch (action.ToLowerInvariant())
        {
            case "list":
                var list = _context.Pages.Select((p, i) => new
                {
                    index = i,
                    url = p.Url,
                    isClosed = p.IsClosed,
                    isActive = p == _page
                }).ToArray();
                return Serialize(new { tabs = list });

            case "new":
                var newPage = await _context.NewPageAsync().ConfigureAwait(false);
                SetActivePage(newPage);
                return Serialize(new { created = _context.Pages.Count, activeIndex = GetPageIndex(newPage) });

            case "switch":
                if (index is null)
                    throw new ArgumentException("Index required for switch action.", nameof(index));
                if (index < 0 || index >= _context.Pages.Count)
                    throw new ArgumentOutOfRangeException(nameof(index), index, "Tab index out of range.");
                var switchPage = _context.Pages[index.Value];
                if (switchPage.IsClosed)
                    throw new InvalidOperationException("Cannot switch to a closed tab.");
                SetActivePage(switchPage);
                return Serialize(new { activeIndex = index, url = switchPage.Url });

            case "close":
                if (index is null)
                    throw new ArgumentException("Index required for close action.", nameof(index));
                if (index < 0 || index >= _context.Pages.Count)
                    throw new ArgumentOutOfRangeException(nameof(index), index, "Tab index out of range.");
                var target = _context.Pages[index.Value];
                var wasActive = target == _page;
                await target.CloseAsync().ConfigureAwait(false);

                var remaining = _context.Pages.FirstOrDefault(p => !p.IsClosed);
                if (remaining is null)
                {
                    var replacement = await _context.NewPageAsync().ConfigureAwait(false);
                    SetActivePage(replacement);
                    return Serialize(new { closed = index, activeIndex = GetPageIndex(replacement) });
                }

                if (wasActive)
                    SetActivePage(remaining);

                return Serialize(new { closed = index, activeIndex = GetPageIndex(_page!) });

            default:
                throw new ArgumentException($"Unsupported action '{action}'.", nameof(action));
        }
    }

    [Description("Install Playwright browsers using bundled CLI.")]
    public static async Task<string> InstallAsync(
        [Description("Optional browser name, e.g. chromium")] string? browser = null,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "install" };
        if (!string.IsNullOrWhiteSpace(browser))
            args.Add(browser);

        var exitCode = await Microsoft.Playwright.Program.Main(args.ToArray()).ConfigureAwait(false);
        return Serialize(new { success = exitCode == 0, exitCode, browser });
    }

    [Description("Move mouse to coordinates relative to page viewport.")]
    public static async Task<string> MouseMoveAsync(
        [Description("X coordinate")] double x,
        [Description("Y coordinate")] double y,
        [Description("Number of move steps")] int? steps = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        await _page!.Mouse.MoveAsync(x, y, new MouseMoveOptions { Steps = steps }).ConfigureAwait(false);
        return Serialize(new { x, y, steps });
    }

    [Description("Click mouse at coordinates relative to page viewport.")]
    public static async Task<string> MouseClickAsync(
        [Description("X coordinate")] double x,
        [Description("Y coordinate")] double y,
        [Description("Button: left|middle|right")] string? button = "left",
        [Description("Number of clicks")] int clickCount = 1,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        await _page!.Mouse.ClickAsync(x, y, new MouseClickOptions
        {
            Button = button switch
            {
                "middle" => MouseButton.Middle,
                "right" => MouseButton.Right,
                _ => MouseButton.Left
            },
            ClickCount = clickCount
        }).ConfigureAwait(false);
        return Serialize(new { x, y, button, clickCount });
    }

    [Description("Drag mouse between coordinates relative to viewport.")]
    public static async Task<string> MouseDragAsync(
        [Description("Start X")] double startX,
        [Description("Start Y")] double startY,
        [Description("End X")] double endX,
        [Description("End Y")] double endY,
        [Description("Steps for the move")] int? steps = 25,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        await _page!.Mouse.MoveAsync(startX, startY).ConfigureAwait(false);
        await _page.Mouse.DownAsync().ConfigureAwait(false);
        await _page.Mouse.MoveAsync(endX, endY, new MouseMoveOptions { Steps = steps }).ConfigureAwait(false);
        await _page.Mouse.UpAsync().ConfigureAwait(false);
        return Serialize(new { startX, startY, endX, endY, steps });
    }

    [Description("Start Playwright tracing (snapshots, screenshots, sources).")]
    public static async Task<string> StartTracingAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        if (_context is null)
            throw new InvalidOperationException("Browser context not initialized.");

        if (_tracingActive)
            return Serialize(new { tracing = true, alreadyStarted = true });

        await _context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true
        }).ConfigureAwait(false);

        _tracingActive = true;
        return Serialize(new { tracing = true });
    }

    [Description("Stop tracing and save to a .zip file.")]
    public static async Task<string> StopTracingAsync(
        [Description("Output path for trace .zip (default ./traces/trace-<timestamp>.zip")] string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        if (_context is null)
            throw new InvalidOperationException("Browser context not initialized.");

        if (!_tracingActive)
            return Serialize(new { tracing = false, alreadyStopped = true });

        EnsureDirectories();
        var path = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(TracesDir, $"trace-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip")
            : Path.GetFullPath(outputPath);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await _context.Tracing.StopAsync(new TracingStopOptions
        {
            Path = path
        }).ConfigureAwait(false);

        _tracingActive = false;
        return Serialize(new { tracing = false, path });
    }

    private sealed record DialogResult(string Type, string Message, string? DefaultValue);

    public sealed record FormField(
        [property: JsonPropertyName("selector")] string Selector,
        [property: JsonPropertyName("value")] string? Value,
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("values")] string[]? Values);

    public sealed record ConsoleMessageEntry
    {
        [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; init; }
        [JsonPropertyName("type")] public string Type { get; init; } = string.Empty;
        [JsonPropertyName("text")] public string Text { get; init; } = string.Empty;
        [JsonPropertyName("args")][AllowNull] public string[]? Args { get; init; }
    }

    public sealed class NetworkRequestEntry
    {
        [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; init; }
        [JsonPropertyName("method")] public string Method { get; init; } = string.Empty;
        [JsonPropertyName("url")] public string Url { get; init; } = string.Empty;
        [JsonPropertyName("resourceType")][AllowNull] public string? ResourceType { get; init; }
        [JsonPropertyName("status")][AllowNull] public int? Status { get; set; }
        [JsonPropertyName("failure")][AllowNull] public string? Failure { get; set; }

        public NetworkRequestEntry Clone() => new()
        {
            Timestamp = Timestamp,
            Method = Method,
            Url = Url,
            ResourceType = ResourceType,
            Status = Status,
            Failure = Failure
        };
    }

    private static int GetPageIndex(IPage page)
    {
        if (_context is null)
            return -1;

        var match = _context.Pages
            .Select((p, idx) => new { Page = p, Index = idx })
            .FirstOrDefault(tuple => tuple.Page == page);
        return match?.Index ?? -1;
    }
}

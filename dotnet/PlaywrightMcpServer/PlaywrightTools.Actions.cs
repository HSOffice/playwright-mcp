using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using ModelContextProtocol.Server;

namespace PlaywrightMcpServer;

public sealed partial class PlaywrightTools
{
    [McpServerTool]
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
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
        var response = await page.GotoAsync(url, new PageGotoOptions
        {
            Timeout = timeoutMs,
            WaitUntil = WaitUntilState.Load
        }).ConfigureAwait(false);

        return Serialize(new
        {
            url = page.Url,
            status = response?.Status,
            ok = response?.Ok,
            requestUrl = response?.Request?.Url
        });
    }

    [McpServerTool]
    [Description("Go back in browser history if possible.")]
    public static async Task<string> GoBackAsync(
        [Description("Timeout ms (default 15000)")] int? timeoutMs = 15000,
        CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
        var response = await page.GoBackAsync(new PageGoBackOptions
        {
            Timeout = timeoutMs,
            WaitUntil = WaitUntilState.Load
        }).ConfigureAwait(false);

        return Serialize(new
        {
            url = page.Url,
            status = response?.Status,
            ok = response?.Ok
        });
    }

    [McpServerTool]
    [Description("Click an element by CSS/XPath/text selector.")]
    public static async Task<string> ClickAsync(
        [Description("Selector (CSS default; prefix with xpath= for XPath, text= for text)")] string selector,
        [Description("Timeout ms (default 10000)")] int? timeoutMs = 10000,
        CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
        await page.ClickAsync(selector, new PageClickOptions
        {
            Timeout = timeoutMs
        }).ConfigureAwait(false);
        return Serialize(new { clicked = selector });
    }

    [McpServerTool]
    [Description("Hover over an element by selector.")]
    public static async Task<string> HoverAsync(
        [Description("Selector")] string selector,
        [Description("Timeout ms (default 10000)")] int? timeoutMs = 10000,
        CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
        await page.HoverAsync(selector, new PageHoverOptions
        {
            Timeout = timeoutMs
        }).ConfigureAwait(false);
        return Serialize(new { hovered = selector });
    }

    [McpServerTool]
    [Description("Drag an element onto another selector.")]
    public static async Task<string> DragAndDropAsync(
        [Description("Source selector")] string sourceSelector,
        [Description("Target selector")] string targetSelector,
        [Description("Timeout ms (default 15000)")] int? timeoutMs = 15000,
        CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
        await page.DragAndDropAsync(sourceSelector, targetSelector, new PageDragAndDropOptions
        {
            Timeout = timeoutMs
        }).ConfigureAwait(false);
        return Serialize(new { dragged = sourceSelector, droppedOn = targetSelector });
    }

    [McpServerTool]
    [Description("Fill input/textarea by selector with given text.")]
    public static async Task<string> FillAsync(
        [Description("Selector")] string selector,
        [Description("Text to fill")] string text,
        [Description("Timeout ms (default 10000)")] int? timeoutMs = 10000,
        CancellationToken cancellationToken = default)
    {
        var locator = await GetLocatorAsync(selector, timeoutMs, cancellationToken).ConfigureAwait(false);
        await locator.FillAsync(text ?? string.Empty, new LocatorFillOptions
        {
            Timeout = timeoutMs
        }).ConfigureAwait(false);
        return Serialize(new { filled = selector, length = text?.Length ?? 0 });
    }

    [McpServerTool]
    [Description("Type text into an element, optionally pressing Enter.")]
    public static async Task<string> TypeAsync(
        [Description("Selector")] string selector,
        [Description("Text to type")] string text,
        [Description("Submit (press Enter) after typing")] bool submit = false,
        [Description("Delay between keystrokes in ms")] int? delayMs = null,
        [Description("Timeout ms (default 10000)")] int? timeoutMs = 10000,
        CancellationToken cancellationToken = default)
    {
        var locator = await GetLocatorAsync(selector, timeoutMs, cancellationToken).ConfigureAwait(false);
        await locator.TypeAsync(text ?? string.Empty, new LocatorTypeOptions
        {
            Delay = delayMs,
            Timeout = timeoutMs
        }).ConfigureAwait(false);

        if (submit)
            await locator.PressAsync("Enter").ConfigureAwait(false);

        return Serialize(new { typed = selector, length = text?.Length ?? 0, submit });
    }

    [McpServerTool]
    [Description("Get innerText from the first matched element.")]
    public static async Task<string> InnerTextAsync(
        [Description("Selector")] string selector,
        [Description("Timeout ms (default 10000)")] int? timeoutMs = 10000,
        CancellationToken cancellationToken = default)
    {
        var locator = await GetLocatorAsync(selector, timeoutMs, cancellationToken).ConfigureAwait(false);
        var text = await locator.InnerTextAsync().ConfigureAwait(false);
        return Serialize(new { selector, text });
    }

    [McpServerTool]
    [Description("Press a single keyboard key on the active page.")]
    public static async Task<string> PressKeyAsync(
        [Description("Key, e.g. Enter or Control+S")] string key,
        [Description("Delay between keydown and keyup in ms")] int? delayMs = null,
        CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
        await page.Keyboard.PressAsync(key, new KeyboardPressOptions
        {
            Delay = delayMs
        }).ConfigureAwait(false);

        return Serialize(new { pressed = key, delayMs });
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
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
        var absolutePath = ResolveOutputPath(outputPath, ShotsDir);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        if (!string.IsNullOrWhiteSpace(selector))
        {
            var locator = await GetLocatorAsync(selector, 15000, cancellationToken).ConfigureAwait(false);
            await locator.ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = absolutePath,
                Timeout = 15000,
                Quality = quality
            }).ConfigureAwait(false);
        }
        else
        {
            await page.ScreenshotAsync(new PageScreenshotOptions
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
    [Description("Select one or more option values in a select element.")]
    public static async Task<string> SelectOptionAsync(
        [Description("Selector for the <select> element")] string selector,
        [Description("Values to select")] string[] values,
        [Description("Timeout ms (default 10000)")] int? timeoutMs = 10000,
        CancellationToken cancellationToken = default)
    {
        if (values is null || values.Length == 0)
            throw new ArgumentException("At least one value must be provided.", nameof(values));

        var locator = await GetLocatorAsync(selector, timeoutMs, cancellationToken).ConfigureAwait(false);
        var selected = await locator.SelectOptionAsync(
            values.Select(v => new SelectOptionValue { Value = v }).ToArray(),
            new LocatorSelectOptionOptions { Timeout = timeoutMs }).ConfigureAwait(false);

        return Serialize(new { selector, selected });
    }

    [McpServerTool]
    [Description("Upload local files to an <input type=\"file\"> element.")]
    public static async Task<string> UploadFilesAsync(
        [Description("Selector for file input")] string selector,
        [Description("Absolute or relative file paths")] string[] paths,
        [Description("Timeout ms (default 10000)")] int? timeoutMs = 10000,
        CancellationToken cancellationToken = default)
    {
        if (paths is null || paths.Length == 0)
            throw new ArgumentException("At least one path must be provided.", nameof(paths));

        var locator = await GetLocatorAsync(selector, timeoutMs, cancellationToken).ConfigureAwait(false);
        var absolutePaths = paths.Select(Path.GetFullPath).ToArray();
        foreach (var filePath in absolutePaths)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        await locator.SetInputFilesAsync(absolutePaths).ConfigureAwait(false);
        return Serialize(new { selector, count = absolutePaths.Length });
    }

    [McpServerTool]
    [Description("Fill multiple form fields by selector.")]
    public static async Task<string> FillFormAsync(
        [Description("Fields to fill")] FormField[] fields,
        [Description("Timeout ms per field (default 10000)")] int? timeoutMs = 10000,
        CancellationToken cancellationToken = default)
    {
        if (fields is null || fields.Length == 0)
            throw new ArgumentException("At least one field must be provided.", nameof(fields));

        var results = new List<object>();

        foreach (var field in fields)
        {
            var locator = await GetLocatorAsync(field.Selector, timeoutMs, cancellationToken).ConfigureAwait(false);

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

    [McpServerTool]
    [Description("Export current page as PDF (Chromium only).")]
    public static async Task<string> PdfAsync(
        [Description("Output path (*.pdf). If relative, saved under ./shots")] string outputPath,
        [Description("Paper format: A4/Letter... (optional)")] string? format = "A4",
        [Description("Print background")] bool printBackground = true,
        CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);

        if (!_isChromium)
            throw new NotSupportedException("PDF export is only supported in Chromium.");

        var absolutePath = ResolveOutputPath(outputPath, ShotsDir);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await page.PdfAsync(new PagePdfOptions
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
    [Description("Handle the next dialog by accepting or dismissing it.")]
    public static async Task<string> HandleDialogAsync(
        [Description("Accept dialog (true) or dismiss (false)")] bool accept = true,
        [Description("Prompt text when accepting")] string? promptText = null,
        [Description("Timeout ms (default 15000)")] int? timeoutMs = 15000,
        CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
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

    [McpServerTool]
    [Description("Wait for selector to be attached/visible/hidden/detached.")]
    public static async Task<string> WaitForSelectorAsync(
        [Description("Selector")] string selector,
        [Description("State: attached|visible|hidden|detached")] string? state = "visible",
        [Description("Timeout ms (default 15000)")] int? timeoutMs = 15000,
        CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
        var targetState = state?.ToLowerInvariant() switch
        {
            "attached" => WaitForSelectorState.Attached,
            "visible" => WaitForSelectorState.Visible,
            "hidden" => WaitForSelectorState.Hidden,
            "detached" => WaitForSelectorState.Detached,
            _ => WaitForSelectorState.Visible
        };

        await page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions
        {
            State = targetState,
            Timeout = timeoutMs
        }).ConfigureAwait(false);

        return Serialize(new { waited = selector, state = targetState.ToString() });
    }

    [McpServerTool]
    [Description("Wait for text to appear/disappear or for a specific time.")]
    public static async Task<string> WaitForAsync(
        [Description("Text to appear")] string? text = null,
        [Description("Text to disappear")] string? textGone = null,
        [Description("Wait this many ms regardless of text")] int? timeMs = null,
        [Description("Timeout ms for text waits (default 15000)")] int? timeoutMs = 15000,
        CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
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

    [McpServerTool]
    [Description("Evaluate JavaScript in page and return JSON-serializable result.")]
    public static async Task<string> EvalAsync(
        [Description("JS expression: must return JSON-serializable value")] string jsExpression,
        CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
        var result = await page.EvaluateAsync<object?>(jsExpression).ConfigureAwait(false);
        return Serialize(new { result });
    }

    [McpServerTool]
    [Description("Get current page URL.")]
    public static async Task<string> GetUrlAsync(CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
        return Serialize(new { url = page.Url });
    }

    [McpServerTool]
    [Description("Get captured console messages since the current page was opened/switch.")]
    public static async Task<string> ConsoleMessagesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        var messages = SnapshotConsoleMessages();
        return Serialize(new { messages });
    }

    [McpServerTool]
    [Description("List captured network requests since the current page was opened/switch.")]
    public static async Task<string> NetworkRequestsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        var requests = SnapshotNetworkRequests();
        return Serialize(new { requests });
    }

    [McpServerTool]
    [Description("Resize browser viewport.")]
    public static async Task<string> ResizeAsync(
        [Description("Viewport width")] int width,
        [Description("Viewport height")] int height,
        CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
        await page.SetViewportSizeAsync(width, height).ConfigureAwait(false);
        return Serialize(new { width, height });
    }

    [McpServerTool]
    [Description("Capture an accessibility snapshot of the page.")]
    public static async Task<string> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await page.Accessibility.SnapshotAsync().ConfigureAwait(false);
        return Serialize(new { snapshot });
    }

    [McpServerTool]
    [Description("Manage tabs: list, create, close or switch.")]
    public static async Task<string> TabsAsync(
        [Description("Action: list|new|close|switch")] string action,
        [Description("Zero-based tab index (for close/switch)")] int? index = null,
        CancellationToken cancellationToken = default)
    {
        var context = await GetContextAsync(cancellationToken).ConfigureAwait(false);

        switch (action.ToLowerInvariant())
        {
            case "list":
                var list = context.Pages.Select((p, i) => new
                {
                    index = i,
                    url = p.Url,
                    isClosed = p.IsClosed,
                    isActive = p == _page
                }).ToArray();
                return Serialize(new { tabs = list });

            case "new":
                var newPage = await context.NewPageAsync().ConfigureAwait(false);
                SetActivePage(newPage);
                return Serialize(new { created = context.Pages.Count, activeIndex = GetPageIndex(newPage) });

            case "switch":
                if (index is null)
                    throw new ArgumentException("Index required for switch action.", nameof(index));
                if (index < 0 || index >= context.Pages.Count)
                    throw new ArgumentOutOfRangeException(nameof(index), index, "Tab index out of range.");
                var switchPage = context.Pages[index.Value];
                if (switchPage.IsClosed)
                    throw new InvalidOperationException("Cannot switch to a closed tab.");
                SetActivePage(switchPage);
                return Serialize(new { activeIndex = index, url = switchPage.Url });

            case "close":
                if (index is null)
                    throw new ArgumentException("Index required for close action.", nameof(index));
                if (index < 0 || index >= context.Pages.Count)
                    throw new ArgumentOutOfRangeException(nameof(index), index, "Tab index out of range.");
                var target = context.Pages[index.Value];
                var wasActive = target == _page;
                await target.CloseAsync().ConfigureAwait(false);

                var remaining = context.Pages.FirstOrDefault(p => !p.IsClosed);
                if (remaining is null)
                {
                    var replacement = await context.NewPageAsync().ConfigureAwait(false);
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

    [McpServerTool]
    [Description("Install Playwright browsers using bundled CLI.")]
    public static async Task<string> InstallAsync(
        [Description("Optional browser name, e.g. chromium")] string? browser = null,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "install" };
        if (!string.IsNullOrWhiteSpace(browser))
            args.Add(browser);

        var exitCode = Microsoft.Playwright.Program.Main(args.ToArray());
        return Serialize(new { success = exitCode == 0, exitCode, browser });
    }

    [McpServerTool]
    [Description("Move mouse to coordinates relative to page viewport.")]
    public static async Task<string> MouseMoveAsync(
        [Description("X coordinate")] double x,
        [Description("Y coordinate")] double y,
        [Description("Number of move steps")] int? steps = null,
        CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
        await page.Mouse.MoveAsync((float)x, (float)y, new MouseMoveOptions { Steps = steps }).ConfigureAwait(false);
        return Serialize(new { x, y, steps });
    }

    [McpServerTool]
    [Description("Click mouse at coordinates relative to page viewport.")]
    public static async Task<string> MouseClickAsync(
        [Description("X coordinate")] double x,
        [Description("Y coordinate")] double y,
        [Description("Button: left|middle|right")] string? button = "left",
        [Description("Number of clicks")] int clickCount = 1,
        CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
        await page.Mouse.ClickAsync((float)x, (float)y, new MouseClickOptions
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

    [McpServerTool]
    [Description("Drag mouse between coordinates relative to viewport.")]
    public static async Task<string> MouseDragAsync(
        [Description("Start X")] double startX,
        [Description("Start Y")] double startY,
        [Description("End X")] double endX,
        [Description("End Y")] double endY,
        [Description("Steps for the move")] int? steps = 25,
        CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
        await page.Mouse.MoveAsync((float)startX, (float)startY).ConfigureAwait(false);
        await page.Mouse.DownAsync().ConfigureAwait(false);
        await page.Mouse.MoveAsync((float)endX, (float)endY, new MouseMoveOptions { Steps = steps }).ConfigureAwait(false);
        await page.Mouse.UpAsync().ConfigureAwait(false);
        return Serialize(new { startX, startY, endX, endY, steps });
    }

    [McpServerTool]
    [Description("Start Playwright tracing (snapshots, screenshots, sources).")]
    public static async Task<string> StartTracingAsync(CancellationToken cancellationToken = default)
    {
        var context = await GetContextAsync(cancellationToken).ConfigureAwait(false);

        if (_tracingActive)
            return Serialize(new { tracing = true, alreadyStarted = true });

        await context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true
        }).ConfigureAwait(false);

        _tracingActive = true;
        return Serialize(new { tracing = true });
    }

    [McpServerTool]
    [Description("Stop tracing and save to a .zip file.")]
    public static async Task<string> StopTracingAsync(
        [Description("Output path for trace .zip (default ./traces/trace-<timestamp>.zip")] string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        var context = await GetContextAsync(cancellationToken).ConfigureAwait(false);

        if (!_tracingActive)
            return Serialize(new { tracing = false, alreadyStopped = true });

        EnsureDirectories();
        var path = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(TracesDir, $"trace-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip")
            : Path.GetFullPath(outputPath);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await context.Tracing.StopAsync(new TracingStopOptions
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
}

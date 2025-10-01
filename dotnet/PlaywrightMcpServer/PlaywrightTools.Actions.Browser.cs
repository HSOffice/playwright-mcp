using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using ModelContextProtocol.Server;

namespace PlaywrightMcpServer;

public sealed partial class PlaywrightTools
{
    /// <summary>
    /// 关闭并释放当前 Playwright 会话涉及的页面、上下文和浏览器等所有资源，确保不会留下后台进程。
    /// </summary>
    /// <param name="cancellationToken">用于在需要时取消关闭流程的取消令牌。</param>
    /// <returns>表示关闭结果的 JSON 字符串。</returns>
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

    /// <summary>
    /// 重新启动浏览器会话：先彻底关闭现有实例，再按配置重新拉起浏览器并打开新的空白页面。
    /// </summary>
    /// <param name="cancellationToken">用于在启动过程中取消操作的取消令牌。</param>
    /// <returns>包含重新启动状态与核心运行信息的 JSON 字符串。</returns>
    [McpServerTool]
    [Description("(Re)launch browser and open a fresh page.")]
    public static async Task<string> RelaunchAsync(CancellationToken cancellationToken = default)
    {
        await CloseAsync(cancellationToken).ConfigureAwait(false);
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        return Serialize(new { relaunched = true, headless = Headless, engine = _isChromium ? "chromium" : "unknown" });
    }

    /// <summary>
    /// 调用 Playwright 自带的 CLI 安装指定浏览器内核，用于在缺少依赖时快速补齐运行环境。
    /// </summary>
    /// <param name="browser">需要安装的浏览器名称，例如 "chromium"，留空则安装所有默认依赖。</param>
    /// <param name="cancellationToken">预留的取消令牌（当前未使用）。</param>
    /// <returns>包含安装是否成功及退出码的 JSON 字符串。</returns>
    [McpServerTool]
    [Description("Install Playwright browsers using bundled CLI.")]
    public static async Task<string> InstallAsync(
        [Description("Optional browser name, e.g. chromium")] string? browser = null,
        CancellationToken cancellationToken = default)
    {
        var args = new System.Collections.Generic.List<string> { "install" };
        if (!string.IsNullOrWhiteSpace(browser))
            args.Add(browser);

        var exitCode = Microsoft.Playwright.Program.Main(args.ToArray());
        await Task.CompletedTask.ConfigureAwait(false);
        return Serialize(new { success = exitCode == 0, exitCode, browser });
    }

    /// <summary>
    /// 在当前浏览器上下文中开启 Playwright Tracing，捕获快照、截图与源码，便于后续调试分析。
    /// </summary>
    /// <param name="cancellationToken">用于在跟踪启动期间取消操作的取消令牌。</param>
    /// <returns>指示跟踪是否已开启的 JSON 字符串。</returns>
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

    /// <summary>
    /// 停止当前的 Tracing 会话，并将捕获到的调试信息保存为 zip 文件，方便下载或共享。
    /// </summary>
    /// <param name="outputPath">自定义的 trace zip 输出路径，为空时自动生成时间戳文件名。</param>
    /// <param name="cancellationToken">用于在停止过程中取消操作的取消令牌。</param>
    /// <returns>包含跟踪结果和输出路径的 JSON 字符串。</returns>
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
}

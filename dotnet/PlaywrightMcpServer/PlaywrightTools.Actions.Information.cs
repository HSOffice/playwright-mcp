using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using ModelContextProtocol.Server;

namespace PlaywrightMcpServer;

public sealed partial class PlaywrightTools
{
    /// <summary>
    /// 读取第一个匹配元素的 <c>innerText</c> 文本内容，用于快速提取页面信息。
    /// </summary>
    /// <param name="selector">目标元素选择器。</param>
    /// <param name="timeoutMs">等待元素出现的超时时间（毫秒）。</param>
    /// <param name="cancellationToken">用于取消等待的取消令牌。</param>
    /// <returns>包含提取文本的 JSON 字符串。</returns>
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

    /// <summary>
    /// 在页面上下文执行 JavaScript 表达式，并返回可 JSON 序列化的结果，便于自定义数据读取。
    /// </summary>
    /// <param name="jsExpression">需要执行的 JS 表达式。</param>
    /// <param name="cancellationToken">用于取消执行的取消令牌。</param>
    /// <returns>包含执行结果的 JSON 字符串。</returns>
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

    /// <summary>
    /// 返回从当前活动页面开始收集到的浏览器控制台输出，方便调试日志分析。
    /// </summary>
    /// <param name="cancellationToken">用于在读取时取消操作的取消令牌。</param>
    /// <returns>包含控制台日志数组的 JSON 字符串。</returns>
    [McpServerTool]
    [Description("Get captured console messages since the current page was opened/switch.")]
    public static async Task<string> ConsoleMessagesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        var messages = SnapshotConsoleMessages();
        return Serialize(new { messages });
    }

    /// <summary>
    /// 列出自当前页面打开以来记录的网络请求及其响应状态，可用于排查接口调用情况。
    /// </summary>
    /// <param name="cancellationToken">用于取消读取的取消令牌。</param>
    /// <returns>包含网络请求列表的 JSON 字符串。</returns>
    [McpServerTool]
    [Description("List captured network requests since the current page was opened/switch.")]
    public static async Task<string> NetworkRequestsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLaunchedAsync(cancellationToken).ConfigureAwait(false);
        var requests = SnapshotNetworkRequests();
        return Serialize(new { requests });
    }

    /// <summary>
    /// 调整浏览器视口尺寸，便于模拟不同设备屏幕或响应式布局测试。
    /// </summary>
    /// <param name="width">视口宽度像素值。</param>
    /// <param name="height">视口高度像素值。</param>
    /// <param name="cancellationToken">用于取消调整的取消令牌。</param>
    /// <returns>包含新尺寸的 JSON 字符串。</returns>
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

    /// <summary>
    /// 生成页面的辅助功能树快照，可用于分析无障碍结构或验证可访问性实现。
    /// </summary>
    /// <param name="cancellationToken">用于在采集期间取消操作的取消令牌。</param>
    /// <returns>包含可访问性树数据的 JSON 字符串。</returns>
    [McpServerTool]
    [Description("Capture an accessibility snapshot of the page.")]
    public static async Task<string> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await page.Accessibility.SnapshotAsync().ConfigureAwait(false);
        return Serialize(new { snapshot });
    }
}

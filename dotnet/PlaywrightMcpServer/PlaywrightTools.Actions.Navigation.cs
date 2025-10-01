using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using ModelContextProtocol.Server;

namespace PlaywrightMcpServer;

public sealed partial class PlaywrightTools
{
    /// <summary>
    /// 打开指定的网址并等待页面进入 <c>load</c> 状态，确保主要资源加载完成后再返回结果。
    /// </summary>
    /// <param name="url">目标网址，支持 http(s) 等常见协议。</param>
    /// <param name="timeoutMs">加载超时时间（毫秒），超时将抛出异常。</param>
    /// <param name="cancellationToken">用于取消导航操作的取消令牌。</param>
    /// <returns>包含最终访问地址与响应状态信息的 JSON 字符串。</returns>
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

    /// <summary>
    /// 在浏览历史中尝试后退一次，常用于返回上一页，若无法后退则返回当前页面信息。
    /// </summary>
    /// <param name="timeoutMs">等待页面完成后退加载的超时时间（毫秒）。</param>
    /// <param name="cancellationToken">用于在等待期间取消操作的取消令牌。</param>
    /// <returns>包含后退后页面地址及响应状态的 JSON 字符串。</returns>
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

    /// <summary>
    /// 获取当前活动页面的地址栏 URL，可用于确认跳转结果或调试导航行为。
    /// </summary>
    /// <param name="cancellationToken">用于在读取过程中取消操作的取消令牌。</param>
    /// <returns>包含当前 URL 的 JSON 字符串。</returns>
    [McpServerTool]
    [Description("Get current page URL.")]
    public static async Task<string> GetUrlAsync(CancellationToken cancellationToken = default)
    {
        var page = await GetPageAsync(cancellationToken).ConfigureAwait(false);
        return Serialize(new { url = page.Url });
    }

    /// <summary>
    /// 对当前浏览器上下文中的标签页进行管理，可列出、创建、关闭或切换到指定索引的标签页。
    /// </summary>
    /// <param name="action">操作类型：list、new、close 或 switch。</param>
    /// <param name="index">当操作需要目标标签页时提供的索引（从 0 开始）。</param>
    /// <param name="cancellationToken">用于在管理过程中取消操作的取消令牌。</param>
    /// <returns>描述标签页操作结果的 JSON 字符串。</returns>
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
}

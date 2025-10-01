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
    /// 等待指定选择器达到特定状态（如出现、隐藏或分离），常用于同步页面动态内容。
    /// </summary>
    /// <param name="selector">目标元素的选择器表达式。</param>
    /// <param name="state">期望的状态：attached、visible、hidden 或 detached。</param>
    /// <param name="timeoutMs">等待超时时间（毫秒）。</param>
    /// <param name="cancellationToken">用于在等待过程中取消操作的取消令牌。</param>
    /// <returns>包含最终状态信息的 JSON 字符串。</returns>
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

    /// <summary>
    /// 通过等待文本出现、消失或纯延时的方式协调页面节奏，支持组合多种等待条件。
    /// </summary>
    /// <param name="text">需要等待出现的文本内容。</param>
    /// <param name="textGone">需要等待消失的文本内容。</param>
    /// <param name="timeMs">无条件等待的时间（毫秒）。</param>
    /// <param name="timeoutMs">文本检测的超时时间（毫秒）。</param>
    /// <param name="cancellationToken">用于取消等待操作的取消令牌。</param>
    /// <returns>包含等待结果与满足条件的 JSON 字符串。</returns>
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
}

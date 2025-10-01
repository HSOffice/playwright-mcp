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
    /// 监听下一次弹出框（alert/confirm/prompt），根据参数选择接受或拒绝，并返回对话框详情。
    /// </summary>
    /// <param name="accept">为 <c>true</c> 时接受对话框，否则执行取消。</param>
    /// <param name="promptText">在提示框上接受时填写的文本。</param>
    /// <param name="timeoutMs">等待弹窗出现的最长时间（毫秒）。</param>
    /// <param name="cancellationToken">用于提前取消等待的取消令牌。</param>
    /// <returns>指示是否处理成功及弹窗元信息的 JSON 字符串。</returns>
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
}

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
    /// 捕获当前页面或指定元素的截图，支持全页、局部以及 JPEG 质量设置，并自动处理输出路径。
    /// </summary>
    /// <param name="outputPath">截图文件保存路径，可为相对或绝对路径。</param>
    /// <param name="selector">若提供，将只截取匹配元素区域。</param>
    /// <param name="fullPage">在未提供选择器时，是否截图整个页面。</param>
    /// <param name="quality">JPEG 图像质量（0-100），PNG 时忽略。</param>
    /// <param name="cancellationToken">用于取消截图任务的取消令牌。</param>
    /// <returns>包含截图文件大小、路径及选区信息的 JSON 字符串。</returns>
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

    /// <summary>
    /// 将当前页面导出为 PDF 文件，仅支持 Chromium 引擎，可自定义纸张规格与是否包含背景。
    /// </summary>
    /// <param name="outputPath">PDF 输出路径，支持相对路径（默认保存至 shots 目录）。</param>
    /// <param name="format">纸张规格，如 A4、Letter 等。</param>
    /// <param name="printBackground">是否渲染页面背景。</param>
    /// <param name="cancellationToken">用于取消导出操作的取消令牌。</param>
    /// <returns>包含 PDF 文件路径、大小和配置的 JSON 字符串。</returns>
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
}

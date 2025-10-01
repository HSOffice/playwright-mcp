using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using ModelContextProtocol.Server;

namespace PlaywrightMcpServer;

public sealed partial class PlaywrightTools
{
    /// <summary>
    /// 根据选择器定位元素并执行单击操作，支持 CSS、XPath 或文本前缀语法，常用于触发按钮或链接。
    /// </summary>
    /// <param name="selector">元素定位语句，支持 <c>xpath=</c>、<c>text=</c> 等前缀。</param>
    /// <param name="timeoutMs">等待目标元素可交互的超时时间（毫秒）。</param>
    /// <param name="cancellationToken">用于在交互过程中取消操作的取消令牌。</param>
    /// <returns>描述点击目标的 JSON 字符串。</returns>
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

    /// <summary>
    /// 将鼠标悬停在匹配的元素上，适合触发悬浮菜单或工具提示等依赖 hover 的交互效果。
    /// </summary>
    /// <param name="selector">需要悬停的元素选择器。</param>
    /// <param name="timeoutMs">等待元素可用的超时毫秒数。</param>
    /// <param name="cancellationToken">用于取消等待的取消令牌。</param>
    /// <returns>包含已悬停元素信息的 JSON 字符串。</returns>
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

    /// <summary>
    /// 执行拖放操作，将源元素拖动到目标元素上，适用于排序、文件拖放等场景。
    /// </summary>
    /// <param name="sourceSelector">被拖动的元素定位表达式。</param>
    /// <param name="targetSelector">放置目标元素的选择器。</param>
    /// <param name="timeoutMs">整个拖放过程的最大等待时间（毫秒）。</param>
    /// <param name="cancellationToken">用于中断操作的取消令牌。</param>
    /// <returns>记录拖放双方的 JSON 字符串。</returns>
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

    /// <summary>
    /// 向输入框或文本域写入指定文本，自动清空原有内容后填充，适合表单快速填写。
    /// </summary>
    /// <param name="selector">目标输入控件的选择器。</param>
    /// <param name="text">需要填入的文本，为空时写入空字符串。</param>
    /// <param name="timeoutMs">等待元素可编辑的超时时间。</param>
    /// <param name="cancellationToken">用于取消等待或填写的取消令牌。</param>
    /// <returns>包含填写长度等信息的 JSON 字符串。</returns>
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

    /// <summary>
    /// 模拟逐字输入文本，可设置击键延迟并在完成后选择性触发 Enter，用于搜索框或即时验证场景。
    /// </summary>
    /// <param name="selector">目标元素选择器。</param>
    /// <param name="text">要输入的文本内容。</param>
    /// <param name="submit">是否在输入完成后自动发送 Enter 键。</param>
    /// <param name="delayMs">每次按键之间的延迟毫秒数。</param>
    /// <param name="timeoutMs">等待元素可输入的超时设置。</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
    /// <returns>包含输入长度与是否提交等信息的 JSON 字符串。</returns>
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

    /// <summary>
    /// 在当前页面级别模拟一次键盘按键，包括可选的组合键与按键按下时间间隔。
    /// </summary>
    /// <param name="key">要发送的键值，例如 "Enter" 或 "Control+S"。</param>
    /// <param name="delayMs">键按下和释放之间的延迟毫秒数。</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
    /// <returns>记录按下按键的 JSON 字符串。</returns>
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

    /// <summary>
    /// 在下拉框中勾选或选择指定的选项值，可支持多选表单控件。
    /// </summary>
    /// <param name="selector">目标 <c>&lt;select&gt;</c> 元素的定位选择器。</param>
    /// <param name="values">需要选中的选项值集合。</param>
    /// <param name="timeoutMs">等待控件可操作的超时时间。</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
    /// <returns>描述成功选中的选项集合的 JSON 字符串。</returns>
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

    /// <summary>
    /// 将本地文件上传到文件输入框，自动解析相对路径并校验文件是否存在。
    /// </summary>
    /// <param name="selector">文件上传控件的选择器。</param>
    /// <param name="paths">需要上传的文件路径列表。</param>
    /// <param name="timeoutMs">等待输入框准备就绪的超时时间。</param>
    /// <param name="cancellationToken">用于取消上传流程的取消令牌。</param>
    /// <returns>包含上传文件数量的 JSON 字符串。</returns>
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

    /// <summary>
    /// 根据给定字段集合批量填写或选择表单元素，支持按需混合文本输入与下拉选择操作。
    /// </summary>
    /// <param name="fields">待处理的表单字段配置数组。</param>
    /// <param name="timeoutMs">每个字段的最大等待时间（毫秒）。</param>
    /// <param name="cancellationToken">用于在长时间执行时取消操作的取消令牌。</param>
    /// <returns>包含各字段操作结果的 JSON 字符串。</returns>
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

    /// <summary>
    /// 将鼠标移动到页面视口内的指定坐标，可搭配后续拖拽或点击动作使用。
    /// </summary>
    /// <param name="x">目标横向坐标（像素）。</param>
    /// <param name="y">目标纵向坐标（像素）。</param>
    /// <param name="steps">平滑移动所需的步数，留空则使用 Playwright 默认值。</param>
    /// <param name="cancellationToken">用于取消鼠标移动的取消令牌。</param>
    /// <returns>包含鼠标移动参数的 JSON 字符串。</returns>
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

    /// <summary>
    /// 在给定坐标模拟鼠标点击，可指定按键类型与连击次数，适合画布或自定义控件交互。
    /// </summary>
    /// <param name="x">点击位置的横坐标。</param>
    /// <param name="y">点击位置的纵坐标。</param>
    /// <param name="button">使用的鼠标按键（left、middle、right）。</param>
    /// <param name="clickCount">连续点击次数，例如 2 表示双击。</param>
    /// <param name="cancellationToken">用于取消点击操作的取消令牌。</param>
    /// <returns>记录鼠标点击参数的 JSON 字符串。</returns>
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

    /// <summary>
    /// 通过坐标模拟鼠标按下、拖动和释放，可指定移动步数来控制拖拽轨迹的平滑度。
    /// </summary>
    /// <param name="startX">拖拽起点的横坐标。</param>
    /// <param name="startY">拖拽起点的纵坐标。</param>
    /// <param name="endX">拖拽终点的横坐标。</param>
    /// <param name="endY">拖拽终点的纵坐标。</param>
    /// <param name="steps">拖动过程中插值的步数。</param>
    /// <param name="cancellationToken">用于取消拖拽动作的取消令牌。</param>
    /// <returns>包含拖拽路径参数的 JSON 字符串。</returns>
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
}

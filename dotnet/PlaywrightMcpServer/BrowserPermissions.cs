using System.Threading.Tasks;
using Microsoft.Playwright;

namespace ChatUiTest.MCP.Playwright
{
    /// <summary>
    /// 常见的 BrowserContext 权限定义（Playwright）
    /// </summary>
    public static class BrowserPermissions
    {
        /// <summary>
        /// 地理位置（navigator.geolocation）  
        /// ✅ 常见浏览器支持  
        /// ⚠️ 需要同时设置 Geolocation 才会返回坐标
        /// </summary>
        public const string Geolocation = "geolocation";

        /// <summary>
        /// 通知（Notification API / Push API）  
        /// ✅ 常见浏览器支持
        /// </summary>
        public const string Notifications = "notifications";

        /// <summary>
        /// 摄像头（getUserMedia video track）  
        /// ✅ 常见浏览器支持
        /// </summary>
        public const string Camera = "camera";

        /// <summary>
        /// 麦克风（getUserMedia audio track）  
        /// ✅ 常见浏览器支持
        /// </summary>
        public const string Microphone = "microphone";

        /// <summary>
        /// 剪贴板读取（navigator.clipboard.readText 等）  
        /// ✅ Chrome / Edge / Safari 新版支持  
        /// ⚠️ Firefox 部分限制
        /// </summary>
        public const string ClipboardRead = "clipboard-read";

        /// <summary>
        /// 剪贴板写入（navigator.clipboard.writeText 等）  
        /// ✅ 常见浏览器支持
        /// </summary>
        public const string ClipboardWrite = "clipboard-write";

        /// <summary>
        /// Web MIDI API（不含 SysEx）  
        /// ✅ Chrome 支持  
        /// ❌ Safari/Firefox 不支持
        /// </summary>
        public const string Midi = "midi";

        /// <summary>
        /// Web MIDI API（含 SysEx）  
        /// ✅ Chrome 支持（需额外 flag）  
        /// ❌ 其他浏览器基本不支持
        /// </summary>
        public const string MidiSysEx = "midi-sysex";

        /// <summary>
        /// 后台同步（Background Sync API）  
        /// ✅ Chrome / Edge 支持  
        /// ❌ Safari/Firefox 不支持
        /// </summary>
        public const string BackgroundSync = "background-sync";

        /// <summary>
        /// 环境光传感器（Ambient Light Sensor API）  
        /// ⚠️ 少数浏览器实验性支持
        /// </summary>
        public const string AmbientLightSensor = "ambient-light-sensor";

        /// <summary>
        /// 加速度传感器（Accelerometer API）  
        /// ⚠️ 少数浏览器实验性支持
        /// </summary>
        public const string Accelerometer = "accelerometer";

        /// <summary>
        /// 陀螺仪（Gyroscope API）  
        /// ⚠️ 少数浏览器实验性支持
        /// </summary>
        public const string Gyroscope = "gyroscope";

        /// <summary>
        /// 磁力计（Magnetometer API）  
        /// ⚠️ 少数浏览器实验性支持
        /// </summary>
        public const string Magnetometer = "magnetometer";

        /// <summary>
        /// Payment Request API / Payment Handler API  
        /// ✅ Chrome / Edge 支持  
        /// ❌ Safari 仅部分支持  
        /// ❌ Firefox 大部分禁用
        /// </summary>
        public const string PaymentHandler = "payment-handler";

        /// <summary>
        /// Web NFC API  
        /// ✅ 仅 Android + Chrome 89+ 支持（需设备具备 NFC 硬件）  
        /// ❌ 桌面浏览器 / iOS 不支持
        /// </summary>
        public const string Nfc = "nfc";

        /// <summary>
        /// Storage Access API（第三方 Cookie / Storage 访问请求）  
        /// ✅ Chrome / Safari 新版支持  
        /// ⚠️ Firefox 部分实验性支持
        /// </summary>
        public const string StorageAccess = "storage-access";

        /// <summary>
        /// Local Fonts Access API（访问用户本地字体）  
        /// ✅ Chromium 系列新版本支持  
        /// ❌ Safari/Firefox 不支持
        /// </summary>
        public const string LocalFonts = "local-fonts";
    }

    public static class BrowserContextExtensions
    {
        /// <summary>
        /// 授予 Playwright 常见的所有权限。
        /// </summary>
        /// <param name="context">BrowserContext</param>
        /// <param name="origin">可选：限制授权的站点（为空则对所有站点生效）</param>
        public static async Task GrantAllPermissionsAsync(this IBrowserContext context, string? origin = null)
        {
            var permissions = new[]
            {
                BrowserPermissions.Geolocation,
                BrowserPermissions.Notifications,
                BrowserPermissions.Camera,
                BrowserPermissions.Microphone,
                BrowserPermissions.ClipboardRead,
                BrowserPermissions.ClipboardWrite,
                BrowserPermissions.Midi,
                BrowserPermissions.MidiSysEx,
                BrowserPermissions.BackgroundSync,
                BrowserPermissions.AmbientLightSensor,
                BrowserPermissions.Accelerometer,
                BrowserPermissions.Gyroscope,
                BrowserPermissions.Magnetometer,
                BrowserPermissions.PaymentHandler,
                //BrowserPermissions.Nfc,
                BrowserPermissions.StorageAccess,
                BrowserPermissions.LocalFonts
            };

            if (origin is null)
            {
                await context.GrantPermissionsAsync(permissions);
            }
            else
            {
                await context.GrantPermissionsAsync(permissions, new BrowserContextGrantPermissionsOptions
                {
                    Origin = origin
                });
            }
        }

        /// <summary>
        /// 撤销当前 Context 中的所有授权（恢复默认提示/询问行为）。
        /// </summary>
        public static async Task RevokeAllPermissionsAsync(this IBrowserContext context)
        {
            await context.ClearPermissionsAsync();
        }
    }
}

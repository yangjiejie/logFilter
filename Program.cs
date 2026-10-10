using System;
using System.Text;
using Avalonia;

namespace LogFilterApp;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 支持 GB18030/GBK 编码的线上日志
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

// 📂 文件:src/MotionControl/Program.cs
namespace MotionControlProject;

/// <summary>程序入口。net8 WinForms 模板写法:ApplicationConfiguration.Initialize() 负责高 DPI / 字体 / 默认样式。</summary>
internal static class Program
{
    [STAThread]   // WinForms 硬要求:UI 线程必须是 STA(剪贴板、文件对话框等 COM 组件依赖)
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        // 想接真卡时,只改这一行:new UI.MainForm(new Device.XxxRealCard("192.168.0.10"))
        Application.Run(new UI.MainForm());
    }
}

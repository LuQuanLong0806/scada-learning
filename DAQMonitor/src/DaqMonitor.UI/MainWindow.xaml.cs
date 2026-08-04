using System.Globalization;
using System.Windows;
using System.Windows.Data;
using DaqMonitor.UI.ViewModels;
using DaqMonitor.UI.Views;

namespace DaqMonitor.UI;

/// <summary>把 bool 取反，给按钮的 IsEnabled 用。</summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, System.Type _, object __, CultureInfo ___)
        => value is bool b ? !b : true;
    public object ConvertBack(object _, System.Type __, object ___, CultureInfo ____) => Binding.DoNothing;
}

/// <summary>把采集状态显示成文字。</summary>
public class RunningTextConverter : IValueConverter
{
    public object Convert(object value, System.Type _, object __, CultureInfo ___)
        => value is bool b && b ? "采集中" : "已停止";
    public object ConvertBack(object _, System.Type __, object ___, CultureInfo ____) => Binding.DoNothing;
}

public partial class MainWindow : Window
{
    public MainWindow()
    {
        // 在 XAML 里用到的两个 Converter 需要事先放进资源
        Resources.Add("InverseBool", new InverseBoolConverter());
        Resources.Add("RunningText", new RunningTextConverter());
        InitializeComponent();
        // 订阅 DataContext 变化:当 VM 注入后,把曲线页和配方页接到 VM
        DataContextChanged += MainWindow_DataContextChanged;
    }

    private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            if (ChartTab is not null) vm.AttachChart(ChartTab);
            // M18:配方管理 Tab —— 由 VM 内嵌的 RecipeManagementViewModel 构造 View
            // (UserControl 不能用 DI 自动注入,所以手动 new)
            if (RecipeTab is not null && vm.Recipes is not null)
            {
                RecipeTab.Content = new RecipeManagementView(vm.Recipes);
            }
        }
    }
}

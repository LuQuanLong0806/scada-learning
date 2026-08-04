using System.Windows.Controls;
using DaqMonitor.UI.ViewModels;

namespace DaqMonitor.UI.Views;

/// <summary>
/// 配方管理视图 code-behind。
/// 故意保持极简 —— 真正的业务在 RecipeManagementViewModel 里(MVVM 一致性)。
/// </summary>
public partial class RecipeManagementView : UserControl
{
    private readonly RecipeManagementViewModel _vm;

    /// <summary>由 MainViewModel 在 DI 装配时调用。</summary>
    public RecipeManagementView(RecipeManagementViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += async (_, _) => await vm.LoadAsync();
    }
}

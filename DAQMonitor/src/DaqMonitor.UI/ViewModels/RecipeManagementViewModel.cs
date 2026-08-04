using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using DaqMonitor.Core.Auth;
using DaqMonitor.Core.Recipes;
using DaqMonitor.UI.Views;
using Microsoft.Win32;

namespace DaqMonitor.UI.ViewModels;

/// <summary>
/// 配方管理 ViewModel:负责配方 CRUD + 激活 + 导入导出 + 历史快照展示。
///
/// 类比前端:这就是"配方管理"页的 React/Vue 组件 state —— list + selected + actions。
/// 权限点:Operator 只能"激活"(换产品),Engineer+ 才能 CRUD/Import/Export。
/// </summary>
public class RecipeManagementViewModel : INotifyPropertyChanged
{
    private readonly RecipeService _svc;
    private readonly ICurrentUserService _current;
    private Recipe? _selected;
    private string _statusText = "";

    public ObservableCollection<Recipe> Recipes { get; } = new();
    public ObservableCollection<RecipeParameter> Parameters { get; } = new();

    public Recipe? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            OnChanged();
            RefreshParameters();
            // 选中项变化 → 所有命令的可执行性跟着变
            ActivateCommand?.RaiseCanExecuteChanged();
            EditCommand?.RaiseCanExecuteChanged();
            DeleteCommand?.RaiseCanExecuteChanged();
            ExportCommand?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>当前激活配方名(头部 + 状态栏用)。</summary>
    public string ActiveRecipeName { get; private set; } = "(未激活)";

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnChanged(); }
    }

    // —— 权限:Operator 只能 Activate,Engineer+ 才能 CRUD ——
    public bool CanEditRecipes => _current.HasPermission(Permissions.RecipeEdit);

    public RecipeDelegateCommand ActivateCommand { get; }
    public RecipeDelegateCommand NewCommand { get; }
    public RecipeDelegateCommand EditCommand { get; }
    public RecipeDelegateCommand DeleteCommand { get; }
    public RecipeDelegateCommand ExportCommand { get; }
    public RecipeDelegateCommand ImportCommand { get; }
    public RecipeDelegateCommand RefreshCommand { get; }

    public RecipeManagementViewModel(RecipeService svc, ICurrentUserService current)
    {
        _svc = svc;
        _current = current;

        ActivateCommand = new RecipeDelegateCommand(
            _ => { _ = ActivateAsync(); },
            _ => Selected is not null && !Selected.IsActive);
        NewCommand = new RecipeDelegateCommand(
            _ => NewRecipe(),
            _ => CanEditRecipes);
        EditCommand = new RecipeDelegateCommand(
            _ => EditRecipe(),
            _ => CanEditRecipes && Selected is not null);
        DeleteCommand = new RecipeDelegateCommand(
            _ => { _ = DeleteAsync(); },
            _ => CanEditRecipes && Selected is not null);
        ExportCommand = new RecipeDelegateCommand(
            _ => Export(),
            _ => CanEditRecipes && Selected is not null);
        ImportCommand = new RecipeDelegateCommand(
            _ => Import(),
            _ => CanEditRecipes);
        RefreshCommand = new RecipeDelegateCommand(_ => { _ = LoadAsync(); });
    }

    /// <summary>初始化加载配方列表 + 同步激活态到头部显示。</summary>
    public async Task LoadAsync()
    {
        Recipes.Clear();
        var list = await _svc.ListAsync();
        foreach (var r in list) Recipes.Add(r);

        var active = await _svc.GetActiveAsync();
        ActiveRecipeName = active?.Name ?? "(未激活)";
        OnChanged(nameof(ActiveRecipeName));
        StatusText = $"已加载 {list.Count} 个配方 · 当前激活:{ActiveRecipeName}";
    }

    private void RefreshParameters()
    {
        Parameters.Clear();
        if (_selected is null) return;
        try
        {
            var ps = JsonSerializer.Deserialize<List<RecipeParameter>>(_selected.ParametersJson) ?? new();
            foreach (var p in ps) Parameters.Add(p);
        }
        catch (Exception ex)
        {
            StatusText = $"参数 JSON 解析失败:{ex.Message}";
        }
    }

    private async Task ActivateAsync()
    {
        if (_selected is null) return;
        try
        {
            await _svc.ActivateAsync(_selected.Id);
            StatusText = $"已激活配方:{_selected.Name}";
            await LoadAsync();
        }
        catch (Exception ex) { StatusText = $"激活失败:{ex.Message}"; }
    }

    private void NewRecipe()
    {
        var dlg = new RecipeEditWindow();
        if (dlg.ShowDialog() != true) return;

        var recipe = _svc.CreateAsync(
            dlg.RecipeName,
            dlg.Description,
            dlg.Parameters).GetAwaiter().GetResult();
        StatusText = $"已创建配方:{recipe.Name}(v{recipe.Version})";
        LoadAsync().GetAwaiter().GetResult();
    }

    private void EditRecipe()
    {
        if (_selected is null) return;
        var existing = JsonSerializer.Deserialize<List<RecipeParameter>>(_selected.ParametersJson) ?? new();

        var dlg = new RecipeEditWindow(
            _selected.Name, _selected.Description, existing);
        if (dlg.ShowDialog() != true) return;

        _svc.UpdateAsync(_selected.Id, dlg.RecipeName, dlg.Description, dlg.Parameters)
            .GetAwaiter().GetResult();
        StatusText = $"已保存:{dlg.RecipeName}(版本号自增)";
        LoadAsync().GetAwaiter().GetResult();
    }

    private async Task DeleteAsync()
    {
        if (_selected is null) return;
        if (MessageBox.Show($"确定软删除配方 \"{_selected.Name}\"?\n(历史快照保留,可追溯)",
            "确认", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;

        await _svc.DeleteAsync(_selected.Id);
        StatusText = $"已软删除:{_selected.Name}";
        await LoadAsync();
    }

    private void Export()
    {
        if (_selected is null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "配方 JSON|*.recipe.json",
            FileName = $"{_selected.Name}.v{_selected.Version}.recipe.json"
        };
        if (dlg.ShowDialog() != true) return;

        var json = _svc.ExportAsync(_selected.Id).GetAwaiter().GetResult();
        System.IO.File.WriteAllText(dlg.FileName, json);
        StatusText = $"已导出 → {dlg.FileName}";
    }

    private void Import()
    {
        var dlg = new OpenFileDialog { Filter = "配方 JSON|*.recipe.json|所有文件|*.*" };
        if (dlg.ShowDialog() != true) return;

        var json = System.IO.File.ReadAllText(dlg.FileName);
        try
        {
            var r = _svc.ImportAsync(json).GetAwaiter().GetResult();
            StatusText = $"已导入:{r.Name}(v{r.Version})";
            LoadAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex) { StatusText = $"导入失败:{ex.Message}"; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>配方专用 ICommand(独立一份避免和 LoginViewModel 的冲突)。</summary>
public class RecipeDelegateCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;
    public RecipeDelegateCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

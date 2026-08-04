using System.Text.Json;
using System.Windows;
using DaqMonitor.Core.Recipes;

namespace DaqMonitor.UI.Views;

/// <summary>
/// 配方编辑对话框:用 JSON 直接编辑参数。
///
/// 为什么用 JSON 而不是属性网格(PropertyGrid):
///   ① 学习项目 — JSON 更透明,工程师能看懂"参数序列化后长这样",也方便面试讲导入导出
///   ② 真实工程:WPF PropertyGrid 一般用第三方控件(DevExpress/HandyControl),学习项目不引入
///   ③ 工业 HMIs 通常确实是表格 UI,但代码量翻 3 倍。等学到 WPF DataGrid 主从表后再升级。
/// </summary>
public partial class RecipeEditWindow : Window
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>构造后用户保存了的话,从这 3 个属性取值。</summary>
    public string RecipeName => NameBox.Text.Trim();
    public string Description => DescBox.Text.Trim();
    public IReadOnlyList<RecipeParameter> Parameters { get; private set; } = new List<RecipeParameter>();

    /// <summary>新建模式:空表单 + 1 个示例参数。</summary>
    public RecipeEditWindow() : this("", "", new List<RecipeParameter>
    {
        new() { Key = "温度", Value = "180", Unit = "℃", Type = "float", Min = "150", Max = "220" }
    }) { }

    /// <summary>编辑模式:把现有配方预填进去。</summary>
    public RecipeEditWindow(string name, string desc, IReadOnlyList<RecipeParameter> parameters)
    {
        InitializeComponent();
        NameBox.Text = name;
        DescBox.Text = desc;
        ParamsBox.Text = JsonSerializer.Serialize(parameters, JsonOpts);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorBlock.Text = "";
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ErrorBlock.Text = "配方名不能为空";
            NameBox.Focus();
            return;
        }

        try
        {
            Parameters = JsonSerializer.Deserialize<List<RecipeParameter>>(ParamsBox.Text) ?? new();
        }
        catch (JsonException ex)
        {
            ErrorBlock.Text = $"JSON 解析失败:{ex.Message}(行 {ex.LineNumber})";
            return;
        }

        DialogResult = true;
        Close();
    }
}

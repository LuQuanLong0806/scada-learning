using System.Windows;

// 关键：告诉 WPF "本程序集里自定义控件的默认主题字典在程序集内部（Themes/Generic.xaml）"。
// 没有这一行，GaugeControl / StatusDot 会因为找不到默认 Style 而"画不出来"（不报错但空白）。
// 这正是自定义控件 vs UserControl 最容易踩的坑，M14 会专门讲。
[assembly: ThemeInfo(
    ResourceDictionaryLocation.SourceAssembly,
    ResourceDictionaryLocation.SourceAssembly)]

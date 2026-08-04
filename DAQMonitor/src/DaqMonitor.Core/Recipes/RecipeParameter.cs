namespace DaqMonitor.Core.Recipes;

/// <summary>
/// 配方参数(单个键值对,强类型,带单位 + 量程)。
///
/// 注意:不是 EF 实体 —— 序列化为 JSON 嵌在 Recipe.ParametersJson 里(值对象)。
/// 这跟 DDD 里 Value Object 一个思路:参数脱离配方没意义,生命周期跟配方绑定。
///
/// 类比前端:RecipeParameter = Figma Variant 的某个 property(温度/压力/速度...),
/// Type 是"属性数据类型",Min/Max 用于 UI 输入校验。
/// </summary>
public class RecipeParameter
{
    /// <summary>参数键名,如 "温度" / "压力" / "速度"。</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>参数值(字符串存储,UI 按 Type 解析为对应类型)。</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>单位,如 "℃" / "MPa" / "mm/s"。</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>值类型:float / int / string / bool(UI 决定编辑器样式)。</summary>
    public string Type { get; set; } = "float";

    /// <summary>最小值(可选,UI 校验 + 报警基准)。</summary>
    public string? Min { get; set; }

    /// <summary>最大值(可选)。</summary>
    public string? Max { get; set; }

    /// <summary>备注/说明(给操作工看的人类可读描述)。</summary>
    public string? Remark { get; set; }
}

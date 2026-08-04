using ClosedXML.Excel;
using DaqMonitor.Core.Reporting;
using System.Collections.Generic;

namespace DaqMonitor.UI.Reporting;

/// <summary>
/// 报表导出：把聚合结果写成格式化 Excel（ClosedXML）。
/// 关联：M10 Day3 —— 企业每天要的"班报/日报"就长这样。
/// 真实可行性：ClosedXML 是工业上位机导出 Excel 最常用库（NuGet 直接装，无需装 Office）。
/// </summary>
public static class ReportExporter
{
    public static void ExportToExcel(IEnumerable<PointStat> stats, string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("报表");

        // 表头
        var headers = new[] { "点位", "样本数", "最小值", "最大值", "平均值", "起始时间", "结束时间" };
        for (int c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];

        // 数据行
        int r = 2;
        foreach (var s in stats)
        {
            ws.Cell(r, 1).Value = s.PointId;
            ws.Cell(r, 2).Value = s.Count;
            ws.Cell(r, 3).Value = s.Min;
            ws.Cell(r, 4).Value = s.Max;
            ws.Cell(r, 5).Value = s.Avg;
            ws.Cell(r, 6).Value = s.First;
            ws.Cell(r, 7).Value = s.Last;
            r++;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
    }
}

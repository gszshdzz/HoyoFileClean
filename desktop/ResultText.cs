using System.Text;

namespace HoyoFileClean.Desktop;

internal static class ResultText
{
    internal static string DescribeGameStatus(FileListSnapshot snapshot, bool imported)
    {
        ScanScope scan = snapshot.Sources[0].Scan!;
        string mode = scan.Mode == "heavy" ? "MD5 重扫" : "轻扫（未校验内容）";
        string text = imported ? $"已导入历史扫描（{mode}，未重新扫描）。" : $"{mode}完成；结果仅保留在内存。";
        int excluded = scan.ExcludedDirectories.Count + scan.ExcludedFiles.Count;
        if (excluded > 0) text += $" 排除 {excluded:N0} 项。";
        if (scan.SkippedLinks.Count > 0) text += $" 跳过链接／重解析点 {scan.SkippedLinks.Count:N0} 项（未跟随）。";
        if (excluded > 0 || scan.SkippedLinks.Count > 0) text += " 上述范围未扫描。";
        return text;
    }

    public static string Describe(FileListSnapshot? snapshot, string title)
    {
        if (snapshot is null) return title + "：尚无有效结果。";
        var text = new StringBuilder();
        text.AppendLine($"{title}：{snapshot.FileCount:N0} 个文件，{snapshot.TotalSize:N0} 字节（重叠条目已去重）");
        text.AppendLine($"MD5：{snapshot.HashedFileCount:N0} 个文件有记录；其余未校验内容。");
        foreach (FileListSource source in snapshot.Sources)
        {
            text.AppendLine();
            text.AppendLine("来源：" + source.Path);
            text.AppendLine($"文件数：{source.FileCount:N0}；大小：{source.TotalSize:N0} 字节；哈希：{source.HashAlgorithm ?? "无（仅路径与大小）"}");
            if (source.Scan is { } scan)
            {
                text.AppendLine("记录的扫描根目录：" + scan.Root);
                text.AppendLine("模式：" + (scan.Mode == "light" ? "轻扫（未校验内容）" : "MD5 重扫"));
                text.AppendLine("排除目录：" + Join(scan.ExcludedDirectories));
                text.AppendLine("排除文件：" + Join(scan.ExcludedFiles));
                text.AppendLine("跳过链接或重解析点：" + Join(scan.SkippedLinks));
                if (scan.ExcludedDirectories.Count + scan.ExcludedFiles.Count + scan.SkippedLinks.Count > 0)
                    text.AppendLine("警告：上述范围未扫描，不能据此认定文件缺失或可清理。");
            }
            else text.AppendLine("覆盖范围：清单内记录；未确认是否覆盖整个游戏。游戏名称与资源包未识别。");
        }
        return text.ToString();
    }

    public static string Describe(ComparisonResult? result)
    {
        if (result is null) return "尚未比较。A 固定为游戏文件，B 固定为参考清单。";
        ComparisonSummary summary = result.Summary;
        return $"游戏独有：{summary.OnlyA:N0}；参考独有：{summary.OnlyB:N0}；共有但不同：{summary.Different:N0}；未扫描范围：{summary.OutsideScope:N0}\r\n"
            + $"大小与 MD5 一致：{summary.MatchedWithHash:N0}；仅大小一致（未校验内容）：{summary.MatchedSizeOnly:N0}\r\n"
            + "独有只表示本次清单的集合差异，不证明文件无用。两侧大小或 MD5 不同是正常差异。";
    }

    private static string Join(IEnumerable<string> paths) => string.Join("；", paths) is { Length: > 0 } value ? value : "无";
}

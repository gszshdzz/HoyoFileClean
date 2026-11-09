namespace HoyoFileClean.Desktop;

public sealed partial class MainForm
{
    private async Task ExecuteAsync()
    {
        if (session.IsBusy) return;
        switch (session.Step)
        {
            case 0: await ConvertAsync(); break;
            case 1: await ScanAsync(); break;
            case 2: await CompareAsync(); break;
        }
    }

    private async Task ConvertAsync()
    {
        string[] inputs = manifests.Items.Cast<string>().ToArray();
        if (inputs.Length == 0) { status.Text = "请先添加 manifest，或使用跳过导入已有 JSON。"; return; }
        using var dialog = new FolderBrowserDialog { Description = "选择转换输出目录（每份 manifest 独立写为 原文件名.json，不覆盖）", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string[] outputs = inputs.Select(input => Path.Combine(dialog.SelectedPath, Path.GetFileName(input) + ".json")).ToArray();
        session.InvalidateReference();
        referenceOrigin = reportOrigin = "";
        await RunWorkAsync(() =>
        {
            if (outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != outputs.Length)
                throw new IOException("所选 manifest 有相同文件名，输出名称冲突。请分批转换到不同目录，再导入这些 JSON。");
            foreach (string output in outputs)
                if (File.Exists(output) || Directory.Exists(output)) throw new IOException("输出已存在，不能覆盖：" + output);
            var written = new List<string>();
            try
            {
                for (int i = 0; i < inputs.Length; i++)
                {
                    ManifestConverter.Convert(inputs[i], outputs[i]);
                    written.Add(outputs[i]);
                }
                return FileComparer.ReadInputs(outputs);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                throw new InvalidDataException("本批解析失败，不启用下一步。已生成的独立 JSON 不会删除或覆盖：\r\n"
                    + (written.Count > 0 ? string.Join("\r\n", written) : "无") + "\r\n原始错误：" + error.Message, error);
            }
        }, result =>
        {
            session.AcceptReference(result);
            referenceOrigin = "manifest 来源：\r\n" + string.Join("\r\n", inputs) + "\r\n\r\n";
        }, "正在转换 manifest…");
    }

    private async Task ScanAsync()
    {
        string root = gameRoot.Text.Trim();
        if (!Path.IsPathFullyQualified(root)) { status.Text = "请输入或浏览选择游戏目录的完整路径。"; return; }
        string? output = ChooseOutput("保存扫描 JSON（必须位于游戏目录之外）", "scan.json");
        if (output is null) return;
        bool hashes = heavy.Checked;
        string[] dirs = ReadLines(excludedDirectories), files = ReadLines(excludedFiles);
        var reporter = new Progress<ScanProgress>(value =>
        {
            if (!session.IsBusy) return;
            progress.Style = ProgressBarStyle.Continuous;
            progress.Value = Math.Clamp((int)value.Percentage, 0, 100);
            status.Text = $"正在 MD5 重扫：{value.CompletedFiles:N0}/{value.TotalFiles:N0} 个文件；{value.ProcessedBytes:N0}/{value.TotalBytes:N0} 字节。";
        });
        session.InvalidateGame();
        scanOrigin = reportOrigin = "";
        await RunWorkAsync(() =>
        {
            DirectoryScanner.Scan(root, output, dirs, files, hashes, value => ((IProgress<ScanProgress>)reporter).Report(value));
            return FileComparer.ReadInputs([output]);
        }, result =>
        {
            session.AcceptGame(result, root);
            scanOrigin = "本次实际扫描；结果已保存。\r\n";
        }, hashes ? "正在枚举文件，随后计算 MD5…" : "正在轻扫（只读取路径与大小）…");
    }

    private async Task CompareAsync()
    {
        if (session.Game is not { } game || session.Reference is not { } reference) return;
        string? output = ChooseOutput("保存比较报告 JSON", "comparison.json");
        if (output is null) return;
        session.InvalidateComparison();
        reportOrigin = "";
        await RunWorkAsync(() =>
        {
            ComparisonResult result = FileComparer.Compare(game, reference);
            FileComparer.WriteReport(output, result);
            return result;
        }, result =>
        {
            session.AcceptComparison(result);
            reportOrigin = "比较报告：" + output + "\r\n";
        }, "正在比较两侧清单…");
    }

    private async Task ImportAsync()
    {
        if (session.IsBusy || session.Step > 2) return;
        int step = session.Step;
        using var dialog = new OpenFileDialog
        {
            Title = step switch { 0 => "导入一份或多份参考清单 JSON", 1 => "导入已有扫描 JSON", _ => "导入比较报告并核对当前两侧" },
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            Multiselect = step == 0
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string[] inputs = dialog.FileNames;
        switch (step)
        {
            case 0:
                session.InvalidateReference();
                referenceOrigin = reportOrigin = "";
                await RunWorkAsync(() => FileComparer.ReadInputs(inputs), result =>
                {
                    session.AcceptReference(result);
                    referenceOrigin = "已导入参考 JSON；未推断游戏名称、版本或完整覆盖范围。\r\n";
                }, "正在校验参考清单…");
                break;
            case 1:
                string root = gameRoot.Text.Trim();
                session.InvalidateGame();
                scanOrigin = reportOrigin = "";
                await RunWorkAsync(() =>
                {
                    FileListSnapshot result = FileComparer.ReadInputs(inputs);
                    WizardSession.ValidateGameInput(result, root);
                    return result;
                }, result =>
                {
                    session.AcceptGame(result, root);
                    scanOrigin = "已导入历史扫描 JSON；未重新扫描，也不证明目录当前状态未变化。以下模式与范围来自扫描记录。\r\n";
                }, "正在校验扫描 JSON 与指定目录…");
                break;
            case 2:
                if (session.Game is not { } game || session.Reference is not { } reference) return;
                session.InvalidateComparison();
                reportOrigin = "";
                await RunWorkAsync(() => FileComparer.ImportReport(inputs[0], game, reference), result =>
                {
                    session.AcceptComparison(result);
                    reportOrigin = "已核对身份和内容的导入报告：" + inputs[0] + "\r\n";
                }, "正在核对报告身份，并用当前清单复核内容…");
                break;
        }
    }

    private async Task RunWorkAsync<T>(Func<T> work, Action<T> apply, string message)
    {
        Task operation = session.RunAsync(work, apply);
        status.Text = message;
        progress.Style = ProgressBarStyle.Marquee;
        progress.Visible = true;
        differences.Render();
        RefreshView();
        try
        {
            await operation;
            status.Text = "处理完成，请核对本步摘要后点击下一步。";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            status.Text = "处理失败，未启用本步结果：\r\n" + error;
        }
        finally
        {
            progress.Visible = false;
            differences.Render();
            RefreshView();
        }
    }

    private string? ChooseOutput(string title, string fileName)
    {
        using var dialog = new SaveFileDialog { Title = title, Filter = "JSON 文件 (*.json)|*.json", FileName = fileName, DefaultExt = "json", AddExtension = true, OverwritePrompt = false };
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        if (!File.Exists(dialog.FileName) && !Directory.Exists(dialog.FileName)) return dialog.FileName;
        status.Text = "输出已存在，不能覆盖。请重新操作并选择新的文件名：" + dialog.FileName;
        return null;
    }

    private static string[] ReadLines(TextBox box) => box.Lines.Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
}

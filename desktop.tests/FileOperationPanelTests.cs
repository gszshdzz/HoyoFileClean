using HoyoFileClean.Desktop;
using Xunit;
using static HoyoFileClean.Desktop.Tests.NativeTestHost;

namespace HoyoFileClean.Desktop.Tests;

[Collection("Windows Forms")]
public sealed class FileOperationPanelTests
{
    private static string NewRoot()
    {
        string root = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static WizardSession Ready(bool different = false, bool mixed = false)
    {
        string root = NewRoot(), reference = NewRoot();
        File.WriteAllText(Path.Combine(root, "file"), "abc");
        if (different) File.WriteAllText(Path.Combine(reference, "file"), "abcd");
        if (mixed) File.WriteAllText(Path.Combine(root, "only"), "abc");
        var session = new WizardSession();
        session.AcceptGame(DirectoryScanner.Scan(root), root);
        session.AcceptReference(DirectoryScanner.Scan(reference));
        session.AcceptComparison(FileComparer.Compare(session.Game!, session.Reference!));
        session.Select("file", true);
        if (mixed) session.Select("only", true);
        return session;
    }

    [Fact]
    public void DisplaySeparatesInformationAndNeverRetainsSelection() => OnSta(() =>
    {
        var session = Ready();
        using var panel = new FileOperationPanel(session);
        var table = Find<DataGridView>(panel, "FileOperationFiles");
        Assert.Equal(new[] { "Category", "Size", "SizeDifference", "HashDifference", "State", "Message", "Path" },
            table.Columns.Cast<DataGridViewColumn>().Select(column => column.Name));
        Assert.Equal("游戏独有", table.Rows[0].Cells["Category"].Value);
        Assert.Equal(3L, table.Rows[0].Cells["Size"].Value);
        Assert.Equal("待操作", table.Rows[0].Cells["State"].Value);
        Assert.Equal("", table.Rows[0].Cells["Message"].Value);
        Assert.Equal(Path.Combine(session.Game!.Sources[0].Scan!.Root, "file"), table.Rows[0].Cells["Path"].Value);
        table.Rows[0].Cells["Path"].Selected = true;
        Assert.Empty(table.SelectedCells.Cast<DataGridViewCell>());
        table.SelectAll();
        Assert.Empty(table.SelectedCells.Cast<DataGridViewCell>());
        panel.Render();
        Assert.Empty(table.SelectedCells.Cast<DataGridViewCell>());
        Assert.Contains("仅处理所选普通文件", Find<Label>(panel, "FileOperationSummary").Text);
    });

    [Fact]
    public void SharedDifferencesDisplaySizeAndHashComparisonSeparately() => OnSta(() =>
    {
        using var panel = new FileOperationPanel(Ready(different: true));
        var row = Find<DataGridView>(panel, "FileOperationFiles").Rows[0];
        Assert.Equal("共有但不同", row.Cells["Category"].Value);
        Assert.Equal("不同", row.Cells["SizeDifference"].Value);
        Assert.Equal("未完整校验", row.Cells["HashDifference"].Value);
    });

    [Fact]
    public void EqualFilesUseEqualCategoryAndVirtualRows() => OnSta(() =>
    {
        string root = NewRoot(), reference = NewRoot();
        File.WriteAllText(Path.Combine(root, "file"), "abc");
        File.WriteAllText(Path.Combine(reference, "file"), "abc");
        var session = new WizardSession();
        session.AcceptGame(DirectoryScanner.Scan(root), root);
        session.AcceptReference(DirectoryScanner.Scan(reference));
        session.AcceptComparison(FileComparer.Compare(session.Game!, session.Reference!));
        session.SetFilters(session.Filters.Append(DifferenceCategory.Equal));
        session.Select("file", true);
        using var panel = new FileOperationPanel(session);
        var table = Find<DataGridView>(panel, "FileOperationFiles");
        Assert.True(table.VirtualMode);
        Assert.Equal(1, table.RowCount);
        Assert.Equal("相同", table.Rows[0].Cells["Category"].Value);
    });

    [Fact]
    public void WizardHasSeparateFifthStepAndCanReturnToReview() => OnSta(() =>
    {
        var session = Ready();
        using var form = new MainForm(session)
        {
            Location = new Point(-20000, -20000), StartPosition = FormStartPosition.Manual, ShowInTaskbar = false
        };
        form.Show();
        Application.DoEvents();
        for (int i = 0; i < 4; i++) Find<Button>(form, "Next").PerformClick();
        Assert.Equal(4, session.Step);
        Assert.True(Find<FileOperationPanel>(form, "FileOperationPanel").Visible);
        Assert.False(Find<DifferencePanel>(form, "DifferencePanel").Visible);
        Assert.False(Find<Button>(form, "Next").Visible);
        Find<Button>(form, "Back").PerformClick();
        Assert.Equal(3, session.Step);
        Assert.True(Find<DifferencePanel>(form, "DifferencePanel").Visible);
    });

    [Fact]
    public void EmptySelectionDisablesConfirmationAndRerenderReflectsSelection() => OnSta(() =>
    {
        var session = Ready();
        using var panel = new FileOperationPanel(session);
        session.Select("file", false);
        panel.Render();
        Assert.Empty(Find<DataGridView>(panel, "FileOperationFiles").Rows.Cast<DataGridViewRow>());
        Assert.False(Find<Button>(panel, "ConfirmFileOperation").Enabled);
        session.Select("file", true);
        panel.Render();
        Assert.Single(Find<DataGridView>(panel, "FileOperationFiles").Rows.Cast<DataGridViewRow>());
        Assert.True(Find<Button>(panel, "ConfirmFileOperation").Enabled);
    });

    [Fact]
    public void GameOnlyCancellationDoesNotRaiseRequest() => OnSta(() =>
    {
        var session = Ready();
        int confirmations = 0, requests = 0;
        using var panel = new FileOperationPanel(session, _ => { confirmations++; return false; });
        panel.OperationConfirmed += _ => requests++;
        panel.ConfirmOperation();
        Assert.Equal(1, confirmations);
        Assert.Equal(0, requests);
        Assert.Equal(1, session.SelectedCount);
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SharedDifferentSelectionRequiresExtraConfirmationAndEitherCancelStopsRequest(bool acceptRisk, bool mixed) => OnSta(() =>
    {
        var session = Ready(different: true, mixed: mixed);
        var prompts = new List<string>();
        int requests = 0;
        using var panel = new FileOperationPanel(session, message =>
        {
            prompts.Add(message);
            return prompts.Count == 1 && acceptRisk;
        });
        panel.OperationConfirmed += _ => requests++;
        panel.ConfirmOperation();
        Assert.Equal(acceptRisk ? 2 : 1, prompts.Count);
        Assert.Contains("额外风险确认", prompts[0]);
        Assert.Contains("1 个", prompts[0]);
        if (acceptRisk) Assert.Contains("即将", prompts[1]);
        Assert.Equal(0, requests);
        Assert.Equal(mixed ? 2 : 1, session.SelectedCount);
    });

    [Theory]
    [InlineData(FileOperationKind.Recycle, "RecycleFiles")]
    [InlineData(FileOperationKind.Delete, "DeleteFiles")]
    [InlineData(FileOperationKind.Move, "MoveFiles")]
    public void ConfirmedOptionsProduceOnlyUiRequestWithoutChangingFiles(FileOperationKind kind, string option) => OnSta(() =>
    {
        var session = Ready(different: true);
        string root = session.Game!.Sources[0].Scan!.Root, target = NewRoot();
        var comparison = session.Comparison;
        int confirmations = 0;
        FileOperationRequest? request = null;
        using var panel = new FileOperationPanel(session, _ => { confirmations++; return true; });
        panel.OperationConfirmed += value => request = value;
        Find<RadioButton>(panel, option).Checked = true;
        Find<TextBox>(panel, "MoveDestination").Text = target;
        panel.ConfirmOperation();
        Assert.Equal(2, confirmations);
        Assert.NotNull(request);
        Assert.Equal(kind, request.Kind);
        Assert.Equal(root, request.Root);
        Assert.Equal("file", Assert.Single(request.Files).Path);
        Assert.Equal(kind == FileOperationKind.Move ? target : null, request.Destination);
        Assert.Equal("abc", File.ReadAllText(Path.Combine(root, "file")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        Assert.Equal(1, session.SelectedCount);
        Assert.Same(comparison, session.Comparison);
        Assert.False(session.IsBusy);
    });

    [Fact]
    public void UnconnectedPanelNeverPerformsAnOperation() => OnSta(() =>
    {
        var session = Ready();
        string root = session.Game!.Sources[0].Scan!.Root;
        using var panel = new FileOperationPanel(session, _ => true);
        panel.ConfirmOperation();
        Assert.Equal("abc", File.ReadAllText(Path.Combine(root, "file")));
        Assert.Equal(1, session.SelectedCount);
    });

    [Fact]
    public void WizardExecutesConfirmedMoveAndKeepsResultsButInvalidatesOldScan() => OnSta(() =>
    {
        var session = Ready();
        string root = session.Game!.Sources[0].Scan!.Root, target = NewRoot();
        var request = new FileOperationRequest(FileOperationKind.Move, root, session.SelectedRows.ToArray(), target);
        using var form = new MainForm(session);
        for (int i = 0; i < 4; i++) session.Next();
        Task operation = form.ExecuteFileOperationAsync(request);
        WaitUntil(() => operation.IsCompleted);
        operation.GetAwaiter().GetResult();
        Assert.False(File.Exists(Path.Combine(root, "file")));
        Assert.Equal("abc", File.ReadAllText(Path.Combine(target, "file")));
        Assert.Null(session.Game);
        Assert.Null(session.Comparison);
        Assert.Equal(0, session.SelectedCount);
        var panel = Find<FileOperationPanel>(form, "FileOperationPanel");
        Assert.Equal("成功", Find<DataGridView>(panel, "FileOperationFiles").Rows[0].Cells["State"].Value);
        Assert.Equal(Path.Combine(target, "file"), Find<DataGridView>(panel, "FileOperationFiles").Rows[0].Cells["Message"].Value);
        Assert.False(Find<Button>(panel, "ConfirmFileOperation").Enabled);
        panel.Render();
        Assert.Equal("成功", Find<DataGridView>(panel, "FileOperationFiles").Rows[0].Cells["State"].Value);
    });

    [Fact]
    public void ConfirmButtonExecutesMoveAndLocksRepeatExecution() => OnSta(() =>
    {
        var session = Ready();
        string root = session.Game!.Sources[0].Scan!.Root, target = NewRoot();
        int confirmations = 0;
        using var form = new MainForm(session, confirmFileOperation: _ => { confirmations++; return true; })
        {
            Location = new Point(-20000, -20000), StartPosition = FormStartPosition.Manual, ShowInTaskbar = false
        };
        form.Show();
        for (int i = 0; i < 4; i++) Find<Button>(form, "Next").PerformClick();
        var panel = Find<FileOperationPanel>(form, "FileOperationPanel");
        Find<RadioButton>(panel, "MoveFiles").Checked = true;
        Find<TextBox>(panel, "MoveDestination").Text = target;
        Find<Button>(panel, "ConfirmFileOperation").PerformClick();
        Assert.True(session.IsBusy);
        Assert.False(Find<Button>(form, "Back").Enabled);
        Assert.False(Find<Button>(panel, "ConfirmFileOperation").Enabled);
        Assert.False(session.RequestClose());
        WaitUntil(() => !session.IsBusy);
        Assert.Equal(2, confirmations);
        Assert.Equal("abc", File.ReadAllText(Path.Combine(target, "file")));
        Assert.False(File.Exists(Path.Combine(root, "file")));
        Assert.Null(session.Game);
        Assert.Equal("成功", Find<DataGridView>(panel, "FileOperationFiles").Rows[0].Cells["State"].Value);
        Assert.False(Find<Button>(panel, "ConfirmFileOperation").Enabled);
    });

    [Fact]
    public void PreflightFailureRetainsSelectionAndAllowsFixingTheDestination() => OnSta(() =>
    {
        var session = Ready();
        var game = session.Game;
        var comparison = session.Comparison;
        string root = game!.Sources[0].Scan!.Root, target = NewRoot();
        File.WriteAllText(Path.Combine(target, "file"), "existing target");
        using var form = new MainForm(session);
        for (int i = 0; i < 4; i++) session.Next();
        Task operation = form.ExecuteFileOperationAsync(new(FileOperationKind.Move, root, session.SelectedRows.ToArray(), target));
        WaitUntil(() => operation.IsCompleted);
        operation.GetAwaiter().GetResult();
        Assert.Same(game, session.Game);
        Assert.Same(comparison, session.Comparison);
        Assert.Equal(1, session.SelectedCount);
        Assert.Equal("abc", File.ReadAllText(Path.Combine(root, "file")));
        Assert.Equal("existing target", File.ReadAllText(Path.Combine(target, "file")));
        var panel = Find<FileOperationPanel>(form, "FileOperationPanel");
        Assert.Equal("失败", Find<DataGridView>(panel, "FileOperationFiles").Rows[0].Cells["State"].Value);
        Assert.True(Find<Button>(panel, "ConfirmFileOperation").Enabled);
        Assert.Contains("整批预检失败", Find<Label>(panel, "FileOperationSummary").Text);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenSharedFilesAndRecycleRisksAreCombinedIntoOneWarning(bool acceptWarnings) => OnSta(() =>
    {
        string root = NewRoot(), reference = NewRoot();
        File.WriteAllText(Path.Combine(root, "pkg_version"), "local metadata");
        File.WriteAllText(Path.Combine(reference, "pkg_version"), "reference metadata differs");
        var session = new WizardSession();
        session.AcceptGame(DirectoryScanner.Scan(root), root, GameIdentifier.FromDetections(
            new Dictionary<string, string> { ["exe_hk4e_cn"] = "YuanShen.exe" }));
        session.AcceptReference(DirectoryScanner.Scan(reference));
        session.AcceptComparison(FileComparer.Compare(session.Game!, session.Reference!));
        session.SetShowHiddenResults(true);
        session.Select("pkg_version", true);
        var prompts = new List<string>();
        int requests = 0;
        using var panel = new FileOperationPanel(session, message =>
        {
            prompts.Add(message);
            return acceptWarnings;
        });
        panel.OperationConfirmed += _ => requests++;
        panel.ConfirmOperation();
        Assert.Equal(acceptWarnings ? 2 : 1, prompts.Count);
        Assert.Contains("额外风险确认", prompts[0]);
        Assert.Contains("默认隐藏范围", prompts[0]);
        Assert.Contains("pkg_version", prompts[0]);
        Assert.Contains("目录结构", prompts[0]);
        Assert.Contains("独立条目", prompts[0]);
        if (acceptWarnings) Assert.Contains("即将", prompts[1]);
        Assert.Equal(acceptWarnings ? 1 : 0, requests);
        Assert.Equal("local metadata", File.ReadAllText(Path.Combine(root, "pkg_version")));
    });

    [Fact]
    public void FileSizesAreFormattedWithoutReplacingNumericCellValues() => OnSta(() =>
    {
        using var panel = new FileOperationPanel(Ready());
        var cell = Find<DataGridView>(panel, "FileOperationFiles").Rows[0].Cells["Size"];
        Assert.Equal(3L, cell.Value);
        Assert.Equal("3B", cell.FormattedValue);
        Assert.Contains("3B", Find<Label>(panel, "FileOperationSummary").Text);
    });

    [Theory]
    [InlineData("DeleteFiles")]
    [InlineData("MoveFiles")]
    public void HiddenGameOnlyFileStillWarnsAndFinalCancellationDoesNotRequestProcessing(string option) => OnSta(() =>
    {
        string root = NewRoot(), reference = NewRoot();
        File.WriteAllText(Path.Combine(root, "pkg_version"), "local metadata");
        var session = new WizardSession();
        session.AcceptGame(DirectoryScanner.Scan(root), root, GameIdentifier.FromDetections(
            new Dictionary<string, string> { ["exe_hk4e_cn"] = "YuanShen.exe" }));
        session.AcceptReference(DirectoryScanner.Scan(reference));
        session.AcceptComparison(FileComparer.Compare(session.Game!, session.Reference!));
        session.SetShowHiddenResults(true);
        session.Select("pkg_version", true);
        var prompts = new List<string>();
        int requests = 0;
        using var panel = new FileOperationPanel(session, message =>
        {
            prompts.Add(message);
            return prompts.Count == 1;
        });
        panel.OperationConfirmed += _ => requests++;
        Find<RadioButton>(panel, option).Checked = true;
        Find<TextBox>(panel, "MoveDestination").Text = NewRoot();
        panel.ConfirmOperation();
        Assert.Equal(2, prompts.Count);
        Assert.Contains("默认隐藏范围", prompts[0]);
        Assert.Contains("pkg_version", prompts[0]);
        Assert.DoesNotContain("非游戏独有文件", prompts[0]);
        Assert.DoesNotContain("独立条目", prompts[0]);
        Assert.Contains("即将", prompts[1]);
        Assert.Equal(0, requests);
        Assert.Equal("local metadata", File.ReadAllText(Path.Combine(root, "pkg_version")));
    });

    [Theory]
    [InlineData("same")]
    [InlineData("child")]
    [InlineData("missing")]
    [InlineData("network")]
    [InlineData("relative")]
    public void InvalidMoveTargetShowsOneInputErrorAndNoConfirmation(string kind) => OnSta(() =>
    {
        var session = Ready();
        string root = session.Game!.Sources[0].Scan!.Root;
        string target = kind switch
        {
            "same" => root,
            "child" => Path.Combine(root, "nested"),
            "missing" => Path.Combine(NewRoot(), "missing"),
            "network" => @"\\localhost\unsupported\target",
            _ => "relative"
        };
        int confirmations = 0, requests = 0;
        var errors = new List<string>();
        using var panel = new FileOperationPanel(session, _ => { confirmations++; return true; }, errors.Add);
        panel.OperationConfirmed += _ => requests++;
        Find<RadioButton>(panel, "MoveFiles").Checked = true;
        Find<TextBox>(panel, "MoveDestination").Text = target;
        panel.ConfirmOperation();
        Assert.Single(errors);
        Assert.Equal(0, confirmations);
        Assert.Equal(0, requests);
        Assert.Equal("abc", File.ReadAllText(Path.Combine(root, "file")));
    });
}

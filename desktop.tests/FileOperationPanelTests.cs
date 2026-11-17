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
        Assert.Contains("仅界面预览", Find<Label>(panel, "FileOperationSummary").Text);
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
}

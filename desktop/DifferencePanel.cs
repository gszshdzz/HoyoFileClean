namespace HoyoFileClean.Desktop;

public sealed class DifferencePanel : UserControl
{
    private readonly WizardSession session;
    private readonly TreeView gameTree = new() { Name = "GameTree", Dock = DockStyle.Fill, CheckBoxes = true, HideSelection = false };
    private readonly TreeView referenceTree = new() { Name = "ReferenceTree", Dock = DockStyle.Fill, CheckBoxes = false, HideSelection = false };
    private readonly ComboBox filter = new() { Name = "DifferenceFilter", DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
    private readonly CheckBox confirm = new() { Name = "ConfirmSelection", Text = "确认所选文件", AutoSize = true };
    private readonly Button demonstrate = new() { Name = "DemonstrateDelete", Text = "删除（演示）", AutoSize = true };
    private readonly Label selection = new() { Name = "SelectionSummary", AutoSize = true };
    private readonly TextBox details = new() { Name = "DifferenceDetails", Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private bool rendering;
    private bool hasRendered;
    private ComparisonResult? renderedComparison;
    private DifferenceCategory renderedFilter;

    public DifferencePanel(WizardSession session)
    {
        this.session = session;
        Name = "DifferencePanel";
        Dock = DockStyle.Fill;
        filter.Items.AddRange(["全部差异（含未扫描范围）", "游戏独有", "参考独有", "共有但不同", "未扫描范围（不可选）"]);
        filter.SelectedIndex = 0;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        top.Controls.AddRange([new Label { Text = "筛选：", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, filter]);
        var split = new SplitContainer { Size = new Size(900, 400), SplitterDistance = 448, Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        AddTree(split.Panel1, "左侧／A：游戏文件（仅文件可勾选）", gameTree);
        AddTree(split.Panel2, "右侧／B：参考清单（只读，不可勾选）", referenceTree);
        var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        bottom.Controls.AddRange([selection, confirm, demonstrate]);
        layout.Controls.Add(top, 0, 0);
        layout.Controls.Add(split, 0, 1);
        layout.Controls.Add(details, 0, 2);
        layout.Controls.Add(bottom, 0, 3);
        var warning = new Label
        {
            Name = "DifferenceWarning",
            Text = "仅演示，不实际删除或移动。独有不代表无用；未扫描范围不能认定为缺失。切换筛选会取消隐藏文件的选择。",
            AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 6), MaximumSize = new Size(740, 0)
        };
        layout.SizeChanged += (_, _) => warning.MaximumSize = new Size(Math.Max(120, layout.ClientSize.Width - warning.Margin.Horizontal), 0);
        layout.Controls.Add(warning, 0, 4);
        Controls.Add(layout);
        filter.SelectedIndexChanged += (_, _) =>
        {
            if (rendering || session.IsBusy) return;
            session.SetFilter((DifferenceCategory)filter.SelectedIndex);
            Render();
        };
        gameTree.BeforeCheck += (_, args) =>
        {
            if (args.Node is null) return;
            if (!rendering && (session.IsBusy || args.Node.Tag is not DifferenceRow { CanSelect: true })) args.Cancel = true;
        };
        gameTree.AfterCheck += (_, args) =>
        {
            if (rendering || args.Node?.Tag is not DifferenceRow row) return;
            session.Select(row.Path, args.Node.Checked);
            RefreshSelection();
        };
        foreach (TreeView tree in new[] { gameTree, referenceTree })
        {
            tree.BeforeExpand += (_, args) =>
            {
                if (args.Node?.Tag is Branch branch && args.Node.Nodes.Count == 1 && args.Node.Nodes[0].Tag is null)
                {
                    args.Node.Nodes.Clear();
                    AddChildren(args.Node.Nodes, branch, tree == gameTree);
                }
            };
            tree.AfterSelect += (_, args) =>
            {
                details.Text = args.Node?.Tag is DifferenceRow row ? Describe(row) : "目录只用于展开／折叠，不能选择目录执行操作。";
            };
        }
        confirm.CheckedChanged += (_, _) =>
        {
            if (rendering) return;
            session.ConfirmSelection(confirm.Checked);
            RefreshSelection();
        };
        demonstrate.Click += (_, _) => DemonstrateDeletion();
        Render();
    }

    public void Render()
    {
        if (hasRendered && ReferenceEquals(renderedComparison, session.Comparison) && renderedFilter == session.Filter)
        {
            RefreshSelection();
            return;
        }
        rendering = true;
        gameTree.BeginUpdate();
        referenceTree.BeginUpdate();
        try
        {
            filter.SelectedIndex = (int)session.Filter;
            gameTree.Nodes.Clear();
            referenceTree.Nodes.Clear();
            var root = new Branch("");
            foreach (DifferenceRow row in session.VisibleRows)
            {
                string[] parts = row.Path.Split('/');
                Branch branch = root;
                foreach (string part in parts[..^1])
                {
                    if (!branch.Directories.TryGetValue(part, out Branch? child))
                        branch.Directories.Add(part, child = new Branch(part));
                    branch = child;
                }
                branch.Files.Add(row);
            }
            AddChildren(gameTree.Nodes, root, true);
            AddChildren(referenceTree.Nodes, root, false);
            details.Text = session.Comparison is null ? "尚无有效比较结果。" : ResultText.Describe(session.Comparison);
            renderedComparison = session.Comparison;
            renderedFilter = session.Filter;
            hasRendered = true;
        }
        finally
        {
            gameTree.EndUpdate();
            referenceTree.EndUpdate();
            rendering = false;
        }
        RefreshSelection();
    }

    private void AddChildren(TreeNodeCollection nodes, Branch branch, bool game)
    {
        foreach (Branch child in branch.Directories.Values)
        {
            var node = new TreeNode(child.Name + "（目录）") { Tag = child };
            node.Nodes.Add(new TreeNode("…"));
            nodes.Add(node);
        }
        bool previous = rendering;
        rendering = true;
        try
        {
            foreach (DifferenceRow row in branch.Files)
            {
                ManifestFile? file = game ? row.Game : row.Reference;
                string name = (file?.Path ?? row.Path).Split('/')[^1];
                var node = new TreeNode(name + "  [" + CategoryName(row.Category) + "]  "
                    + (file is null ? "（此侧无记录）" : $"{file.Size:N0} 字节"))
                {
                    Tag = row,
                    Checked = game && session.IsSelected(row.Path)
                };
                nodes.Add(node);
            }
        }
        finally { rendering = previous; }
    }

    private void RefreshSelection()
    {
        rendering = true;
        try
        {
            filter.Enabled = !session.IsBusy;
            selection.Text = $"已选 {session.SelectedCount:N0} 个文件，{session.SelectedSize:N0} 字节  ";
            confirm.Checked = session.SelectionConfirmed;
            confirm.Enabled = !session.IsBusy && session.SelectedCount > 0;
            demonstrate.Enabled = session.CanDemonstrateDeletion;
        }
        finally { rendering = false; }
    }

    private void DemonstrateDeletion()
    {
        if (!session.CanDemonstrateDeletion) return;
        if (MessageBox.Show(this,
            $"【仅演示，不实际删除】\r\n已选 {session.SelectedCount:N0} 个游戏文件，共 {session.SelectedSize:N0} 字节。\r\n继续只会演示确认交互，不会删除或移动文件。是否继续？",
            "演示：二次确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        MessageBox.Show(this, "演示确认结束，未删除或移动任何文件。文件列表保持不变。", "仅演示", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string Describe(DifferenceRow row)
    {
        static string FileText(ManifestFile? file) => file is null ? "此侧无记录" : $"{file.Path}；{file.Size:N0} 字节；MD5：{file.Hash ?? "未计算（未校验内容）"}";
        return CategoryName(row.Category) + "\r\n游戏：" + FileText(row.Game) + "\r\n参考：" + FileText(row.Reference)
            + (row.Category == DifferenceCategory.OutsideScope ? "\r\n警告：对侧明确未扫描此范围，不认定为独有或缺失，不可选择。" : "");
    }

    private static string CategoryName(DifferenceCategory category) => category switch
    {
        DifferenceCategory.GameOnly => "游戏独有",
        DifferenceCategory.ReferenceOnly => "参考独有",
        DifferenceCategory.Different => "共有但不同",
        _ => "未扫描范围"
    };

    private static void AddTree(Panel panel, string title, TreeView tree)
    {
        panel.Controls.Add(tree);
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 6) });
    }

    private sealed class Branch(string name)
    {
        public string Name { get; } = name;
        public SortedDictionary<string, Branch> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<DifferenceRow> Files { get; } = [];
    }
}

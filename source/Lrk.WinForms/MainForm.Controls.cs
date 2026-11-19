namespace Lrk.WinForms;

internal sealed partial class MainForm
{
    private static TableLayoutPanel Vertical()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return panel;
    }

    private static void Row(TableLayoutPanel panel, Control content)
    {
        Row(panel, content, 100f);
    }

    private static void Row(TableLayoutPanel panel, Control content, int pixels)
    {
        AddRow(panel, content, new RowStyle(SizeType.Absolute, pixels));
    }

    private static void Row(TableLayoutPanel panel, Control content, float percent)
    {
        AddRow(panel, content, new RowStyle(SizeType.Percent, percent));
    }

    private static void AddRow(TableLayoutPanel panel, Control content, RowStyle style)
    {
        var index = panel.RowCount++;
        panel.RowStyles.Add(style);
        content.Dock = DockStyle.Fill;
        panel.Controls.Add(content, 0, index);
    }

    private static Label Label(string text, bool muted = false, float size = 10, bool bold = false) =>
        new()
        {
            Text = text,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = muted ? Muted : Ink,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false,
            Margin = Padding.Empty
        };

    private static Button Button(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 31),
            Padding = new Padding(9, 1, 9, 1),
            Margin = new Padding(0, 3, 6, 3),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            BackColor = primary ? Accent : Color.White,
            ForeColor = primary ? Color.White : Ink,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        return button;
    }

    private static FlowLayoutPanel Toolbar(params Control[] controls)
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        foreach (var control in controls)
        {
            if (control is Label label)
            {
                label.AutoSize = true;
                label.Margin = new Padding(0, 8, 6, 4);
            }
            bar.Controls.Add(control);
        }
        return bar;
    }

    private static TextBox Edit(bool multiline = false, bool monospace = false) =>
        new()
        {
            Multiline = multiline,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            WordWrap = !multiline,
            AcceptsReturn = multiline,
            BackColor = Color.White,
            ForeColor = Ink,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(monospace ? "Consolas" : "Segoe UI", 11),
            Margin = new Padding(0, 1, 0, 4)
        };

    private static TextBox TextBlock() =>
        new()
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Background,
            ForeColor = Ink,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 10),
            Margin = new Padding(0, 4, 0, 5)
        };

    private static ComboBox Combo(int width = 235) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        DropDownWidth = Math.Max(width, 290),
        IntegralHeight = true,
        Width = width,
        Margin = new Padding(0, 4, 5, 3)
    };

    private static TabPage Page(string title) => new(title) { BackColor = Color.White, ForeColor = Ink };

    private static TableLayoutPanel PageContents(TabPage page)
    {
        var panel = Vertical();
        panel.Padding = new Padding(14, 10, 14, 8);
        page.Controls.Add(panel);
        return panel;
    }

    private static DataGridView Grid()
    {
        return new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToOrderColumns = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            GridColor = Border,
            RowHeadersVisible = false,
            AutoGenerateColumns = false,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Background,
                ForeColor = Ink,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                SelectionBackColor = Background,
                SelectionForeColor = Ink,
                Padding = new Padding(4)
            },
            RowTemplate = { Height = 28 },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = Ink,
                SelectionBackColor = SelectionBackground,
                SelectionForeColor = Ink,
                Padding = new Padding(4, 0, 4, 0)
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(248, 250, 252) },
            Margin = new Padding(0, 3, 0, 5)
        };
    }

    private static void SetColumns(DataGridView grid, params (string Title, float Weight)[] columns)
    {
        foreach (var (title, weight) in columns)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = title,
                FillWeight = weight,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }
    }

    private static T? CurrentValue<T>(DataGridView grid, IReadOnlyList<T> values) where T : class =>
        grid.CurrentCell is { RowIndex: var index } && (uint)index < (uint)values.Count ? values[index] : null;

    private static void SelectRow(DataGridView grid, int index)
    {
        if (index < 0 || index >= grid.RowCount) return;
        grid.CurrentCell = grid.Rows[index].Cells[0];
    }
}

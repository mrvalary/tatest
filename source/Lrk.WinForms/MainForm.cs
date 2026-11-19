using Lrk.Core;

namespace Lrk.WinForms;

internal sealed partial class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(242, 245, 249);
    private static readonly Color Ink = Color.FromArgb(22, 38, 55);
    private static readonly Color Muted = Color.FromArgb(68, 88, 108);
    private static readonly Color Border = Color.FromArgb(207, 217, 226);
    private static readonly Color Accent = Color.FromArgb(22, 107, 98);
    private static readonly Color Error = Color.FromArgb(158, 52, 41);
    private static readonly Color SelectionBackground = Color.FromArgb(220, 239, 236);
    private static readonly Color Pending = Color.FromArgb(86, 101, 119);

    private readonly ComboBox examplePicker = Combo();
    private readonly TextBox terminals = Edit();
    private readonly TextBox nonterminals = Edit();
    private readonly TextBox start = Edit();
    private readonly TextBox rules = Edit(multiline: true, monospace: true);
    private readonly NumericUpDown kInput = new() { Minimum = 0, Maximum = 32, Width = 72 };
    private readonly Button openGrammar = Button("Открыть…");
    private readonly Button saveGrammar = Button("Сохранить…");
    private readonly Button check = Button("Проверить грамматику", primary: true);
    private readonly Button cancel = Button("Отменить");
    private readonly ErrorProvider inputErrors = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };
    private readonly Label operation = Label(" ", muted: true);
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill, Padding = new Point(8, 5) };

    private readonly TabPage resultPage = Page("Результат");
    private readonly TabPage tablePage = Page("Таблица действий");
    private readonly TabPage automatonPage = Page("Автомат");
    private readonly TabPage tracePage = Page("Разбор цепочки");
    private readonly TabPage helpPage = Page("Справка");

    private readonly Label verdict = Label("Проверка грамматики", size: 18, bold: true);
    private readonly TextBox summary = TextBlock();
    private readonly DataGridView conflicts = Grid();
    private readonly TextBox conflictDetail = TextBlock();
    private readonly TextBox resultGuide = TextBlock();
    private readonly Panel resultBody = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly TableLayoutPanel conflictSection = Vertical();
    private readonly Button openState = Button("Показать состояние");
    private readonly Button openCell = Button("Найти в таблице");
    private readonly Button openTrace = Button("Перейти к разбору", primary: true);
    private readonly Button saveProtocol = Button("Сохранить протокол…");

    private readonly ComboBox tableState = Combo(width: 125);
    private readonly CheckBox conflictsOnly = new() { Text = "Только конфликтные ячейки", AutoSize = true, Margin = new Padding(8, 8, 5, 4) };
    private readonly DataGridView actionTable = Grid();
    private readonly TextBox actionDetail = TextBlock();
    private readonly Button tableOpenState = Button("Показать состояние");

    private readonly AutomatonGraph graph = new() { Dock = DockStyle.Fill };
    private readonly ComboBox statePicker = Combo(width: 130);
    private readonly DataGridView items = Grid();
    private readonly TextBox transitionDetail = TextBlock();
    private readonly TextBox itemDetail = TextBlock();
    private readonly Button saveGraph = Button("Сохранить граф .dot…");

    private readonly TextBox chain = Edit(monospace: true);
    private readonly Button parse = Button("Разобрать", primary: true);
    private readonly Button prevStep = Button("← Назад");
    private readonly Button nextStep = Button("Дальше →");
    private readonly Label parseVerdict = Label("Сначала проверьте грамматику.", bold: true);
    private readonly DataGridView steps = Grid();
    private readonly TextBox stepDetail = TextBlock();
    private readonly Button stepState = Button("Показать состояние");

    private LrkResult? result;
    private TraceResult? trace;
    private CancellationTokenSource? busy;
    private bool loading;
    private int stateShown = -1;

    public MainForm()
    {
        Text = "Проверка КС-грамматики на LR(k)";
        BackColor = Background;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(955, 645);
        var screen = Screen.GetWorkingArea(MousePosition);
        Size = new Size(Math.Min(1230, screen.Width), Math.Min(820, screen.Height));
        KeyPreview = true;
        rules.MinimumSize = new Size(0, 55);
        inputErrors.ContainerControl = this;
        inputErrors.SetIconPadding(terminals, 3);
        inputErrors.SetIconPadding(nonterminals, 3);
        inputErrors.SetIconPadding(start, 3);
        inputErrors.SetIconPadding(rules, 3);

        var shell = Vertical();
        shell.Padding = new Padding(18, 14, 18, 12);
        Row(shell, Header(), 66);
        Row(shell, Workspace());
        Row(shell, operation, 24);
        Controls.Add(shell);

        ConfigureResultPage();
        ConfigureTablePage();
        ConfigureAutomatonPage();
        ConfigureTracePage();
        ConfigureHelpPage();

        examplePicker.Items.Add("Выберите пример");
        foreach (var example in Workflow.Examples)
            examplePicker.Items.Add(example.Name);
        examplePicker.SelectedIndexChanged += (_, _) =>
        {
            if (examplePicker.SelectedIndex > 0 && !loading)
            {
                var example = Workflow.Examples[examplePicker.SelectedIndex - 1];
                LoadInput(example.Grammar, example.Chain, examplePicker.SelectedIndex);
            }
        };
        foreach (var editor in new[] { terminals, nonterminals, start, rules })
            editor.TextChanged += (_, _) => InputChanged();
        kInput.ValueChanged += (_, _) => InputChanged();
        chain.TextChanged += (_, _) =>
        {
            if (!loading) ClearTrace();
        };
        check.Click += async (_, _) => await CheckAsync();
        cancel.Click += (_, _) => busy?.Cancel();
        openGrammar.Click += (_, _) => LoadGrammar();
        saveGrammar.Click += (_, _) => SaveGrammar();
        saveProtocol.Click += (_, _) => SaveProtocol();
        parse.Click += async (_, _) => await ParseAsync();
        FormClosing += (_, _) => busy?.Cancel();
        LoadInput(Workflow.Examples[0].Grammar, Workflow.Examples[0].Chain, 1);
        operation.Text = "Готовый пример загружен. Нажмите «Проверить грамматику».";
    }

    private Control Header()
    {
        var panel = Vertical();
        Row(panel, Label("Проверка КС-грамматики на LR(k)", size: 23, bold: true), 36);
        Row(panel, Label("Задайте грамматику, проверьте её для выбранного k и проследите разбор цепочки.", muted: true), 25);
        return panel;
    }

    private Control Workspace()
    {
        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.Controls.Add(GrammarEditor(), 0, 0);
        tabs.TabPages.AddRange([resultPage, tablePage, automatonPage, tracePage, helpPage]);
        workspace.Controls.Add(tabs, 1, 0);
        return workspace;
    }

    private Control GrammarEditor()
    {
        var panel = Vertical();
        panel.Margin = new Padding(0, 0, 15, 0);
        panel.Padding = new Padding(13, 9, 25, 11);
        panel.BackColor = Color.White;

        Row(panel, Label("Грамматика", size: 15, bold: true), 30);
        Row(panel, examplePicker, 32);
        Row(panel, Toolbar(openGrammar, saveGrammar), 34);
        Field(panel, "Терминалы Vₜ", "Символы, которые встречаются во входе.", terminals);
        Field(panel, "Нетерминалы Vₙ", "Символы, которые заменяются по правилам.", nonterminals);
        Field(panel, "Начальный символ S", "Нетерминал, с которого начинается вывод.", start);

        var lookahead = Toolbar(Label("Длина просмотра k", bold: true), kInput);
        kInput.Margin = new Padding(9, 3, 3, 3);
        Row(panel, lookahead, 34);
        Row(panel, Label("Сколько следующих символов учитывается.\n0 — решения без просмотра входа.", muted: true), 39);
        Row(panel, Label("Правила P", bold: true), 22);
        Row(panel, Label("A → aB | ε. Каждое правило с новой строки.", muted: true, size: 9), 23);
        rules.AccessibleName = "Правила P";
        kInput.AccessibleName = "Длина просмотра k";
        examplePicker.AccessibleName = "Готовые примеры грамматик";
        Row(panel, rules);
        Row(panel, Toolbar(check, cancel), 37);
        return panel;
    }

    private static void Field(TableLayoutPanel panel, string name, string hint, Control input)
    {
        Row(panel, Label(name, bold: true), 22);
        input.AccessibleName = name;
        Row(panel, input, 29);
        Row(panel, Label(hint, muted: true, size: 9), 21);
    }

    private GrammarInput CurrentInput() =>
        new(terminals.Text, nonterminals.Text, start.Text, rules.Text, (int)kInput.Value);

    private void LoadInput(GrammarInput input, string inputChain, int exampleIndex = 0)
    {
        loading = true;
        try
        {
            terminals.Text = input.Terminals;
            nonterminals.Text = input.Nonterminals;
            start.Text = input.Start;
            rules.Text = input.Rules.ReplaceLineEndings(Environment.NewLine);
            kInput.Value = input.K;
            chain.Text = inputChain;
            examplePicker.SelectedIndex = exampleIndex;
        }
        finally
        {
            loading = false;
        }
        InvalidateResult();
        tabs.SelectedTab = resultPage;
    }

    private void InputChanged()
    {
        if (loading) return;
        loading = true;
        try
        {
            examplePicker.SelectedIndex = 0;
        }
        finally
        {
            loading = false;
        }
        InvalidateResult();
    }

    private void InvalidateResult()
    {
        result = null;
        inputErrors.Clear();
        conflicts.RowCount = 0;
        visibleItems = [];
        items.RowCount = 0;
        stateShown = -1;
        graph.SetGraph(0, [], []);
        tableState.Items.Clear();
        statePicker.Items.Clear();
        actionDetail.Clear();
        transitionDetail.Clear();
        itemDetail.Clear();
        ShowConflictSection(false);
        resultGuide.Text = "LR(k) определяет, можно ли продолжать разбор однозначно, используя до k следующих символов входа. "
            + "Программа проверяет все достижимые состояния и правые контексты. После проверки здесь появится результат и путь к его подробностям.";
        conflictsOnly.Checked = false;
        PopulateActionTable();
        SetVerdict("Грамматика готова к проверке", Pending,
            "Нажмите «Проверить грамматику». Здесь появится вывод для выбранного k, число состояний и объяснение конфликтов.");
        operation.Text = "Грамматика изменена. Проверьте её для получения результата.";
        ClearTrace();
        UpdateControls();
    }

    private async Task CheckAsync()
    {
        if (busy is not null) return;
        var input = CurrentInput();
        InvalidateResult();
        tabs.SelectedTab = resultPage;
        SetVerdict("Идёт проверка…", Pending, "Программа вычисляет FIRSTₖ, замыкания, переходы и таблицу действий.");
        operation.Text = "Построение канонического автомата LR(k)…";
        using var source = new CancellationTokenSource();
        busy = source;
        UpdateControls();

        try
        {
            var checkedGrammar = await Task.Run(() => Workflow.Check(input, source.Token), source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (!IsDisposed) ShowResult(checkedGrammar);
        }
        catch (GrammarException failure)
        {
            if (IsDisposed) return;
            var field = failure.Field switch
            {
                "Terminals" => terminals,
                "Nonterminals" => nonterminals,
                "Start" => start,
                _ => rules
            };
            inputErrors.SetError(field, failure.Message);
            SetVerdict("Исправьте ввод", Error, failure.Message);
            operation.Text = "Неправильно задана грамматика.";
            BeginInvoke(() => field.Focus());
        }
        catch (ResourceLimitException failure)
        {
            if (!IsDisposed) Interrupted(failure.Message);
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed) Interrupted("Операция отменена пользователем.");
        }
        catch (Exception failure)
        {
            if (!IsDisposed) Interrupted(failure.Message);
        }
        finally
        {
            busy = null;
            if (!IsDisposed) UpdateControls();
        }
    }

    private void Interrupted(string reason)
    {
        SetVerdict("Проверка не завершена", Error, reason + Environment.NewLine
            + "Для выбранного k вывод о принадлежности к LR(k) не получен.");
        operation.Text = "Проверка не завершена.";
    }

    private void ShowResult(LrkResult computed)
    {
        result = computed;
        var count = computed.Conflicts.Count;
        var warning = computed.Grammar.Warnings.Count == 0 ? "" : Environment.NewLine
            + Environment.NewLine + string.Join(Environment.NewLine, computed.Grammar.Warnings);
        var notation = computed.K == 0
            ? "При k = 0 все действия состояния, включая допуск, сравниваются для контекста ε."
            : "Знак $ обозначает конец входа и добавляется, если до конца осталось меньше k символов.";
        SetVerdict(count == 0 ? $"Грамматика является LR({computed.K})" : $"Грамматика не является LR({computed.K})",
            count == 0 ? Accent : Error,
            $"Состояний: {computed.States.Count}  ·  Заполненных ячеек: {computed.Cells.Count}  ·  Конфликтных ячеек: {count}"
            + Environment.NewLine + notation + warning);

        tableState.Items.Add("Все состояния");
        foreach (var state in computed.States)
        {
            tableState.Items.Add(StateName(state.Id));
            statePicker.Items.Add(StateName(state.Id));
        }
        tableState.SelectedIndex = 0;

        ShowConflictSection(count > 0);
        conflicts.RowCount = count;
        graph.SetGraph(computed.States.Count,
            computed.States.SelectMany(state => state.Transitions.Select(edge =>
                new GraphEdge(state.Id, edge.Value, edge.Key.ToString()))),
            computed.Conflicts.Select(conflict => conflict.StateId).Distinct());
        SelectState(computed.States[0].Id);
        if (count == 0)
        {
            resultGuide.Text = "Для каждого состояния и правого контекста найдено не более одного действия. "
                + "Откройте «Разбор цепочки», чтобы увидеть работу анализатора.";
        }
        else
        {
            SelectRow(conflicts, 0);
            DisplayConflict();
        }
        operation.Text = count == 0 ? "Проверка завершена. Доступен пошаговый разбор." : "Проверка завершена. Выберите конфликт для подробностей.";
        ClearTrace();
    }

    private void SetVerdict(string title, Color color, string details)
    {
        verdict.Text = title;
        verdict.ForeColor = color;
        summary.Text = details.ReplaceLineEndings(Environment.NewLine);
    }

    private void UpdateControls()
    {
        var editable = busy is null;
        foreach (var control in new Control[] { terminals, nonterminals, start, rules, kInput, examplePicker, openGrammar, saveGrammar })
            control.Enabled = editable;
        check.Enabled = editable;
        cancel.Enabled = !editable;
        parse.Enabled = editable && result is { Conflicts.Count: 0 };
        chain.Enabled = editable;
        openTrace.Enabled = result is { Conflicts.Count: 0 };
        saveProtocol.Enabled = editable && result is not null;
        saveGraph.Enabled = editable && result is not null;
        openState.Enabled = SelectedConflict() is not null;
        openCell.Enabled = openState.Enabled;
        tableOpenState.Enabled = SelectedActionCell() is not null;
        UpdateStepControls();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Enter))
        {
            if (tabs.SelectedTab == tracePage && parse.Enabled) _ = ParseAsync();
            else if (check.Enabled) _ = CheckAsync();
            return true;
        }
        if (keyData == (Keys.Control | Keys.O) && openGrammar.Enabled)
        {
            LoadGrammar();
            return true;
        }
        if (keyData == (Keys.Control | Keys.S) && saveGrammar.Enabled)
        {
            SaveGrammar();
            return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }

    private void LoadGrammar()
    {
        using var picker = new OpenFileDialog { Filter = "Грамматика (*.cfg)|*.cfg|Все файлы|*.*", CheckFileExists = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        TryFile(() =>
        {
            LoadInput(Workflow.OpenGrammar(picker.FileName), "");
            operation.Text = "Грамматика открыта. Нажмите «Проверить грамматику».";
        });
    }

    private void SaveGrammar() => SaveFile("Грамматика (*.cfg)|*.cfg", "grammar.cfg",
        path => Workflow.SaveGrammar(path, CurrentInput()));

    private void SaveProtocol()
    {
        if (result is { } computed)
            SaveFile("Текстовый протокол (*.txt)|*.txt", "protocol.txt", path => Workflow.ExportProtocol(path, computed, trace));
    }

    private void SaveFile(string filter, string name, Action<string> save)
    {
        using var picker = new SaveFileDialog { Filter = filter, FileName = name, AddExtension = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        TryFile(() =>
        {
            save(picker.FileName);
            operation.Text = $"Сохранён файл {Path.GetFileName(picker.FileName)}.";
        });
    }

    private void TryFile(Action operationToRun)
    {
        try
        {
            operationToRun();
        }
        catch (Exception failure)
        {
            MessageBox.Show(this, failure.Message, "Операция с файлом не выполнена", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

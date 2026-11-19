using Lrk.Core;

namespace Lrk.WinForms;

internal sealed partial class MainForm
{
    private IReadOnlyList<ActionCell> visibleActions = [];
    private IReadOnlyList<LrItem> visibleItems = [];
    private bool settingState;

    private void ConfigureResultPage()
    {
        var panel = PageContents(resultPage);
        Row(panel, verdict, 43);
        Row(panel, summary, 92);
        Row(panel, Toolbar(openTrace, saveProtocol), 38);
        Row(panel, resultBody);
        resultGuide.Dock = DockStyle.Fill;
        resultBody.Controls.Add(resultGuide);
        resultBody.Controls.Add(conflictSection);
        Row(conflictSection, Label("Конфликтные ячейки", size: 13, bold: true), 34);
        SetColumns(conflicts, ("Состояние", 22), ("Контекст", 19), ("Конкурирующие действия", 59));
        conflicts.VirtualMode = true;
        conflicts.CellValueNeeded += (_, e) =>
        {
            if (result is { } computed && (uint)e.RowIndex < (uint)computed.Conflicts.Count)
                e.Value = CellText(computed.Conflicts[e.RowIndex], e.ColumnIndex);
        };
        Row(conflictSection, conflicts);
        Row(conflictSection, conflictDetail, 150);
        Row(conflictSection, Toolbar(openState, openCell), 38);
        conflicts.SelectionChanged += (_, _) => DisplayConflict();
        conflicts.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0) GoToState(SelectedConflict()?.StateId);
        };
        openState.Click += (_, _) => GoToState(SelectedConflict()?.StateId);
        openCell.Click += (_, _) =>
        {
            if (SelectedConflict() is { } conflict) GoToCell(conflict);
        };
        openTrace.Click += (_, _) =>
        {
            tabs.SelectedTab = tracePage;
            chain.Focus();
        };
    }

    private void ShowConflictSection(bool show)
    {
        conflictSection.Visible = show;
        resultGuide.Visible = !show;
    }

    private ActionCell? SelectedConflict() =>
        result is { } computed ? CurrentValue(conflicts, computed.Conflicts) : null;

    private void DisplayConflict()
    {
        if (result is { } computed && SelectedConflict() is { } conflict)
            conflictDetail.Text = Workflow.ExplainConflict(computed, conflict).ReplaceLineEndings(Environment.NewLine);
        openState.Enabled = SelectedConflict() is not null;
        openCell.Enabled = openState.Enabled;
    }

    private void ConfigureTablePage()
    {
        var panel = PageContents(tablePage);
        Row(panel, Label("Таблица действий", size: 16, bold: true), 35);
        Row(panel, Label("Каждая строка — заполненная ячейка ACTION. Действие выбирается по состоянию и контексту.",
            muted: true), 44);
        Row(panel, Toolbar(Label("Состояние:", bold: true), tableState, conflictsOnly), 37);
        SetColumns(actionTable, ("Состояние", 20), ("Правый контекст", 27), ("Действия", 53));
        actionTable.VirtualMode = true;
        actionTable.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= visibleActions.Count) return;
            e.Value = CellText(visibleActions[e.RowIndex], e.ColumnIndex);
        };
        actionTable.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < visibleActions.Count && visibleActions[e.RowIndex].Actions.Count > 1
                && e.CellStyle is { } style)
                style.ForeColor = Error;
        };
        Row(panel, actionTable);
        Row(panel, actionDetail, 115);
        Row(panel, Toolbar(tableOpenState), 38);
        tableState.SelectedIndexChanged += (_, _) => PopulateActionTable();
        conflictsOnly.CheckedChanged += (_, _) => PopulateActionTable();
        actionTable.SelectionChanged += (_, _) => DisplayActionCell();
        tableOpenState.Click += (_, _) => GoToState(SelectedActionCell()?.StateId);
        actionTable.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0) GoToState(SelectedActionCell()?.StateId);
        };
    }

    private void PopulateActionTable()
    {
        var stateId = tableState.SelectedIndex - 1;
        IReadOnlyList<ActionCell> source = result is { } computed
            ? (conflictsOnly.Checked ? computed.Conflicts : computed.Cells)
            : [];
        visibleActions = stateId < 0 ? source : source.Where(cell => cell.StateId == stateId).ToArray();
        actionTable.RowCount = visibleActions.Count;
        actionTable.Invalidate();
        SelectRow(actionTable, 0);
        DisplayActionCell();
    }

    private ActionCell? SelectedActionCell() => CurrentValue(actionTable, visibleActions);

    private string CellText(ActionCell cell, int column) => column switch
    {
        0 => StateName(cell.StateId),
        1 => Workflow.FormatContext(cell.Context),
        _ => string.Join("  /  ", cell.Actions.Select(ActionTitle))
    };

    private void DisplayActionCell()
    {
        var cell = SelectedActionCell();
        tableOpenState.Enabled = cell is not null;
        if (cell is null || result is not { } computed)
        {
            actionDetail.Text = result is null
                ? "Таблица появится после проверки грамматики."
                : "Для выбранного фильтра заполненных ячеек нет.";
            return;
        }
        actionDetail.Text = cell.Actions.Count > 1
            ? Workflow.ExplainConflict(computed, cell).ReplaceLineEndings(Environment.NewLine)
            : $"{StateName(cell.StateId)}, контекст {Workflow.FormatContext(cell.Context)}.{Environment.NewLine}"
                + ExplainCellAction(computed, cell, cell.Actions[0]);
    }

    private static string ExplainCellAction(LrkResult computed, ActionCell cell, LrAction action)
    {
        var title = Workflow.ExplainAction(computed, action);
        var item = computed.GetSourceItem(cell.StateId, cell.Context, action);
        var origin = item is null ? "" : $" Ситуация {Workflow.FormatItem(computed, item)}.";
        var explanation = action.Kind switch
        {
            ActionKind.Shift => "Считывается один символ входа. Символ и состояние перехода добавляются в магазин.",
            ActionKind.Reduce => "Правая часть правила в магазине заменяется нетерминалом из левой части. Вход остаётся прежним.",
            _ => "Начальный символ распознан. Цепочка принимается, если вход исчерпан."
        };
        return $"{title}.{origin} {explanation}";
    }

    private void GoToCell(ActionCell match)
    {
        tabs.SelectedTab = tablePage;
        tableState.SelectedIndex = match.StateId + 1;
        for (var index = 0; index < visibleActions.Count; index++)
        {
            if (visibleActions[index].Context != match.Context) continue;
            SelectRow(actionTable, index);
            actionTable.FirstDisplayedScrollingRowIndex = index;
            break;
        }
        actionTable.Focus();
    }

    private void ConfigureAutomatonPage()
    {
        var panel = PageContents(automatonPage);
        Row(panel, Label("Канонический автомат", size: 16, bold: true), 35);
        Row(panel, Toolbar(Label("Состояние:", bold: true), statePicker, saveGraph), 38);
        Row(panel, graph, 58f);
        Row(panel, Label("Ситуации выбранного состояния", bold: true), 26);
        SetColumns(items, ("Ситуация [правило с точкой, контекст]", 100));
        items.VirtualMode = true;
        items.CellValueNeeded += (_, e) =>
        {
            if (result is { } computed && (uint)e.RowIndex < (uint)visibleItems.Count)
                e.Value = Workflow.FormatItem(computed, visibleItems[e.RowIndex]);
        };
        Row(panel, items, 42f);
        Row(panel, itemDetail, 60);
        Row(panel, transitionDetail, 49);
        statePicker.SelectedIndexChanged += (_, _) =>
        {
            if (statePicker.SelectedIndex >= 0) SelectState(statePicker.SelectedIndex);
        };
        graph.SelectedStateChanged += (_, _) =>
        {
            if (graph.SelectedState is { } id) SelectState(id);
        };
        items.SelectionChanged += (_, _) => DisplaySituation();
        saveGraph.Click += (_, _) => SaveFile("Граф Graphviz (*.dot)|*.dot", "automaton.dot",
            path => File.WriteAllText(path, graph.ExportDot()));
    }

    private void GoToState(int? stateId)
    {
        if (stateId is not { } id) return;
        tabs.SelectedTab = automatonPage;
        SelectState(id);
        statePicker.Focus();
    }

    private void SelectState(int id)
    {
        if (settingState || stateShown == id || result is not { } computed || (uint)id >= (uint)computed.States.Count)
            return;
        settingState = true;
        try
        {
            stateShown = id;
            statePicker.SelectedIndex = id;
            graph.SelectedState = id;
            var state = computed.States[id];
            visibleItems = state.Items;
            items.RowCount = visibleItems.Count;
            items.Invalidate();
            SelectRow(items, 0);
            DisplaySituation();
            transitionDetail.Text = state.Transitions.Count == 0
                ? "Из этого состояния нет переходов."
                : "Переходы: " + string.Join("    ", state.Transitions.Select(edge => $"{edge.Key} → {StateName(edge.Value)}"));
        }
        finally
        {
            settingState = false;
        }
    }

    private void DisplaySituation()
    {
        if (result is not { } computed || CurrentValue(items, visibleItems) is not { } item)
        {
            itemDetail.Clear();
            return;
        }
        var production = computed.Productions[item.RuleId];
        var progress = item.Dot == production.Right.Length
            ? item.RuleId == 0
                ? "Добавленное начальное правило завершено. Эта ситуация даёт допуск при соответствующем контексте."
                : $"Правая часть распознана целиком. При контексте {Workflow.FormatContext(item.Context)} эта ситуация даёт свёртку."
            : $"Точка показывает ход распознавания. Следующий символ правила — «{production.Right[item.Dot]}». "
                + $"После всей правой части ожидается контекст {Workflow.FormatContext(item.Context)}.";
        itemDetail.Text = progress;
    }

    private void ConfigureTracePage()
    {
        var panel = PageContents(tracePage);
        Row(panel, Label("Пошаговый разбор цепочки", size: 16, bold: true), 35);
        Row(panel, Label("Введите терминалы без разделителей. Для пустой цепочки оставьте поле пустым или введите ε.",
            muted: true), 43);
        var entry = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        entry.Controls.Add(chain, 0, 0);
        entry.Controls.Add(parse, 1, 0);
        chain.AccessibleName = "Входная цепочка";
        Row(panel, entry, 35);
        parseVerdict.AutoEllipsis = false;
        Row(panel, parseVerdict, 58);
        SetColumns(steps, ("Шаг", 12), ("Состояние", 19), ("Контекст", 19), ("Действие", 50));
        Row(panel, steps);
        Row(panel, stepDetail, 158);
        Row(panel, Toolbar(prevStep, nextStep, stepState), 38);
        steps.SelectionChanged += (_, _) => DisplayStep();
        prevStep.Click += (_, _) => MoveStep(-1);
        nextStep.Click += (_, _) => MoveStep(1);
        stepState.Click += (_, _) => GoToState(SelectedTraceStep()?.StateId);
    }

    private async Task ParseAsync()
    {
        if (busy is not null || result is not { Conflicts.Count: 0 } computed) return;
        var input = chain.Text;
        ClearTrace();
        using var source = new CancellationTokenSource();
        busy = source;
        UpdateControls();
        parseVerdict.Text = "Идёт разбор цепочки…";
        operation.Text = "Построение пошагового разбора…";
        try
        {
            var parsed = await Task.Run(() => Workflow.Trace(computed, input, source.Token), source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (IsDisposed) return;
            trace = parsed;
            parseVerdict.ForeColor = parsed.Status == TraceStatus.Accepted ? Accent : Error;
            parseVerdict.Text = parsed.Message;
            foreach (var step in parsed.Steps)
                steps.Rows.Add(step.Number, StateName(step.StateId), Workflow.FormatContext(step.Context), step.Action);
            SelectRow(steps, 0);
            DisplayStep();
            if (parsed.Steps.Count == 0) stepDetail.Text = parsed.Message;
            operation.Text = "Разбор завершён. Выберите шаг, чтобы увидеть его объяснение.";
        }
        catch (OperationCanceledException)
        {
            if (IsDisposed) return;
            parseVerdict.Text = "Разбор отменён.";
            operation.Text = parseVerdict.Text;
        }
        catch (Exception failure)
        {
            if (IsDisposed) return;
            parseVerdict.Text = "Разбор не завершён.";
            parseVerdict.ForeColor = Error;
            stepDetail.Text = failure.Message;
            operation.Text = parseVerdict.Text;
        }
        finally
        {
            busy = null;
            if (!IsDisposed) UpdateControls();
        }
    }

    private void ClearTrace()
    {
        trace = null;
        steps.Rows.Clear();
        stepDetail.Text = "После разбора выберите шаг, чтобы увидеть содержимое магазина и причину действия.";
        parseVerdict.ForeColor = Pending;
        parseVerdict.Text = result switch
        {
            null => "Сначала проверьте грамматику.",
            { Conflicts.Count: > 0 } => "Пошаговый разбор доступен после проверки без конфликтов.",
            _ => "Введите цепочку и нажмите «Разобрать»."
        };
        UpdateStepControls();
    }

    private TraceStep? SelectedTraceStep() =>
        trace is { } parsed ? CurrentValue(steps, parsed.Steps) : null;

    private void DisplayStep()
    {
        if (SelectedTraceStep() is { } step)
        {
            stepDetail.Text = $"Прочитано: {DisplayChain(step.Consumed)}        Осталось: {DisplayChain(step.Remaining)}"
                + $"{Environment.NewLine}Магазин: {DisplayChain(step.Stack)}        Состояния в магазине: {step.States}"
                + $"{Environment.NewLine}{Environment.NewLine}{step.Explanation}";
        }
        UpdateStepControls();
    }

    private void UpdateStepControls()
    {
        var index = steps.CurrentCell?.RowIndex ?? -1;
        prevStep.Enabled = index > 0;
        nextStep.Enabled = index >= 0 && index + 1 < steps.RowCount;
        stepState.Enabled = SelectedTraceStep() is not null;
    }

    private void MoveStep(int offset)
    {
        if (steps.CurrentCell is { RowIndex: var index }) SelectRow(steps, index + offset);
    }

    private void ConfigureHelpPage()
    {
        var panel = PageContents(helpPage);
        Row(panel, Label("Грамматика и разбор", size: 16, bold: true), 35);
        var help = TextBlock();
        help.Text = """
            Что проверяет программа
            Для каждого достижимого состояния и правого контекста выбирается действие. Если ячейка таблицы соответствует нескольким разным действиям, грамматика не является LR(k) для выбранного k.

            Как задать грамматику
            Введите терминалы Vₜ (входные символы), нетерминалы Vₙ (символы, заменяемые по правилам), начальный символ S и правила P. Каждый символ задаётся одним знаком. Правило можно записать как S -> aS | ε или S → aS | ε. ε — пустая цепочка.

            Ситуация [A → α · β, u]
            Точка показывает, что α уже распознана, β остаётся распознать. Правый контекст u описывает продолжение после всего правила. Контекст входа при разборе вычисляется по его непрочитанному остатку. $ означает конец входа. При k = 0 все действия состояния, включая допуск, относятся к одному контексту ε.

            Сдвиг, свёртка и допуск
            При сдвиге анализатор читает один символ и кладёт его в магазин вместе с новым состоянием. При свёртке он применяет правило в обратном направлении: заменяет распознанную правую часть в магазине на нетерминал слева, затем переходит по нему. Вход при свёртке остаётся на месте. При допуске цепочка принята.

            Как воспроизвести конфликт
            Выберите пример с A → c и B → c. При k = 1 после c обе свёртки имеют один контекст a. При k = 2 свёртки относятся к разным ячейкам, aa и ab. Проверьте грамматику при обоих k и разберите caa либо cab.

            Ctrl+Enter — проверка или запуск разбора на вкладке «Разбор цепочки».
            Ctrl+O — открыть грамматику, Ctrl+S — сохранить грамматику.
            """.ReplaceLineEndings(Environment.NewLine);
        Row(panel, help);
    }

    private string ActionTitle(LrAction action) => action.Kind switch
    {
        ActionKind.Shift => action.Target < 0 ? "Сдвиг" : $"Сдвиг в {StateName(action.Target)}",
        ActionKind.Reduce => $"Свёртка {result?.Productions[action.Target]}",
        _ => "Допуск"
    };

    private static string StateName(int id) => $"I{id}";
    private static string DisplayChain(string value) => value.Length == 0 ? "ε" : value;
}

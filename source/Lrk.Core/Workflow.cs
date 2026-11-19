namespace Lrk.Core;

public sealed record GrammarInput(string Terminals, string Nonterminals, string Start, string Rules, int K);

public sealed record Example(string Name, GrammarInput Grammar, string Chain);

public enum TraceStatus
{
    Accepted,
    Rejected,
    Blocked,
    Limit
}

public sealed record TraceStep(
    int Number, int StateId, string States, string Stack, string Consumed,
    string Remaining, string Context, string Action, string Explanation);

public sealed record TraceResult(TraceStatus Status, string Message, IReadOnlyList<TraceStep> Steps);

public static partial class Workflow
{
    private const int MaxTraceSteps = 2000;
    private const int MaxTraceInput = 2000;

    public static IReadOnlyList<Example> Examples { get; } = Array.AsReadOnly(new[]
    {
        new Example("Две свёртки: сравнение k = 1 и k = 2",
            new GrammarInput("a b c", "S A B", "S", "S → Aaa | Bab\nA → c\nB → c", 2), "caa"),
        new Example("Пустая цепочка и конфликт LR(0)",
            new GrammarInput("a", "S", "S", "S → Sa | ε", 0), "aaa"),
        new Example("Простой пример LR(0)",
            new GrammarInput("a b", "S", "S", "S → ab", 0), "ab"),
        new Example("Неоднозначная грамматика",
            new GrammarInput("a +", "S", "S", "S → S+S | a", 1), "a+a+a")
    });

    public static LrkResult Check(GrammarInput input, CancellationToken cancellationToken = default)
        => LrkBuilder.Build(Grammar.Parse(input.Terminals, input.Nonterminals, input.Start, input.Rules),
            input.K, cancellationToken);

    public static string FormatContext(string context) => context.Length == 0 ? "ε" : context;

    public static string FormatItem(LrkResult result, LrItem item)
    {
        var rule = result.Productions[item.RuleId];
        return $"[{rule.Left} → {rule.Right.Insert(item.Dot, "·")}, {FormatContext(item.Context)}]";
    }

    public static string ExplainAction(LrkResult result, LrAction action) => action.Kind switch
    {
        ActionKind.Shift => action.Target < 0 ? "Сдвиг" : $"Сдвиг в I{action.Target}",
        ActionKind.Reduce => $"Свёртка по правилу {result.Productions[action.Target]}",
        ActionKind.Accept => "Допуск",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    public static string ExplainConflict(LrkResult result, ActionCell cell)
    {
        var previous = cell.StateId == 0 ? " I0 — начальное состояние." : "";
        var context = result.K == 0
            ? "При k = 0 все действия этого состояния записываются для контекста ε."
            : $"Для правого контекста «{FormatContext(cell.Context)}» в одной ячейке ACTION есть несколько действий.";
        var actions = cell.Actions.Select(action =>
        {
            var item = result.GetSourceItem(cell.StateId, cell.Context, action);
            var origin = item is null ? "" : $" Ситуация {FormatItem(result, item)}.";
            var explanation = action.Kind switch
            {
                ActionKind.Shift => " Считывается один терминал с входа.",
                ActionKind.Reduce => " Правая часть правила заменяется в магазине его левой частью.",
                _ => " Разбор завершается, когда распознан начальный символ и вход исчерпан."
            };
            return $"• {ExplainAction(result, action)}.{origin}{explanation}";
        });
        return $"Состояние I{cell.StateId}.{previous} {context}\n\n{string.Join("\n\n", actions)}";
    }

    public static TraceResult Trace(LrkResult result, string input, CancellationToken cancellationToken = default)
    {
        if (result.Conflicts.Count > 0)
            return new(TraceStatus.Blocked, "Разбор недоступен при конфликтах в таблице действий.", []);

        input = input.Trim();
        if (input == "ε")
            input = "";
        if (input.Length > MaxTraceInput)
            return new(TraceStatus.Limit, $"Для пошагового разбора доступно до {MaxTraceInput} символов.", []);

        var unknown = input.Select((symbol, index) => (symbol, index))
            .Where(pair => !result.Grammar.Terminals.Contains(pair.symbol)).Take(1).ToArray();
        if (unknown.Length > 0)
        {
            var (symbol, index) = unknown[0];
            return new(TraceStatus.Rejected,
                $"Символ «{symbol}» в позиции {index + 1} не входит в терминальный алфавит Vₜ.", []);
        }

        var states = new List<int> { 0 };
        var stack = new List<char>();
        var steps = new List<TraceStep>();
        var offset = 0;

        while (steps.Count < MaxTraceSteps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stateId = states[^1];
            var state = result.States[stateId];
            var context = result.GetContext(input, offset);
            var actions = result.GetActions(stateId, context);
            var next = offset < input.Length ? input[offset] : (char?)null;

            void Add(string action, string explanation) =>
                steps.Add(new(steps.Count + 1, stateId, string.Join(" ", states),
                    new string(stack.ToArray()), input[..offset], input[offset..], context, action, explanation));

            if (actions.Count == 0)
            {
                var message = $"В состоянии I{stateId} для контекста «{FormatContext(context)}» нет действия.";
                var available = result.Cells.Where(cell => cell.StateId == stateId)
                    .Take(7).Select(cell => $"«{FormatContext(cell.Context)}»").ToArray();
                if (available.Length > 0)
                {
                    message += " Контексты с действиями: " + string.Join(", ", available.Take(6));
                    message += available.Length > 6 ? " и другие." : ".";
                }
                Add("Ошибка разбора", message);
                return new(TraceStatus.Rejected, message, steps);
            }

            var action = actions[0];
            var title = ExplainAction(result, action);
            var reason = $"В I{stateId} при контексте «{FormatContext(context)}» выбрано это действие.";
            var item = result.GetSourceItem(stateId, context, action);
            if (item is not null && !(action.Kind == ActionKind.Shift && result.K == 0))
                reason += $" Ситуация {FormatItem(result, item)}.";

            switch (action.Kind)
            {
                case ActionKind.Shift:
                    if (next is null || !state.Transitions.TryGetValue(next.Value, out var target)
                        || (action.Target >= 0 && target != action.Target))
                    {
                        var expected = action.Target < 0
                            ? string.Join(" или ", state.Transitions.Keys
                                .Where(result.Grammar.Terminals.Contains).Select(symbol => $"«{symbol}»"))
                            : item is null
                                ? "символ, указанный в таблице"
                                : $"«{result.Productions[item.RuleId].Right[item.Dot]}»";
                        var actual = next is null ? "достигнут конец входа" : $"на входе «{next}»";
                        var message = $"Для сдвига ожидается {expected}. Сейчас {actual}.";
                        Add("Ошибка разбора", message);
                        return new(TraceStatus.Rejected, message, steps);
                    }
                    Add($"Сдвиг «{next}» в I{target}", $"{reason} Символ «{next}» считывается с входа и кладётся в магазин. "
                        + $"Состояние меняется на I{target}.");
                    stack.Add(next.Value);
                    states.Add(target);
                    offset++;
                    break;

                case ActionKind.Reduce:
                    var rule = result.Productions[action.Target];
                    if (rule.Right.Length > stack.Count)
                        throw new InvalidOperationException("Для свёртки в магазине недостаточно символов.");
                    var fromId = states[states.Count - 1 - rule.Right.Length];
                    var gotoState = result.States[fromId].Transitions[rule.Left];
                    var replacement = rule.Right.Length == 0
                        ? $"По правилу с ε в магазин добавляется «{rule.Left}»."
                        : $"В магазине «{rule.Right}» заменяется на «{rule.Left}», как при применении правила в обратном направлении.";
                    Add(title, $"{reason} {replacement} Вход остаётся прежним. "
                        + $"Из I{fromId} по «{rule.Left}» выполняется переход в I{gotoState}.");
                    stack.RemoveRange(stack.Count - rule.Right.Length, rule.Right.Length);
                    states.RemoveRange(states.Count - rule.Right.Length, rule.Right.Length);
                    stack.Add(rule.Left);
                    states.Add(gotoState);
                    break;

                case ActionKind.Accept:
                    if (offset < input.Length)
                    {
                        const string message = "Начальный символ распознан, но часть цепочки ещё не прочитана.";
                        Add("Ошибка разбора", message);
                        return new(TraceStatus.Rejected, message, steps);
                    }
                    Add(title, $"{reason} Начальный символ «{result.Grammar.Start}» распознан, "
                        + "вход полностью прочитан. Цепочка принята.");
                    return new(TraceStatus.Accepted, "Цепочка принадлежит языку грамматики.", steps);

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return new(TraceStatus.Limit,
            $"Разбор остановлен после {MaxTraceSteps} шагов. Принадлежность цепочки не установлена.", steps);
    }
}

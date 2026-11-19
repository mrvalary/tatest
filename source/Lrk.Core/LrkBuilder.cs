using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Lrk.Core;

public static class LrkBuilder
{
    public static LrkResult Build(Grammar grammar, int k, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grammar);
        if (k is < 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(k), "Укажите k от 0 до 32.");

        cancellationToken.ThrowIfCancellationRequested();
        return new Construction(grammar, k, cancellationToken).Run();
    }

    private sealed class Construction
    {
        private const int MaxStates = 1_500;
        private const int MaxItems = 60_000;
        private const int MaxContextsPerSet = 6_000;
        private const int MaxStoredContexts = 60_000;
        private const int MaxActionEntries = 50_000;
        private const int MaxOperations = 3_000_000;
        private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(10);

        private readonly Grammar grammar;
        private readonly int k;
        private readonly CancellationToken cancellationToken;
        private readonly Production[] productions;
        private readonly Dictionary<char, Production[]> byLeft;
        private readonly Dictionary<char, HashSet<string>> first;
        private readonly Dictionary<(int RuleId, int Dot), HashSet<string>> suffixFirst = new();
        private readonly Dictionary<LrItem[], int> stateByItems = new(new ItemSetComparer());
        private readonly List<LrItem[]> itemSets = new();
        private readonly List<LrState> states = new();
        private readonly Dictionary<(int StateId, string Context), Dictionary<LrAction, LrItem>> sources = new();
        private readonly Stopwatch timer = Stopwatch.StartNew();
        private int operations;
        private int itemCount;
        private int storedContexts;
        private int actionEntries;

        internal Construction(Grammar grammar, int k, CancellationToken cancellationToken)
        {
            this.grammar = grammar;
            this.k = k;
            this.cancellationToken = cancellationToken;

            char augmented = (grammar.Terminals.Contains('Z') || grammar.Nonterminals.Contains('Z')) ? '⊢' : 'Z';
            var startRule = new Production(0, augmented, grammar.Start.ToString());
            productions = new[] { startRule }.Concat(grammar.Productions).ToArray();
            byLeft = grammar.Productions.GroupBy(production => production.Left)
                .ToDictionary(group => group.Key, group => group.ToArray());
            first = grammar.Nonterminals.ToDictionary(symbol => symbol, _ => new HashSet<string>(StringComparer.Ordinal));
            foreach (char terminal in grammar.Terminals)
                first.Add(terminal, new HashSet<string>(StringComparer.Ordinal) { terminal.ToString() });
        }

        internal LrkResult Run()
        {
            if (k > 0)
                FindFirst();

            var start = new LrItem(0, 0, k == 0 ? string.Empty : LrkResult.EndOfInput.ToString());
            GetState(new[] { start });
            for (int id = 0; id < itemSets.Count; id++)
                BuildTransitionsAndActions(id);

            ActionCell[] cells = sources.OrderBy(entry => entry.Key.StateId)
                .ThenBy(entry => entry.Key.Context, StringComparer.Ordinal)
                .Select(entry => new ActionCell(entry.Key.StateId, entry.Key.Context,
                    Array.AsReadOnly(entry.Value.Keys.OrderBy(action => action.Kind).ThenBy(action => action.Target).ToArray())))
                .ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            return new LrkResult(grammar, k, Array.AsReadOnly(productions), states.AsReadOnly(),
                Array.AsReadOnly(cells), sources);
        }

        private void FindFirst()
        {
            bool changed;
            do
            {
                changed = false;
                foreach (Production production in grammar.Productions)
                {
                    Spend();
                    HashSet<string> prefixes = FirstOfSymbols(production.Right.AsSpan());
                    HashSet<string> target = first[production.Left];
                    foreach (string prefix in prefixes)
                    {
                        if (!target.Add(prefix))
                            continue;

                        changed = true;
                        CheckContextCount(target.Count);
                        RetainContexts(1);
                    }
                }
            }
            while (changed);
        }

        private HashSet<string> FirstOfSymbols(ReadOnlySpan<char> symbols)
        {
            var prefixes = new HashSet<string>(StringComparer.Ordinal) { string.Empty };
            foreach (char symbol in symbols)
            {
                Spend();
                HashSet<string> continuations = first[symbol];
                if (continuations.Count == 0)
                {
                    prefixes.Clear();
                    break;
                }

                // A saturated prefix remains valid only if every remaining symbol can derive terminals.
                if (prefixes.All(prefix => prefix.Length == k))
                    continue;

                var joined = new HashSet<string>(StringComparer.Ordinal);
                foreach (string prefix in prefixes)
                {
                    if (prefix.Length == k)
                    {
                        Spend();
                        joined.Add(prefix);
                        continue;
                    }

                    foreach (string continuation in continuations)
                    {
                        Spend();
                        joined.Add(JoinPrefix(prefix, continuation));
                        CheckContextCount(joined.Count);
                    }
                }

                prefixes = joined;
            }

            return prefixes;
        }

        private IEnumerable<string> ContextsAfter(int ruleId, int start, string context)
        {
            if (k == 0)
            {
                yield return string.Empty;
                yield break;
            }

            var key = (ruleId, start);
            if (!suffixFirst.TryGetValue(key, out HashSet<string>? prefixes))
            {
                prefixes = FirstOfSymbols(productions[ruleId].Right.AsSpan(start));
                RetainContexts(prefixes.Count + 1);
                suffixFirst.Add(key, prefixes);
            }

            foreach (string prefix in prefixes)
            {
                Spend();
                yield return JoinPrefix(prefix, context);
            }
        }

        private string JoinPrefix(string left, string right)
        {
            int fromRight = Math.Min(k - left.Length, right.Length);
            return fromRight == 0 ? left : string.Concat(left.AsSpan(), right.AsSpan(0, fromRight));
        }

        private int GetState(IEnumerable<LrItem> seed)
        {
            var closure = new HashSet<LrItem>();
            var work = new Queue<LrItem>();
            foreach (LrItem item in seed)
                Enqueue(item);

            while (work.TryDequeue(out LrItem? item))
            {
                Spend();
                string right = productions[item.RuleId].Right;
                if (item.Dot == right.Length || !byLeft.TryGetValue(right[item.Dot], out Production[]? expansions))
                    continue;

                foreach (string context in ContextsAfter(item.RuleId, item.Dot + 1, item.Context))
                {
                    foreach (Production expansion in expansions)
                        Enqueue(new LrItem(expansion.Id, 0, context));
                }
            }

            LrItem[] ordered = closure.OrderBy(item => item.RuleId).ThenBy(item => item.Dot)
                .ThenBy(item => item.Context, StringComparer.Ordinal).ToArray();
            if (stateByItems.TryGetValue(ordered, out int existing))
                return existing;

            if (itemSets.Count >= MaxStates)
                Limit("Количество состояний превысило предел построения.");
            itemCount += ordered.Length;
            if (itemCount > MaxItems)
                Limit("Общее число ситуаций превысило предел построения.");

            int id = itemSets.Count;
            stateByItems.Add(ordered, id);
            itemSets.Add(ordered);
            return id;

            void Enqueue(LrItem item)
            {
                Spend();
                if (!closure.Add(item))
                    return;
                if (closure.Count > MaxItems)
                    Limit("В одном состоянии слишком много ситуаций.");
                work.Enqueue(item);
            }
        }

        private void BuildTransitionsAndActions(int id)
        {
            LrItem[] items = itemSets[id];
            var groups = new SortedDictionary<char, List<LrItem>>();
            foreach (LrItem item in items)
            {
                Spend();
                string right = productions[item.RuleId].Right;
                if (item.Dot == right.Length)
                    continue;

                char symbol = right[item.Dot];
                if (!groups.TryGetValue(symbol, out List<LrItem>? group))
                {
                    group = new List<LrItem>();
                    groups.Add(symbol, group);
                }
                group.Add(item with { Dot = item.Dot + 1 });
            }

            var transitions = new Dictionary<char, int>();
            foreach (var group in groups)
                transitions.Add(group.Key, GetState(group.Value));

            states.Add(new LrState(id, Array.AsReadOnly(items), new ReadOnlyDictionary<char, int>(transitions)));
            foreach (LrItem item in items)
            {
                Spend();
                string right = productions[item.RuleId].Right;
                if (item.Dot == right.Length)
                {
                    LrAction action = item.RuleId == 0
                        ? new LrAction(ActionKind.Accept, 0)
                        : new LrAction(ActionKind.Reduce, item.RuleId);
                    AddAction(id, item.Context, action, item);
                    continue;
                }

                char symbol = right[item.Dot];
                if (!grammar.Terminals.Contains(symbol))
                    continue;

                // In LR(0) the shift consumes the next symbol and the symbol selects the transition.
                int target = k == 0 ? LrkResult.ShiftByInput : transitions[symbol];
                var shift = new LrAction(ActionKind.Shift, target);
                foreach (string context in ContextsAfter(item.RuleId, item.Dot, item.Context))
                    AddAction(id, context, shift, item);
            }
        }

        private void AddAction(int stateId, string context, LrAction action, LrItem source)
        {
            Spend();
            var key = (stateId, context);
            if (!sources.TryGetValue(key, out Dictionary<LrAction, LrItem>? cell))
            {
                cell = new Dictionary<LrAction, LrItem>();
                sources.Add(key, cell);
            }

            if (cell.TryAdd(action, source) && ++actionEntries > MaxActionEntries)
                Limit("Таблица действий превысила предел построения.");
        }

        private void Spend()
        {
            if (++operations > MaxOperations)
                Limit("Достигнут предел числа операций при построении автомата.");

            if (operations % 256 != 0)
                return;

            cancellationToken.ThrowIfCancellationRequested();
            if (timer.Elapsed > TimeLimit)
                Limit("Достигнут предел времени построения автомата.");
        }

        private void RetainContexts(int count)
        {
            storedContexts += count;
            if (storedContexts > MaxStoredContexts)
                Limit("Слишком много правых контекстов для выбранного k.");
        }

        private static void CheckContextCount(int count)
        {
            if (count > MaxContextsPerSet)
                Limit("Слишком много вариантов в одном множестве FIRSTₖ.");
        }

        private static void Limit(string message) => throw new ResourceLimitException(message);
    }

    private sealed class ItemSetComparer : IEqualityComparer<LrItem[]>
    {
        public bool Equals(LrItem[]? left, LrItem[]? right) =>
            ReferenceEquals(left, right) || (left is not null && right is not null && left.AsSpan().SequenceEqual(right));

        public int GetHashCode(LrItem[] items)
        {
            var hash = new HashCode();
            foreach (LrItem item in items)
                hash.Add(item);
            return hash.ToHashCode();
        }
    }
}

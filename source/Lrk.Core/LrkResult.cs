namespace Lrk.Core;

public enum ActionKind
{
    Shift,
    Reduce,
    Accept
}

public sealed record LrAction(ActionKind Kind, int Target);

public sealed record LrItem(int RuleId, int Dot, string Context);

public sealed record LrState(int Id, IReadOnlyList<LrItem> Items, IReadOnlyDictionary<char, int> Transitions);

public sealed record ActionCell(int StateId, string Context, IReadOnlyList<LrAction> Actions);

public sealed class ResourceLimitException : Exception
{
    public ResourceLimitException(string message) : base(message) { }
}

public sealed class LrkResult
{
    public const char EndOfInput = '$';
    public const int ShiftByInput = -1;

    public Grammar Grammar { get; }
    public int K { get; }
    public IReadOnlyList<Production> Productions { get; }
    public IReadOnlyList<LrState> States { get; }
    public IReadOnlyList<ActionCell> Cells { get; }
    public IReadOnlyList<ActionCell> Conflicts { get; }

    private readonly Dictionary<(int StateId, string Context), ActionCell> cellByKey;
    private readonly Dictionary<(int StateId, string Context), Dictionary<LrAction, LrItem>> sources;

    internal LrkResult(Grammar grammar, int k, IReadOnlyList<Production> productions,
        IReadOnlyList<LrState> states, IReadOnlyList<ActionCell> cells,
        Dictionary<(int StateId, string Context), Dictionary<LrAction, LrItem>> sources)
    {
        Grammar = grammar;
        K = k;
        Productions = productions;
        States = states;
        Cells = cells;
        Conflicts = Array.AsReadOnly(cells.Where(cell => cell.Actions.Count > 1).ToArray());
        cellByKey = cells.ToDictionary(cell => (cell.StateId, cell.Context));
        this.sources = sources;
    }

    public IReadOnlyList<LrAction> GetActions(int stateId, string context) =>
        cellByKey.TryGetValue((stateId, context), out ActionCell? cell) ? cell.Actions : Array.Empty<LrAction>();

    public LrItem? GetSourceItem(int stateId, string context, LrAction action) =>
        sources.TryGetValue((stateId, context), out var items) && items.TryGetValue(action, out LrItem? item)
            ? item
            : null;

    public string GetContext(string input, int offset)
    {
        if (offset < 0 || offset > input.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        int unread = input.Length - offset;
        return unread >= K ? input.Substring(offset, K) : input[offset..] + EndOfInput;
    }

    public static string DisplayContext(string context) => context.Length == 0 ? "ε" : context;
}

namespace Lrk.Core;

public sealed class GrammarException : Exception
{
    public GrammarException(string field, string message) : base(message) => Field = field;
    public string Field { get; }
}

public sealed record Production(int Id, char Left, string Right)
{
    public override string ToString() => $"{Left} → {(Right.Length == 0 ? "ε" : Right)}";
}

public sealed class Grammar
{
    public string Terminals { get; }
    public string Nonterminals { get; }
    public char Start { get; }
    public IReadOnlyList<Production> Productions { get; }
    public IReadOnlyList<string> Warnings { get; }

    private Grammar(string terminals, string nonterminals, char start, List<Production> productions)
    {
        Terminals = terminals;
        Nonterminals = nonterminals;
        Start = start;
        Productions = productions.AsReadOnly();
        Warnings = Inspect().AsReadOnly();
    }

    public static Grammar Parse(string terminals, string nonterminals, string start, string rules)
    {
        string terminalAlphabet = ParseAlphabet(terminals, nameof(Terminals));
        string nonterminalAlphabet = ParseAlphabet(nonterminals, nameof(Nonterminals));
        if (nonterminalAlphabet.Length == 0)
            throw new GrammarException(nameof(Nonterminals), "Укажите хотя бы один нетерминальный символ.");

        foreach (char symbol in nonterminalAlphabet)
        {
            if (terminalAlphabet.Contains(symbol))
                throw new GrammarException(nameof(Nonterminals), $"Символ «{symbol}» указан в обоих алфавитах. Оставьте его в одном из них.");
        }

        string startText = start.Trim();
        if (startText.Length != 1 || !nonterminalAlphabet.Contains(startText[0]))
            throw new GrammarException(nameof(Start), "Начальный символ должен быть одним знаком из алфавита нетерминалов.");

        if (rules.Length > 32_000)
            throw new GrammarException("Rules", "Текст правил длиннее 32 000 знаков. Сократите грамматику для проверки.");

        var productions = new List<Production>();
        var seen = new HashSet<(char Left, string Right)>();
        string[] lines = rules.ReplaceLineEndings("\n").Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            foreach (string segment in lines[index].Split(';'))
            {
                string line = WithoutWhitespace(segment);
                if (line.Length == 0)
                    continue;

                line = line.Replace("::=", "→").Replace("->", "→");
                int number = index + 1;
                int arrow = line.IndexOf('→');
                if (arrow != 1 || line.LastIndexOf('→') != arrow)
                    throw RuleError(number, "Запишите правило в виде A → c или A -> c.");

                char left = line[0];
                if (!nonterminalAlphabet.Contains(left))
                    throw RuleError(number, $"Символ «{left}» слева от стрелки должен быть объявлен нетерминалом.");

                foreach (string right in line[(arrow + 1)..].Split('|'))
                {
                    if (right.Length == 0)
                        throw RuleError(number, "Укажите ε, если правило порождает пустую цепочку.");

                    string body = right is "ε" or "ϵ" ? "" : right;
                    foreach (char symbol in body)
                    {
                        if (symbol is 'ε' or 'ϵ')
                            throw RuleError(number, "Пустая цепочка ε записывается отдельно, без других символов.");
                        if (!terminalAlphabet.Contains(symbol) && !nonterminalAlphabet.Contains(symbol))
                            throw RuleError(number, $"Символ «{symbol}» из правила {left} → {right} отсутствует в обоих алфавитах.");
                    }

                    if (seen.Add((left, body)))
                        productions.Add(new Production(productions.Count + 1, left, body));
                }
            }
        }

        if (!productions.Any(production => production.Left == startText[0]))
            throw new GrammarException("Rules", $"Укажите хотя бы одно правило для начального символа «{startText[0]}».");

        return new Grammar(terminalAlphabet, nonterminalAlphabet, startText[0], productions);
    }

    private List<string> Inspect()
    {
        var reachable = new HashSet<char> { Start };
        var productive = new HashSet<char>();
        bool changed;
        do
        {
            changed = false;
            foreach (Production production in Productions)
            {
                if (reachable.Contains(production.Left))
                {
                    foreach (char symbol in production.Right.Where(Nonterminals.Contains))
                        changed |= reachable.Add(symbol);
                }

                if (!productive.Contains(production.Left) &&
                    production.Right.All(symbol => Terminals.Contains(symbol) || productive.Contains(symbol)))
                {
                    changed |= productive.Add(production.Left);
                }
            }
        }
        while (changed);

        var warnings = new List<string>();
        char[] unreachable = Nonterminals.Where(symbol => !reachable.Contains(symbol)).ToArray();
        char[] unproductive = Nonterminals.Where(symbol => !productive.Contains(symbol)).ToArray();
        if (unreachable.Length > 0)
            warnings.Add($"Недостижимые нетерминалы: {string.Join(", ", unreachable)}. Эти символы нельзя получить выводом из {Start}.");
        if (unproductive.Length > 0)
            warnings.Add($"Непродуктивные нетерминалы: {string.Join(", ", unproductive)}. Из них нельзя вывести цепочку из терминалов или ε.");
        return warnings;
    }

    private static string ParseAlphabet(string value, string field)
    {
        string symbols = WithoutWhitespace(value);
        foreach (char symbol in symbols)
        {
            if (char.IsControl(symbol) || char.IsSurrogate(symbol) || symbol is '$' or '⊢' or 'ε' or 'ϵ' or '→' or '|' or ';' or '·')
                throw new GrammarException(field, $"Символ «{symbol}» нельзя использовать в алфавите. Выберите другой одиночный знак.");
        }

        return new string(symbols.Distinct().ToArray());
    }

    private static string WithoutWhitespace(string value) =>
        new(value.Where(symbol => !char.IsWhiteSpace(symbol)).ToArray());

    private static GrammarException RuleError(int line, string message) => new("Rules", $"Строка {line}. {message}");
}

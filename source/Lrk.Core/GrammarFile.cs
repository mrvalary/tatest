using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Lrk.Core;

public static partial class Workflow
{
    private const int MaxGrammarFileSize = 1_048_576;

    private static readonly JsonSerializerOptions GrammarJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed record GrammarFile(int Version, GrammarInput Grammar);

    public static void SaveGrammar(string path, GrammarInput input)
    {
        var json = JsonSerializer.Serialize(new GrammarFile(1, input), GrammarJson);
        if (Encoding.UTF8.GetByteCount(json) > MaxGrammarFileSize)
            throw new IOException("Размер сохраняемой грамматики превышает 1 МБ.");
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    public static GrammarInput OpenGrammar(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaxGrammarFileSize)
            throw new InvalidDataException("Файл грамматики превышает 1 МБ.");

        GrammarFile? document;
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            document = JsonSerializer.Deserialize<GrammarFile>(reader.ReadToEnd(), GrammarJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Не удалось прочитать грамматику. Ожидается файл .cfg этого приложения.", ex);
        }

        if (document is not { Version: 1, Grammar: var input } ||
            input is not { Terminals: not null, Nonterminals: not null, Start: not null, Rules: not null }
            || input.K is < 0 or > 32)
            throw new InvalidDataException("В файле .cfg неизвестная версия или отсутствуют поля грамматики.");

        return input;
    }

    public static void ExportProtocol(string path, LrkResult result, TraceResult? trace = null)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        WriteProtocol(writer, result, trace);
    }

    public static void WriteProtocol(TextWriter writer, LrkResult result, TraceResult? trace = null)
    {
        var grammar = result.Grammar;
        writer.WriteLine($"Проверка КС-грамматики на LR({result.K})");
        writer.WriteLine($"Vₜ = {{ {string.Join(", ", grammar.Terminals.AsEnumerable())} }}");
        writer.WriteLine($"Vₙ = {{ {string.Join(", ", grammar.Nonterminals.AsEnumerable())} }}");
        writer.WriteLine($"S = {grammar.Start}");
        writer.WriteLine("Правила (0 — добавленное правило)");
        foreach (var production in result.Productions)
            writer.WriteLine($"  {production.Id}. {production}");
        writer.WriteLine("$ — конец входа. ε — пустая цепочка или пустой правый контекст.");
        if (result.K == 0)
            writer.WriteLine("Строгое соглашение LR(0): сдвиг, свёртку и допуск сравниваем при одном контексте ε.");
        foreach (var warning in grammar.Warnings)
            writer.WriteLine($"Предупреждение. {warning}");

        writer.WriteLine();
        writer.WriteLine(result.Conflicts.Count == 0
            ? $"Грамматика является LR({result.K})."
            : $"Грамматика не является LR({result.K}). Конфликтов: {result.Conflicts.Count}.");
        writer.WriteLine($"Состояний: {result.States.Count}. Заполненных ячеек ACTION: {result.Cells.Count}.");
        foreach (var conflict in result.Conflicts)
        {
            writer.WriteLine();
            writer.WriteLine(ExplainConflict(result, conflict));
        }

        writer.WriteLine();
        writer.WriteLine("Ситуации и переходы");
        foreach (var state in result.States)
        {
            writer.WriteLine($"I{state.Id}");
            foreach (var item in state.Items)
                writer.WriteLine($"  {FormatItem(result, item)}");
            foreach (var edge in state.Transitions)
                writer.WriteLine($"  По «{edge.Key}» → I{edge.Value}");
        }

        writer.WriteLine();
        writer.WriteLine("Таблица ACTION");
        foreach (var cell in result.Cells)
        {
            var actions = string.Join(" / ", cell.Actions.Select(action => ExplainAction(result, action)));
            writer.WriteLine($"  I{cell.StateId} · {FormatContext(cell.Context)} · {actions}");
        }

        if (trace is null)
            return;

        writer.WriteLine();
        writer.WriteLine("Разбор цепочки");
        if (trace.Steps.Count > 0)
            writer.WriteLine($"Цепочка: {FormatContext(trace.Steps[0].Remaining)}");
        writer.WriteLine(trace.Message);
        foreach (var step in trace.Steps)
        {
            writer.WriteLine($"{step.Number}. I{step.StateId} · {step.Action}");
            writer.WriteLine($"   Состояния: {step.States}. Магазин: {FormatContext(step.Stack)}.");
            writer.WriteLine($"   Прочитано: {FormatContext(step.Consumed)}. Осталось: {FormatContext(step.Remaining)}. "
                + $"Правый контекст: {FormatContext(step.Context)}.");
            writer.WriteLine($"   {step.Explanation}");
        }
    }
}

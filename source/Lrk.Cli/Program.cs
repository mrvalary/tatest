using System.Globalization;
using Lrk.Core;

if (args.Length == 0 || args is ["--help"])
{
    Console.WriteLine("""
        Проверка КС-грамматики на LR(k)
          Lrk.Cli check <файл.cfg> [k]
          Lrk.Cli trace <файл.cfg> <цепочка|ε> [k]

        Вывод можно перенаправить в TXT. Коды завершения:
          0 — грамматика LR(k), цепочка принята
          1 — есть конфликт или цепочка отклонена
          2 — ошибка ввода или вычисление не завершено
        """);
    return 0;
}

var traceRequested = args is ["trace", _, ..];
var expectedLength = traceRequested ? 3 : 2;
if (args[0] is not ("check" or "trace") || args.Length < expectedLength || args.Length > expectedLength + 1)
{
    Console.Error.WriteLine("Недопустимые аргументы. Посмотрите Lrk.Cli --help.");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    var grammar = Workflow.OpenGrammar(args[1]);
    if (args.Length == expectedLength + 1)
    {
        if (!int.TryParse(args[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var k) || k > 32)
        {
            Console.Error.WriteLine("Введите целое k от 0 до 32.");
            return 2;
        }
        grammar = grammar with { K = k };
    }

    var result = Workflow.Check(grammar, cancellation.Token);
    var trace = traceRequested ? Workflow.Trace(result, args[2], cancellation.Token) : null;
    Workflow.WriteProtocol(Console.Out, result, trace);
    if (trace?.Status == TraceStatus.Limit)
        return 2;
    return result.Conflicts.Count > 0 || trace?.Status == TraceStatus.Rejected ? 1 : 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Вычисление отменено.");
    return 2;
}
catch (Exception ex) when (ex is GrammarException or ResourceLimitException or IOException or InvalidDataException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

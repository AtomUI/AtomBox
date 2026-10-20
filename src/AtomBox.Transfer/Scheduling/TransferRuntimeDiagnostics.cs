using AtomBox.Core.ValueObjects;
using AtomUI.City.Core.Diagnostics;

namespace AtomBox.Transfer.Scheduling;

public enum TransferRuntimeDiagnosticSeverity
{
    Information = 0,
    Warning = 1,
    Error = 2,
}

public sealed record TransferRuntimeDiagnostic(
    string Code,
    string Message,
    TransferRuntimeDiagnosticSeverity Severity,
    Exception? Exception = null,
    TransferTaskId? TaskId = null);

public interface ITransferRuntimeDiagnostics
{
    void Write(TransferRuntimeDiagnostic diagnostic);
}

internal sealed class NullTransferRuntimeDiagnostics : ITransferRuntimeDiagnostics
{
    public static NullTransferRuntimeDiagnostics Instance { get; } = new();

    public void Write(TransferRuntimeDiagnostic diagnostic)
    {
    }
}

internal sealed class CityTransferRuntimeDiagnostics(IHostDiagnostics? diagnostics)
    : ITransferRuntimeDiagnostics
{
    public void Write(TransferRuntimeDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (diagnostics is null)
        {
            return;
        }

        var context = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["component"] = "AtomBox.Transfer",
        };
        if (diagnostic.TaskId is { } taskId)
        {
            context["taskId"] = taskId.ToString();
        }

        if (diagnostic.Exception is { } exception)
        {
            context["exceptionType"] = exception.GetType().FullName;
        }

        diagnostics.Write(new HostDiagnosticRecord(
            diagnostic.Code,
            diagnostic.Exception is null
                ? diagnostic.Message
                : $"{diagnostic.Message}{Environment.NewLine}{diagnostic.Exception}",
            MapSeverity(diagnostic.Severity))
        {
            ScopeId = diagnostic.TaskId?.ToString(),
            Context = context,
        });
    }

    private static HostDiagnosticSeverity MapSeverity(
        TransferRuntimeDiagnosticSeverity severity) => severity switch
        {
            TransferRuntimeDiagnosticSeverity.Information => HostDiagnosticSeverity.Info,
            TransferRuntimeDiagnosticSeverity.Warning => HostDiagnosticSeverity.Warning,
            TransferRuntimeDiagnosticSeverity.Error => HostDiagnosticSeverity.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null),
        };
}

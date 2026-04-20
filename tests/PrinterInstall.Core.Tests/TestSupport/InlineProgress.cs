namespace PrinterInstall.Core.Tests.TestSupport;

/// <summary>
/// <see cref="System.Progress{T}"/> may post to a <see cref="System.Threading.SynchronizationContext"/>, so
/// assertions can run before progress callbacks. Use this in tests when events must be recorded synchronously.
/// </summary>
internal sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _onReport;

    public InlineProgress(Action<T> onReport) => _onReport = onReport;

    public void Report(T value) => _onReport(value);
}

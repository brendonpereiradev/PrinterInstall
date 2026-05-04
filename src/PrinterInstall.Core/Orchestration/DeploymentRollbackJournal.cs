namespace PrinterInstall.Core.Orchestration;

public sealed class DeploymentRollbackJournal
{
    private static readonly ComputerPortComparer KeyComparer = new();

    private readonly List<DeploymentRollbackQueueEntry> _queues = new();
    private readonly HashSet<(string Computer, string PortName)> _portOnly = new(KeyComparer);

    public IReadOnlyList<DeploymentRollbackQueueEntry> QueueEntries => _queues;
    public IReadOnlyCollection<(string Computer, string PortName)> PortOnlyEntries => _portOnly;

    public bool HasRollbackWork => _queues.Count > 0 || _portOnly.Count > 0;

    public void RecordPortCreated(string computerName, string portName)
    {
        var c = computerName.Trim();
        var p = portName.Trim();
        if (c.Length == 0 || p.Length == 0)
            return;
        _portOnly.Add((c, p));
    }

    public void RecordQueueCreated(string computerName, string printerName, string portName)
    {
        var c = computerName.Trim();
        var q = printerName.Trim();
        var p = portName.Trim();
        if (c.Length == 0 || q.Length == 0 || p.Length == 0)
            return;

        _portOnly.Remove((c, p));
        _queues.Add(new DeploymentRollbackQueueEntry(c, q, p));
    }

    private sealed class ComputerPortComparer : IEqualityComparer<(string Computer, string PortName)>
    {
        public bool Equals((string Computer, string PortName) x, (string Computer, string PortName) y) =>
            string.Equals(x.Computer, y.Computer, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.PortName, y.PortName, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Computer, string PortName) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Computer),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PortName));
    }
}

public sealed record DeploymentRollbackQueueEntry(string ComputerName, string PrinterName, string PortName);

using PrinterInstall.Core.Orchestration;

namespace PrinterInstall.Core.Tests.Orchestration;

public class DeploymentRollbackJournalTests
{
    [Fact]
    public void RecordQueueCreated_RemovesMatchingPortOnlyEntry()
    {
        var j = new DeploymentRollbackJournal();
        j.RecordPortCreated("pc1", "10.0.0.1");
        Assert.True(j.HasRollbackWork);
        j.RecordQueueCreated("pc1", "Q1", "10.0.0.1");
        Assert.Single(j.QueueEntries);
        Assert.Empty(j.PortOnlyEntries);
    }

    [Fact]
    public void PortOnly_WithoutQueue_RemainsForRollback()
    {
        var j = new DeploymentRollbackJournal();
        j.RecordPortCreated("pc1", "10.0.0.2");
        Assert.Single(j.PortOnlyEntries);
        Assert.Contains(j.PortOnlyEntries, t => t.Item1 == "pc1" && t.Item2 == "10.0.0.2");
    }

    [Fact]
    public void DuplicatePortOnly_SameComputerAndPort_IsIdempotent()
    {
        var j = new DeploymentRollbackJournal();
        j.RecordPortCreated("pc1", "P");
        j.RecordPortCreated("pc1", "P");
        Assert.Single(j.PortOnlyEntries);
    }

    [Fact]
    public void AbandonQueue_RemovesQueueAndPortEntries()
    {
        var j = new DeploymentRollbackJournal();
        j.RecordPortCreated("pc1", "10.0.0.1");
        j.RecordQueueCreated("pc1", "Q1", "10.0.0.1");
        Assert.Single(j.QueueEntries);

        j.AbandonQueue("pc1", "Q1", "10.0.0.1");
        Assert.Empty(j.QueueEntries);
        Assert.Empty(j.PortOnlyEntries);
        Assert.False(j.HasRollbackWork);
    }

    [Fact]
    public void ConcurrentOperations_MultipleThreads_ThreadSafeAndConsistent()
    {
        var j = new DeploymentRollbackJournal();
        const int iterations = 100;

        Parallel.For(0, iterations, i =>
        {
            var pc = $"pc_{i}";
            var port = $"port_{i}";
            var queue = $"queue_{i}";

            j.RecordPortCreated(pc, port);
            j.RecordQueueCreated(pc, queue, port);
            _ = j.HasRollbackWork;
            _ = j.QueueEntries.Count;
            _ = j.PortOnlyEntries.Count;
        });

        Assert.Equal(iterations, j.QueueEntries.Count);
        Assert.Empty(j.PortOnlyEntries);
    }
}

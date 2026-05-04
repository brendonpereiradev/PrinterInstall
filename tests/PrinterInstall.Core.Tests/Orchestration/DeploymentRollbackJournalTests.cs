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
}

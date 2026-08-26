using PrinterInstall.App.ViewModels;

namespace PrinterInstall.App.Tests.ViewModels;

/// <summary>
/// Testes unitários para <see cref="SelectableQueueRow"/>.
/// </summary>
public class SelectableQueueRowTests
{
    [Fact]
    public void InitialState_HasExpectedDefaults()
    {
        // Arrange & Act
        var sut = new SelectableQueueRow();

        // Assert
        Assert.Equal(string.Empty, sut.Name);
        Assert.Null(sut.PortName);
        Assert.False(sut.IsSelected);
        Assert.Equal(string.Empty, sut.NewName);
        Assert.True(sut.IsRenameEditable);
    }

    [Fact]
    public void SettingIsSelectedTrue_ClearsNewNameAndSetsIsRenameEditableFalse()
    {
        // Arrange
        var sut = new SelectableQueueRow
        {
            Name = "OldPrinter",
            NewName = "RenamedPrinter"
        };
        Assert.True(sut.IsRenameEditable);

        // Act
        sut.IsSelected = true;

        // Assert
        Assert.True(sut.IsSelected);
        Assert.Equal(string.Empty, sut.NewName);
        Assert.False(sut.IsRenameEditable);
    }

    [Fact]
    public void SettingIsSelectedFalse_RestoresIsRenameEditableTrue()
    {
        // Arrange
        var sut = new SelectableQueueRow
        {
            Name = "OldPrinter",
            IsSelected = true
        };
        Assert.False(sut.IsRenameEditable);

        // Act
        sut.IsSelected = false;

        // Assert
        Assert.True(sut.IsRenameEditable);
    }

    [Fact]
    public void SettingNewName_WhenDifferentFromName_UnselectsRow()
    {
        // Arrange
        var sut = new SelectableQueueRow
        {
            Name = "OriginalName",
            IsSelected = true
        };
        Assert.True(sut.IsSelected);

        // Act
        sut.NewName = "NewDifferentName";

        // Assert
        Assert.False(sut.IsSelected);
        Assert.True(sut.IsRenameEditable);
    }

    [Fact]
    public void SettingNewName_WhenSameAsNameCaseInsensitive_DoesNotUnselectRow()
    {
        // Arrange
        var sut = new SelectableQueueRow
        {
            Name = "PrinterQueue1",
            IsSelected = true
        };

        // Act
        sut.NewName = "printerqueue1";

        // Assert
        Assert.True(sut.IsSelected);
    }

    [Fact]
    public void SettingNewName_WhenEmptyOrWhiteSpace_DoesNotUnselectRow()
    {
        // Arrange
        var sut = new SelectableQueueRow
        {
            Name = "PrinterQueue1",
            IsSelected = true
        };

        // Act
        sut.NewName = "   ";

        // Assert
        Assert.True(sut.IsSelected);
    }
}

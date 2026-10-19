using Jornal.Core.Orders;
using Jornal.Desktop.ViewModels;
using Xunit;

namespace Jornal.Tests;

public sealed class DeliveryNumberTests
{
    [Fact]
    public async Task UniformNumberIsPrefilledAndUnchangedSaveIsDisabled()
    {
        var writes = 0;
        var form = new DeliveryNumberViewModel([ResultOutputTests.Order() with { DeliveryNumber = "БЛЦБ-123" }], _ => { writes++; return Task.FromResult(true); });
        Assert.Equal("БЛЦБ-123", form.Number);
        Assert.False(form.HasDifferentNumbers);
        Assert.False(form.SaveCommand.CanExecute(null));
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, writes);
        form.Number = new string('1', 255);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved);
        Assert.Equal(1, writes);
        Assert.False(form.SaveCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvalidNumberDoesNotWriteAndCanBeCorrected(int scenario)
    {
        var writes = 0;
        var form = new DeliveryNumberViewModel([ResultOutputTests.Order()], _ => { writes++; return Task.FromResult(true); });
        form.Number = scenario switch { 0 => new string('1', 256), 1 => "123\n456", _ => "   " };
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.HasError);
        Assert.Equal(0, writes);
        form.Number = "БЛЦБ-'Б'-123";
        Assert.False(form.HasError);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, writes);
        Assert.True(form.IsSaved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearOnlyWritesWhenAtLeastOneSelectedNumberChanges(bool mixed)
    {
        var writes = 0;
        var row = ResultOutputTests.Order() with { DeliveryNumber = "" };
        var form = new DeliveryNumberViewModel([row, row with { Id = row.Id + 1, DeliveryNumber = mixed ? "123" : "" }],
            _ => { writes++; return Task.FromResult(true); });
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, writes);
        form.ClearNumberCommand.Execute(null);
        Assert.Equal(mixed, form.SaveCommand.CanExecute(null));
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal(mixed ? 1 : 0, writes);
    }
}

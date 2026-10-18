using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Jornal.Core.Orders;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class MainWindowTests
{
    [AvaloniaFact]
    public void SearchBindingFiltersTheDisplayedRows()
    {
        var viewModel = new MainWindowViewModel(new DemoOrderStore());
        var window = new MainWindow { DataContext = viewModel };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            var search = window.FindControl<TextBox>("SearchBox")!;
            Assert.Equal(3, grid.ItemsSource.Cast<OrderRowViewModel>().Count());

            search.Text = "5083h111";
            ((Jornal.Desktop.Controls.JournalSearchBox)search).SubmitInput();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("5083H111", Assert.Single(grid.ItemsSource.Cast<OrderRowViewModel>()).MaterialName);
            Assert.Equal("Заявок: 1 из 3", viewModel.CountText);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ReadFailureKeepsVisibleRowsAndOffersRetry()
    {
        var reader = new FailingReader();
        var viewModel = new MainWindowViewModel(reader);
        await viewModel.ReloadCommand.ExecuteAsync(null);
        Assert.Equal(3, viewModel.Orders.Count);

        reader.ShouldFail = true;
        await viewModel.ReloadCommand.ExecuteAsync(null);

        Assert.Equal(3, viewModel.Orders.Count);
        Assert.True(viewModel.HasError);
        Assert.True(viewModel.ReloadCommand.CanExecute(null));

        reader.ShouldFail = false;
        await viewModel.ReloadCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasError);
    }

    private sealed class FailingReader : IOrderReader
    {
        public bool ShouldFail { get; set; }

        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) =>
            ShouldFail ? throw new IOException("Simulated network failure") : new DemoOrderStore().ReadAsync(cancellationToken);
    }
}

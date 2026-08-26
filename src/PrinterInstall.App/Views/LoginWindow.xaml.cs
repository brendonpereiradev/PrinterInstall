using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PrinterInstall.App.ViewModels;

namespace PrinterInstall.App.Views;

public partial class LoginWindow
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel, IServiceProvider serviceProvider)
    {
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        DataContext = _viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.LoadRememberedUser();
        PasswordBox.Focus();
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.Password = PasswordBox.Password;
    }

    private async void SignIn_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _viewModel.TryLoginAsync().ConfigureAwait(true);
            if (!result.Success)
                return;

            var main = _serviceProvider.GetRequiredService<MainWindow>();
            Application.Current.MainWindow = main;
            main.Show();
            Close();
        }
        catch (Exception ex)
        {
            // Trata exceções não esperadas exibindo na interface sem derrubar o processo WPF.
            _viewModel.ErrorMessage = ex.Message;
        }
    }
}

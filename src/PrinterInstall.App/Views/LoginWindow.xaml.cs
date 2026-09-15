using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using PrinterInstall.App.ViewModels;

namespace PrinterInstall.App.Views;

public partial class LoginWindow
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LoginViewModel _viewModel;
    private bool _isSyncingPassword;

    public LoginWindow(LoginViewModel viewModel, IServiceProvider serviceProvider)
    {
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        DataContext = _viewModel;
        InitializeComponent();

        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.LoadRememberedUser();
        if (!string.IsNullOrWhiteSpace(_viewModel.UserName))
        {
            PasswordBox.Focus();
        }
        else
        {
            UserNameTextBox.Focus();
        }
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.IsPasswordRevealed))
        {
            if (_viewModel.IsPasswordRevealed)
            {
                RevealedPasswordTextBox.Focus();
                RevealedPasswordTextBox.CaretIndex = RevealedPasswordTextBox.Text.Length;
            }
            else
            {
                PasswordBox.Focus();
            }
        }
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncingPassword)
            return;

        try
        {
            _isSyncingPassword = true;
            _viewModel.Password = PasswordBox.Password;
            if (RevealedPasswordTextBox.Text != PasswordBox.Password)
            {
                RevealedPasswordTextBox.Text = PasswordBox.Password;
            }
        }
        finally
        {
            _isSyncingPassword = false;
        }
    }

    private void RevealedPasswordTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingPassword)
            return;

        try
        {
            _isSyncingPassword = true;
            _viewModel.Password = RevealedPasswordTextBox.Text;
            if (PasswordBox.Password != RevealedPasswordTextBox.Text)
            {
                PasswordBox.Password = RevealedPasswordTextBox.Text;
            }
        }
        finally
        {
            _isSyncingPassword = false;
        }
    }

    private async void SignIn_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsAuthenticating)
            return;

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

    private void Settings_OnClick(object sender, RoutedEventArgs e)
    {
        var settingsWin = _serviceProvider.GetRequiredService<SettingsWindow>();
        settingsWin.Owner = this;
        settingsWin.ShowDialog();
    }
}

using System;
using System.Windows;
using PrinterInstall.App.ViewModels;

namespace PrinterInstall.App.Views;

public partial class MainWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureWindowFitsScreen();
    }

    /// <summary>
    /// Garante que a janela caiba completamente dentro da área de trabalho do monitor (WorkArea),
    /// evitando que a barra de título ou navbar fiquem projetadas para fora da tela em notebooks.
    /// </summary>
    private void EnsureWindowFitsScreen()
    {
        var workArea = SystemParameters.WorkArea;

        // Se a janela for maior que a área útil do monitor, redimensiona proporcionalmente
        if (Height > workArea.Height * 0.96)
        {
            Height = Math.Max(MinHeight, workArea.Height * 0.94);
        }

        if (Width > workArea.Width * 0.96)
        {
            Width = Math.Max(MinWidth, workArea.Width * 0.94);
        }

        // Garante que o topo e a esquerda nunca fiquem fora dos limites da tela visível
        if (Top < workArea.Top)
        {
            Top = workArea.Top + 8;
        }

        if (Left < workArea.Left)
        {
            Left = workArea.Left + 8;
        }
    }
}

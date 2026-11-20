using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Atoll.Core.Contracts;
using Atoll.Core.Models;
using Atoll.Core.ViewModels;
using Atoll.UI.Settings;

namespace Atoll.UI;

public partial class MainWindow : Window
{
    private ShellViewModel Shell => DataContext as ShellViewModel
        ?? throw new InvalidOperationException("DataContext is not ShellViewModel");

    public MainWindow()
    {
        InitializeComponent();
    }

    private ISettingsService? Settings =>
        ServiceLocator.Services?.GetService<ISettingsService>();

    private void OnTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && Enum.TryParse<NotchView>(btn.Content?.ToString(), out var view))
            Shell.SwitchTab(view);
    }

    private void OnOpenAll(object sender, RoutedEventArgs e)
    {
        foreach (var vm in Shell.Notches)
            vm.State = NotchState.Open;
    }

    private void OnCloseAll(object sender, RoutedEventArgs e)
    {
        Shell.CloseAllNotches();
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        var settings = Settings;
        if (settings is null) return;
        var settingsWindow = new SettingsWindow(settings);
        settingsWindow.Owner = this;
        settingsWindow.Show();
    }
}

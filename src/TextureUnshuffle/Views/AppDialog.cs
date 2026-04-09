using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace TextureUnshuffle.Views;

/// <summary>Lightweight modal dialog without third-party packages.</summary>
internal static class AppDialog
{
    public static Task ShowErrorAsync(Window owner, string title, string message)
        => ShowAsync(owner, title, message, isConfirm: false);

    public static Task<bool> ConfirmAsync(Window owner, string title, string message)
        => ShowAsync(owner, title, message, isConfirm: true);

    private static async Task<bool> ShowAsync(Window owner, string title, string message, bool isConfirm)
    {
        var tcs = new TaskCompletionSource<bool>();

        var okButton = new Button
        {
            Content = "ОК",
            Width = 90,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(0, 4)
        };

        var cancelButton = new Button
        {
            Content = "Отмена",
            Width = 90,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(0, 4),
            IsVisible = isConfirm
        };

        var buttonRow = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 12,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttonRow.Children.Add(okButton);
        buttonRow.Children.Add(cancelButton);

        var content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 0
        };
        content.Children.Add(new SelectableTextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480,
            Foreground = new SolidColorBrush(Color.Parse("#e0e0f0"))
        });
        content.Children.Add(buttonRow);

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            MinWidth = 280,
            Background = new SolidColorBrush(Color.Parse("#1a1a2e")),
            Content = content
        };

        okButton.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        cancelButton.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(false);

        await dialog.ShowDialog(owner);
        return await tcs.Task;
    }
}

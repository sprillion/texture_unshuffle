using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using System.Linq;
using System.Threading.Tasks;
using TextureUnshuffle.Services;
using TextureUnshuffle.ViewModels;

namespace TextureUnshuffle.Views;

public partial class MainWindow : Window, IDialogService
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(this);

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnFileDrop);
    }

#pragma warning disable CS0618 // DataTransfer API still in transition
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
            DropOverlay.IsVisible = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
    }

    private void OnFileDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        var path = e.Data.GetFiles()?.FirstOrDefault()?.Path.LocalPath;
        if (path != null && DataContext is MainWindowViewModel vm)
            vm.LoadFile(path);
    }
#pragma warning restore CS0618

    public async Task<string?> OpenTextureFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Открыть текстуру",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Изображения") { Patterns = ["*.png", "*.bmp", "*.tga", "*.jpg", "*.jpeg"] },
                new FilePickerFileType("Все файлы") { Patterns = ["*.*"] }
            ]
        });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> SaveFileAsync(string? suggestedFileName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить текстуру",
            SuggestedFileName = suggestedFileName ?? "texture.png",
            FileTypeChoices =
            [
                new FilePickerFileType("PNG") { Patterns = ["*.png"] }
            ]
        });
        return file?.Path.LocalPath;
    }
}

using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace TextureUnshuffle.ViewModels;

public partial class TileItemViewModel : ObservableObject
{
    [ObservableProperty] private AvaloniaBitmap _image;
    [ObservableProperty] private bool _isSelected;

    public int Position { get; }
    public ICommand SelectCommand { get; }

    public TileItemViewModel(AvaloniaBitmap image, int position, ICommand selectCommand)
    {
        _image = image;
        Position = position;
        SelectCommand = selectCommand;
    }
}

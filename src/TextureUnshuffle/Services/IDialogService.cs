using System.Threading.Tasks;

namespace TextureUnshuffle.Services;

public interface IDialogService
{
    Task<string?> OpenTextureFileAsync();
    Task<string?> OpenModelFileAsync();
    Task<string?> SaveFileAsync(string? suggestedFileName);
    Task ShowErrorAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message);
}

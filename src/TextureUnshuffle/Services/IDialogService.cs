using System.Threading.Tasks;

namespace TextureUnshuffle.Services;

public interface IDialogService
{
    Task<string?> OpenTextureFileAsync();
    Task<string?> SaveFileAsync(string? suggestedFileName);
}

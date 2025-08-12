
using Smotrel.ViewModels;

namespace Smotrel.Services
{
    public interface IVideoLibraryService
    {
        Task<FolderNodeViewModel> LoadLibraryAsync(string rootPath);
    }
}

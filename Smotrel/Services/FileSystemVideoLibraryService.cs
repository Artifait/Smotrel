
using Smotrel.Models;
using Smotrel.ViewModels;
using System.IO;

namespace Smotrel.Services
{
    public class FileSystemVideoLibraryService : IVideoLibraryService
    {
        public async Task<FolderNodeViewModel> LoadLibraryAsync(string rootPath)
        {
            return await Task.Run(() => ScanFolder(rootPath));
        }

        private FolderNodeViewModel ScanFolder(string path)
        {
            var folderVm = new FolderNodeViewModel(Path.GetFileName(path), path);
            foreach (var dir in Directory.GetDirectories(path))
                folderVm.Children.Add(ScanFolder(dir));
            foreach (var file in Directory.GetFiles(path)
                                      .Where(f => IsVideoFile(f)))
                folderVm.Children.Add(new VideoNodeViewModel(new VideoItem
                {
                    Title = Path.GetFileNameWithoutExtension(file),
                    FilePath = file
                }));
            return folderVm;
        }

        private bool IsVideoFile(string f) =>
            new[] { ".mp4", ".mkv", ".avi" }
            .Contains(Path.GetExtension(f).ToLower());
    }
}

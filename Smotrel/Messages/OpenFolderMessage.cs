
namespace Smotrel.Messages
{
    public class OpenFolderMessage
    {
        public string FolderPath { get; }
        public OpenFolderMessage(string folderPath) => FolderPath = folderPath;
    }
}


namespace Smotrel.Messages
{
    public class PlayVideoMessage
    {
        public string FilePath { get; }
        public PlayVideoMessage(string path) => FilePath = path;
    }
}

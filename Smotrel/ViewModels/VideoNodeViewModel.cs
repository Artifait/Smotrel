
using CommunityToolkit.Mvvm.Messaging;
using Smotrel.Helpers;
using Smotrel.Messages;
using Smotrel.Models;

using System.Windows.Input;

namespace Smotrel.ViewModels
{
    public class VideoNodeViewModel : BaseViewModel
    {
        public VideoItem Video { get; }
        public ICommand PlayCommand { get; }
        public VideoNodeViewModel(VideoItem video)
        {

            Video = video;
            PlayCommand = new RelayCommand(_ =>
                WeakReferenceMessenger.Default.Send(new PlayVideoMessage(video.FilePath)));
        }
    }
}

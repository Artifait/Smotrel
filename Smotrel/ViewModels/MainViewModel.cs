using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Smotrel.Helpers;
using Smotrel.Messages;
using Smotrel.Models;
using Smotrel.Services;

namespace Smotrel.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private readonly IVideoLibraryService _libraryService;
        private readonly string[] _videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov" };
        private int _currentIndex = -1;

        /// <summary>
        /// Корневая папка — для TreeView
        /// </summary>
        private FolderNodeViewModel _root;
        public FolderNodeViewModel Root
        {
            get => _root;
            private set
            {
                if (_root != value)
                {
                    _root = value;
                    OnPropertyChanged(nameof(Root));
                }
            }
        }

        /// <summary>
        /// Плоский плейлист текущей главы
        /// </summary>
        public ObservableCollection<VideoItem> Playlist { get; } = new();

        private VideoItem _selectedVideo;
        public VideoItem SelectedVideo
        {
            get => _selectedVideo;
            set
            {
                if (_selectedVideo != value)
                {
                    _selectedVideo = value;
                    OnPropertyChanged(nameof(SelectedVideo));
                    OnPropertyChanged(nameof(CurrentVideoPath));

                    if (_selectedVideo != null)
                    {
                        // При изменении выбранного — отправляем сообщение о воспроизведении
                        WeakReferenceMessenger.Default.Send(new PlayVideoMessage(_selectedVideo.FilePath));
                    }
                }
            }
        }

        /// <summary>
        /// Путь для MediaElement.Source
        /// </summary>
        public string CurrentVideoPath => SelectedVideo?.FilePath;

        // Команды
        public ICommand BrowseCommand { get; }
        public ICommand PrevCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand PausePlayCommand { get; }
        public ICommand SpeedUpCommand { get; }
        public ICommand NormalSpeedCommand { get; }
        public ICommand FullscreenCommand { get; }

        public MainViewModel(IVideoLibraryService libraryService)
        {
            _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));

            // Messenger на открытие главы (папки)
            WeakReferenceMessenger.Default.Register<OpenFolderMessage>(this, (_, msg) =>
            {
                LoadPlaylistFromFolder(msg.FolderPath);
            });

            BrowseCommand = new RelayCommand(async _ => await LoadLibraryAsync());
            PrevCommand = new RelayCommand(_ =>
                                                 WeakReferenceMessenger.Default.Send(
                                                   new VideoControlMessage(VideoControlAction.Previous)),
                                               _ => CanMovePrevious());

            PausePlayCommand = new RelayCommand(_ =>
                                     WeakReferenceMessenger.Default.Send(
                                       new VideoControlMessage(VideoControlAction.TogglePlayPause)));

            SpeedUpCommand = new RelayCommand(_ =>
                                     WeakReferenceMessenger.Default.Send(
                                       new VideoControlMessage(VideoControlAction.SpeedUp)));

            NormalSpeedCommand = new RelayCommand(_ =>
                                     WeakReferenceMessenger.Default.Send(
                                       new VideoControlMessage(VideoControlAction.NormalSpeed)));

            FullscreenCommand = new RelayCommand(_ =>
                                     WeakReferenceMessenger.Default.Send(
                                       new VideoControlMessage(VideoControlAction.ToggleFullscreen)));

            NextCommand = new RelayCommand(_ =>
                                     WeakReferenceMessenger.Default.Send(
                                       new VideoControlMessage(VideoControlAction.Next)),
                                   _ => CanMoveNext());
        }

        /// <summary>
        /// Загрузка всей структуры и автозапуск первого видео
        /// </summary>
        private async Task LoadLibraryAsync()
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog() != DialogResult.OK)
                return;

            // 1) Строим дерево для TreeView
            var rootVm = await _libraryService.LoadLibraryAsync(dlg.SelectedPath);
            Root = rootVm;

            // 2) Параллельно заполняем плейлист всем видео из всех папок
            var allVideos = Flatten(rootVm).ToList();
            Playlist.Clear();
            foreach (var vid in allVideos)
                Playlist.Add(vid);

            // 3) Сразу запускаем первое
            if (Playlist.Any())
            {
                _currentIndex = 0;
                SelectedVideo = Playlist[0];
            }

            RaiseNavCommandsCanExecuteChanged();
        }

        /// <summary>
        /// Загрузка плейлиста для конкретной главы (папки)
        /// </summary>
        private void LoadPlaylistFromFolder(string folderPath)
        {
            var videos = Directory.GetFiles(folderPath)
                                  .Where(f => _videoExtensions.Contains(Path.GetExtension(f).ToLower()))
                                  .OrderBy(f => f)
                                  .Select(f => new VideoItem
                                  {
                                      Title = Path.GetFileNameWithoutExtension(f),
                                      FilePath = f
                                  })
                                  .ToList();

            Playlist.Clear();
            foreach (var vid in videos)
                Playlist.Add(vid);

            if (Playlist.Any())
            {
                _currentIndex = 0;
                SelectedVideo = Playlist[0];
            }

            RaiseNavCommandsCanExecuteChanged();
        }

        public void MoveNext()
        {
            if (CanMoveNext())
            {
                _currentIndex++;
                SelectedVideo = Playlist[_currentIndex];
                RaiseNavCommandsCanExecuteChanged();
            }
        }

        public void MovePrevious()
        {
            if (CanMovePrevious())
            {
                _currentIndex--;
                SelectedVideo = Playlist[_currentIndex];
                RaiseNavCommandsCanExecuteChanged();
            }
        }

        private bool CanMoveNext() => _currentIndex < Playlist.Count - 1;
        private bool CanMovePrevious() => _currentIndex > 0;

        private void RaiseNavCommandsCanExecuteChanged()
        {
            (NextCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PrevCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Рекурсивно собирает все видео из дерева папок
        /// </summary>
        private IEnumerable<VideoItem> Flatten(FolderNodeViewModel folder)
        {
            foreach (var child in folder.Children)
            {
                if (child is VideoNodeViewModel v)
                    yield return v.Video;
                else if (child is FolderNodeViewModel f)
                    foreach (var sub in Flatten(f))
                        yield return sub;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Smotrel.Data.Entities;
using Smotrel.Helpers;
using Smotrel.Messages;
using Smotrel.Models;
using Smotrel.Services.Interfaces;

namespace Smotrel.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private readonly ICourseScanner _scanner;
        private readonly ICourseRepository _repository;
        private readonly ICourseMergeService _mergeService;
        private readonly IPlaybackService _playbackService;

        private readonly string[] _videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".webm", ".flv" };

        private int _currentIndex = -1;

        /// <summary>
        /// Путь к корню текущего загруженного курса (null если не загружен)
        /// </summary>
        public string? CourseRootPath { get; private set; }

        /// <summary>
        /// Корневая папка — для TreeView (построена из CourseEntity)
        /// </summary>
        private FolderNodeViewModel? _root;
        public FolderNodeViewModel? Root
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
        /// Плоский плейлист текущей главы/курса
        /// </summary>
        public ObservableCollection<VideoItem> Playlist { get; } = new();

        private VideoItem? _selectedVideo;
        public VideoItem? SelectedVideo
        {
            get => _selectedVideo;
            set
            {
                if (_selectedVideo != value)
                {
                    _selectedVideo = value;
                    OnPropertyChanged(nameof(SelectedVideo));
                    OnPropertyChanged(nameof(CurrentVideoPath));
                    OnPropertyChanged(nameof(CurrentVideoTitle));

                    if (_selectedVideo != null)
                    {
                        // При изменении выбранного — отправляем сообщение о воспроизведении
                        WeakReferenceMessenger.Default.Send(new PlayVideoMessage(_selectedVideo.FilePath));

                        // Асинхронно проверяем, есть ли resume-маркер курса для этой части
                        _ = CheckAndNotifyResumeForSelectedAsync();
                    }
                }
            }
        }

        private async Task CheckAndNotifyResumeForSelectedAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(CourseRootPath) || SelectedVideo == null || string.IsNullOrWhiteSpace(SelectedVideo.PartId))
                    return;

                if (!Guid.TryParse(SelectedVideo.PartId, out var pid)) return;

                var course = await _repository.LoadAsync(CourseRootPath);
                if (course == null) return;

                if (course.LastPlayedPartId.HasValue && course.LastPlayedPartId.Value == pid && course.LastPlayedPositionSeconds > 0)
                {
                    // отправляем сообщение для UI (MainWindow подпишется)
                    WeakReferenceMessenger.Default.Send(new ResumeAvailableMessage(pid, course.LastPlayedPositionSeconds, CourseRootPath));
                }
            }
            catch
            {
                // best-effort — игнорируем ошибки проверки resume
            }
        }


        public int SelectedIndex
        {
            get => _currentIndex;
            set
            {
                if (value < 0 || value >= Playlist.Count)
                    throw new ArgumentOutOfRangeException(nameof(value), "Index is out of range of the playlist.");
                _currentIndex = value;
                SelectedVideo = Playlist[_currentIndex];
            }
        }

        /// <summary>
        /// Путь для MediaElement.Source
        /// </summary>
        public string? CurrentVideoPath => SelectedVideo?.FilePath;

        public string? CurrentVideoTitle => SelectedVideo?.Title;

        // Команды
        public ICommand BrowseCommand { get; }
        public ICommand PrevCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand PausePlayCommand { get; }
        public ICommand SpeedUpCommand { get; }
        public ICommand NormalSpeedCommand { get; }
        public ICommand FullscreenCommand { get; }

        public MainViewModel(
            ICourseScanner scanner,
            ICourseRepository repository,
            ICourseMergeService mergeService,
            IPlaybackService playbackService)
        {
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _mergeService = mergeService ?? throw new ArgumentNullException(nameof(mergeService));
            _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));

            // Messenger на открытие главы (папки) — прежнее поведение (если кто-то шлёт OpenFolderMessage)
            WeakReferenceMessenger.Default.Register<OpenFolderMessage>(this, (_, msg) =>
            {
                // Prefer to open folder inside loaded course: try to find chapter with that path
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
        /// Загрузка всей структуры: сканируем папку, загружаем/сливаем с репозиторием (JSON), строим UI-дерево/плейлист
        /// </summary>
        private async Task LoadLibraryAsync()
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog() != DialogResult.OK)
                return;

            var rootPath = dlg.SelectedPath;

            // 1) Сканируем (получим свежую структуру и fsHash)
            CourseEntity scanned;
            try
            {
                scanned = await _scanner.ScanAsync(rootPath, tryGetDurations: true);
            }
            catch (Exception ex)
            {
                // TODO: показать пользователю понятное сообщение, сейчас пробрасываем
                throw new InvalidOperationException("Scan failed: " + ex.Message, ex);
            }

            // 2) Загружаем существующую запись из репозитория (если есть)
            CourseEntity? existing = null;
            try
            {
                existing = await _repository.LoadAsync(rootPath);
            }
            catch
            {
                existing = null;
            }

            // 3) Решаем, что сохранять (new / keep existing / merge)
            CourseEntity toSave;
            if (existing == null)
            {
                toSave = scanned;
            }
            else
            {
                // если hash совпадает — оставляем existing (с обновлением метаданных)
                if (!string.IsNullOrWhiteSpace(existing.FsHash) &&
                    !string.IsNullOrWhiteSpace(scanned.FsHash) &&
                    string.Equals(existing.FsHash, scanned.FsHash, StringComparison.OrdinalIgnoreCase))
                {
                    existing.LastScannedAt = DateTime.UtcNow;
                    existing.TotalDurationSeconds = scanned.TotalDurationSeconds ?? existing.TotalDurationSeconds;
                    // preserve user fields (LastPlayedPartId и т.п.)
                    toSave = existing;
                }
                else
                {
                    // merge existing + scanned
                    var mergeResult = _mergeService.Merge(existing, scanned);
                    // backup current JSON (если есть)
                    await _repository.BackupAsync(rootPath, "merge-before-save");
                    toSave = mergeResult.MergedCourse;
                }
            }

            // 4) Сохраняем (atomic save в .smotrel/course.json)
            await _repository.SaveAsync(toSave);

            // 5) Построим UI из toSave
            CourseRootPath = toSave.RootPath; // важно — для Resume проверок
            Root = BuildFolderNodeFromCourse(toSave);
            PopulatePlaylistFromCourse(toSave);

            // 6) Выбор видео для автозапуска:
            //    Если есть курс-маркер LastPlayedPartId — выбрать именно ту часть (resume).
            //    Если не найден (файл удалён/переименован), fallback: выбрать первый элемент (если есть).
            if (Playlist.Any())
            {
                bool selected = false;

                if (toSave.LastPlayedPartId.HasValue)
                {
                    var pid = toSave.LastPlayedPartId.Value;
                    var matchIndex = Playlist.ToList().FindIndex(v =>
                        !string.IsNullOrWhiteSpace(v.PartId) &&
                        Guid.TryParse(v.PartId, out var g) &&
                        g == pid);

                    if (matchIndex >= 0)
                    {
                        _currentIndex = matchIndex;
                        SelectedVideo = Playlist[_currentIndex];
                        selected = true;
                    }
                }

                if (!selected)
                {
                    // fallback: pick first element
                    _currentIndex = 0;
                    SelectedVideo = Playlist[0];
                }
            }

            RaiseNavCommandsCanExecuteChanged();
        }



        /// <summary>
        /// Заполнить плейлист всем видео из CourseEntity (в порядке глав -> частей)
        /// </summary>
        private void PopulatePlaylistFromCourse(CourseEntity course)
        {
            Playlist.Clear();
            foreach (var ch in course.Chapters.OrderBy(c => c.Order ?? int.MaxValue).ThenBy(c => c.Title))
            {
                foreach (var p in ch.Parts.OrderBy(p => p.Index ?? int.MaxValue).ThenBy(p => p.FileName))
                {
                    Playlist.Add(new VideoItem
                    {
                        Title = string.IsNullOrWhiteSpace(p.Title) ? p.FileName : p.Title,
                        FilePath = p.Path,
                        PartId = p.Id.ToString(),
                        ChapterId = ch.Id.ToString(),
                        CourseId = course.Id.ToString(),
                        Duration = p.DurationSeconds ?? 0,
                        LastPosition = p.LastPositionSeconds,
                        Watched = p.Watched
                    });
                }
            }
        }

        /// <summary>
        /// Строит FolderNodeViewModel из CourseEntity.Chapters
        /// </summary>
        private FolderNodeViewModel BuildFolderNodeFromCourse(CourseEntity course)
        {
            // Root folder
            var rootVm = new FolderNodeViewModel(string.IsNullOrWhiteSpace(course.Title) ? Path.GetFileName(course.RootPath) : course.Title, course.RootPath);

            // For each chapter: create a folder node with VideoNode children
            foreach (var ch in course.Chapters.OrderBy(c => c.Order ?? int.MaxValue).ThenBy(c => c.Title))
            {
                // compute absolute path of chapter
                var absPath = ch.RelPath == "." ? course.RootPath : Path.Combine(course.RootPath, ch.RelPath);
                var chVm = new FolderNodeViewModel(string.IsNullOrWhiteSpace(ch.Title) ? ch.RelPath : ch.Title, absPath);

                foreach (var p in ch.Parts.OrderBy(p => p.Index ?? int.MaxValue).ThenBy(p => p.FileName))
                {
                    var videoItem = new VideoItem
                    {
                        Title = string.IsNullOrWhiteSpace(p.Title) ? p.FileName : p.Title,
                        FilePath = p.Path,
                        PartId = p.Id.ToString(),
                        ChapterId = ch.Id.ToString(),
                        CourseId = course.Id.ToString(),
                        Duration = p.DurationSeconds ?? 0,
                        LastPosition = p.LastPositionSeconds,
                        Watched = p.Watched
                    };
                    var videoNode = new VideoNodeViewModel(videoItem);
                    chVm.Children.Add(videoNode);
                }

                rootVm.Children.Add(chVm);
            }

            return rootVm;
        }

        /// <summary>
        /// Загрузка плейлиста для конкретной главы (папки) — теперь на базе уже загруженного CourseRootPath/Root
        /// </summary>
        private void LoadPlaylistFromFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return;

            // Если у нас есть CourseRootPath и Root, то пытаемся найти chapter matching folderPath
            // сравниваем normalized absolute paths
            var target = NormalizePath(folderPath);

            // try find chapter node under Root
            FolderNodeViewModel? found = null;
            if (Root != null)
            {
                // search recursively
                found = FindFolderNode(Root, target);
            }

            // build playlist from found chapter or fallback to scanning files in folder
            List<VideoItem> videos = new();

            if (found != null)
            {
                foreach (var child in found.Children)
                {
                    if (child is VideoNodeViewModel v)
                        videos.Add(v.Video);
                }
            }
            else
            {
                // fallback: simple dir scan (legacy)
                if (Directory.Exists(folderPath))
                {
                    var files = Directory.GetFiles(folderPath)
                                         .Where(f => _videoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                         .OrderBy(f => f)
                                         .ToList();
                    foreach (var f in files)
                    {
                        videos.Add(new VideoItem
                        {
                            Title = Path.GetFileNameWithoutExtension(f),
                            FilePath = f
                        });
                    }
                }
            }

            Playlist.Clear();
            foreach (var v in videos) Playlist.Add(v);

            if (Playlist.Any())
            {
                _currentIndex = 0;
                SelectedVideo = Playlist[0];
            }

            RaiseNavCommandsCanExecuteChanged();
        }

        private FolderNodeViewModel? FindFolderNode(FolderNodeViewModel root, string normalizedAbsPath)
        {
            if (NormalizePath(root.FolderPath) == normalizedAbsPath)
                return root;

            foreach (var child in root.Children.OfType<FolderNodeViewModel>())
            {
                var found = FindFolderNode(child, normalizedAbsPath);
                if (found != null) return found;
            }

            return null;
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

        public void SetCurrentIndex(int index)
        {
            if (index < 0 || index >= Playlist.Count)
                throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range of the playlist.");
            _currentIndex = index;
            SelectedVideo = Playlist[_currentIndex];
            RaiseNavCommandsCanExecuteChanged();
        }

        private bool CanMoveNext() => _currentIndex < Playlist.Count - 1;
        private bool CanMovePrevious() => _currentIndex > 0;

        private void RaiseNavCommandsCanExecuteChanged()
        {
            (NextCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PrevCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Рекурсивно собирает все видео из дерева папок (оставил для совместимости)
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

        // утилиты
        private static string NormalizePath(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return string.Empty;
            try
            {
                return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .ToLowerInvariant();
            }
            catch
            {
                return p.Trim().ToLowerInvariant();
            }
        }
    }
}

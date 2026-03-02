using Smotrel.Models;
using Smotrel.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Smotrel.Controls
{
    /// <summary>
    /// Боковая панель навигации по курсу.
    ///
    /// Инициализация (однократно из MainPlayer.OnLoaded):
    ///   Nav.Initialize(course, db);
    ///
    /// При смене видео (из MainPlayer):
    ///   Nav.SetCurrentVideo(video);
    ///
    /// Обновление позиции (из PositionTimer ~250 мс):
    ///   Nav.UpdatePosition(position);
    ///
    /// События:
    ///   VideoRequested  — пользователь кликнул по видео
    ///   SeekRequested   — пользователь кликнул по таймкоду
    ///   TimecodeChanged — список меток текущего видео обновился
    /// </summary>
    public partial class CourseNavigationControl : UserControl
    {
        // ════════════════════════════════════════════════════════════════
        //  СОБЫТИЯ
        // ════════════════════════════════════════════════════════════════

        public event Action<VideoModel>?  VideoRequested;
        public event Action<TimeSpan>?    SeekRequested;
        public event Action<VideoModel>?  TimecodeChanged;

        // ════════════════════════════════════════════════════════════════
        //  СОСТОЯНИЕ
        // ════════════════════════════════════════════════════════════════

        private CourseModel?          _course;
        private SmotrelContext?       _db;

        // ── Навигация ────────────────────────────────────────────────────

        private ChapterCourseModel?   _currentChapter;
        private readonly Stack<ChapterCourseModel> _navStack = new();

        // ── Текущее видео / позиция ───────────────────────────────────────

        private VideoModel?  _currentVideo;
        private TimeSpan     _currentPos;

        // ── Редактирование названия главы (шапка) ────────────────────────

        private bool _isEditingChapterHeader;

        // ── Коллекции VM ─────────────────────────────────────────────────

        private ObservableCollection<ChapterCardVm>  _chapterVms  = [];
        private ObservableCollection<VideoCardVm>    _videoVms    = [];
        private ObservableCollection<TimecodeVm>     _timecodeVms = [];

        // быстрый доступ по Id видео для обновления маркеров
        private readonly Dictionary<int, VideoCardVm> _videoVmById = [];

        // ════════════════════════════════════════════════════════════════
        //  КОНСТРУКТОР
        // ════════════════════════════════════════════════════════════════

        public CourseNavigationControl()
        {
            InitializeComponent();

            IcChapters.ItemsSource  = _chapterVms;
            IcVideos.ItemsSource    = _videoVms;
            IcTimecodes.ItemsSource = _timecodeVms;
        }

        // ════════════════════════════════════════════════════════════════
        //  ПУБЛИЧНЫЙ API
        // ════════════════════════════════════════════════════════════════

        public void Initialize(CourseModel course, SmotrelContext db)
        {
            _course = course;
            _db     = db;
            NavigateTo(course.MainChapter, clearStack: true);
        }

        public void SetCurrentVideo(VideoModel video)
        {
            _currentVideo = video;
            _currentPos   = TimeSpan.Zero;

            // Если видео не в текущей главе — прыгаем к нужной
            if (_currentChapter != null &&
                !_currentChapter.Videos.Any(v => v.Id == video.Id))
            {
                var chain = FindChain(_course!.MainChapter, video);
                if (chain != null)
                {
                    _navStack.Clear();
                    for (int i = 0; i < chain.Count - 1; i++)
                        _navStack.Push(chain[i]);
                    NavigateTo(chain[^1]);
                }
            }

            RefreshVideoMarkers();
            RefreshVideoInfoPanel();
        }

        public void UpdatePosition(TimeSpan pos)
        {
            _currentPos = pos;
            UpdateWatchPercent();
        }

        // ════════════════════════════════════════════════════════════════
        //  НАВИГАЦИЯ
        // ════════════════════════════════════════════════════════════════

        private void NavigateTo(ChapterCourseModel chapter, bool clearStack = false)
        {
            if (clearStack) _navStack.Clear();
            _currentChapter = chapter;

            // Шапка
            TbChapterName.Text = chapter.Title;
            EdChapterName.Text = chapter.Title;
            BtnUp.Visibility   = _navStack.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;

            BuildChapterList(chapter);
            BuildVideoList(chapter);
            RefreshChapterStats();
            UpdateEmptyState();
        }

        private void BuildChapterList(ChapterCourseModel chapter)
        {
            _chapterVms.Clear();
            foreach (var ch in chapter.Chapters)
            {
                var vm = new ChapterCardVm(ch);
                vm.OnNavigate = v =>
                {
                    _navStack.Push(_currentChapter!);
                    NavigateTo(v.Model);
                };
                vm.OnRenamed = v => _ = SaveChapterAsync(v.Model);
                _chapterVms.Add(vm);
            }

            SecChapters.Visibility = _chapterVms.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BuildVideoList(ChapterCourseModel chapter)
        {
            _videoVms.Clear();
            _videoVmById.Clear();

            foreach (var video in chapter.Videos)
            {
                var vm = new VideoCardVm(video);
                vm.OnSelect         = v => VideoRequested?.Invoke(v.Model);
                vm.OnWatchedToggled = v => _ = SaveVideoAsync(v.Model);
                vm.OnRenamed        = v =>
                {
                    _ = SaveVideoAsync(v.Model);
                    // Если переименовали текущее — обновляем панель видео
                    if (_currentVideo?.Id == v.Model.Id)
                        TbVideoTitle.Text = v.Title;
                };
                vm.IsCurrent = _currentVideo?.Id == video.Id;

                _videoVms.Add(vm);
                _videoVmById[video.Id] = vm;
            }

            SecVideos.Visibility = _videoVms.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshVideoMarkers()
        {
            foreach (var vm in _videoVms)
                vm.IsCurrent = _currentVideo?.Id == vm.Model.Id;
        }

        private void UpdateEmptyState()
        {
            bool isEmpty = _chapterVms.Count == 0 && _videoVms.Count == 0;
            TbEmpty.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Поиск цепочки родителей ───────────────────────────────────────

        private static List<ChapterCourseModel>? FindChain(
            ChapterCourseModel root, VideoModel target)
        {
            if (root.Videos.Any(v => v.Id == target.Id))
                return [root];
            foreach (var sub in root.Chapters)
            {
                var chain = FindChain(sub, target);
                if (chain != null) { chain.Insert(0, root); return chain; }
            }
            return null;
        }

        // ════════════════════════════════════════════════════════════════
        //  ПАНЕЛЬ ИНФОРМАЦИИ О ВИДЕО
        // ════════════════════════════════════════════════════════════════

        private void RefreshVideoInfoPanel()
        {
            TbVideoTitle.Text = _currentVideo?.Title ?? "—";
            UpdateWatchPercent();
            RebuildTimecodes();
        }

        private void UpdateWatchPercent()
        {
            if (_currentVideo == null || _currentVideo.Duration == TimeSpan.Zero)
            {
                SetWatchPercent(0, false); return;
            }

            double pct = _currentPos.TotalSeconds
                         / _currentVideo.Duration.TotalSeconds * 100.0;
            pct = Math.Clamp(pct, 0, 100);
            bool green = pct >= 80;
            SetWatchPercent(pct, green);

            // Авто-маркировка
            if (green && _currentVideo is { IsWatched: false })
                _ = MarkWatchedAsync(_currentVideo, true);
        }

        private void SetWatchPercent(double pct, bool green)
        {
            TbWatchPct.Text = $"{(int)pct}%";
            PbWatch.Value   = pct;

            var color = green
                ? Color.FromRgb(0x4C, 0xAF, 0x50)
                : Color.FromRgb(0x99, 0x99, 0x99);
            TbWatchPct.Foreground = new SolidColorBrush(color);

            PbWatch.Foreground = new SolidColorBrush(
                green ? Color.FromRgb(0x4C, 0xAF, 0x50)
                      : Color.FromRgb(0xFF, 0x00, 0x33));
        }

        private void RebuildTimecodes()
        {
            _timecodeVms.Clear();

            if (_currentVideo == null || _currentVideo.Timestamps.Count == 0)
            {
                TbNoTimecodes.Visibility = Visibility.Visible;
                return;
            }

            TbNoTimecodes.Visibility = Visibility.Collapsed;
            foreach (var tc in _currentVideo.Timestamps)
            {
                var vm = new TimecodeVm(tc);
                vm.OnSeek   = v => SeekRequested?.Invoke(v.Position);
                vm.OnDelete = v => _ = DeleteTimecodeAsync(v);
                _timecodeVms.Add(vm);
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  ПРОГРЕСС ГЛАВЫ
        // ════════════════════════════════════════════════════════════════

        private void RefreshChapterStats()
        {
            if (_currentChapter == null) return;

            int total   = CountAll(_currentChapter);
            int watched = CountWatched(_currentChapter);
            int pct     = total > 0 ? (int)((double)watched / total * 100) : 0;

            TbChapterPct.Text   = $"{pct}%";
            TbChapterRatio.Text = $"{watched} / {total} видео завершено";
            PbChapter.Value     = pct;
        }

        private static int CountAll(ChapterCourseModel ch)
            => ch.Videos.Count + ch.Chapters.Sum(CountAll);

        private static int CountWatched(ChapterCourseModel ch)
            => ch.Videos.Count(v => v.IsWatched) + ch.Chapters.Sum(CountWatched);

        // ════════════════════════════════════════════════════════════════
        //  ОБРАБОТЧИКИ СОБЫТИЙ — НАВИГАЦИЯ
        // ════════════════════════════════════════════════════════════════

        private void BtnUp_Click(object sender, RoutedEventArgs e)
        {
            if (_navStack.Count == 0) return;
            NavigateTo(_navStack.Pop());
        }

        // ── Глава: одиночный клик → войти, двойной → переименовать ───────────

        private void ChapterCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not ChapterCardVm vm)
                return;

            if (vm.IsEditing) { e.Handled = true; return; }

            if (e.ClickCount >= 2) { vm.StartEdit(); e.Handled = true; }
            else                     vm.Navigate();
        }

        // ── Видео: одиночный клик → выбрать, двойной → переименовать ─────────

        private void VideoCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not VideoCardVm vm)
                return;

            if (vm.IsEditing) { e.Handled = true; return; }

            // Клик на кнопку IsWatched не должен выбирать видео
            if (e.OriginalSource is TextBlock tb && tb.Parent is Button) return;

            if (e.ClickCount == 1) vm.Select();
        }

        private void VideoTitle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2 && sender is FrameworkElement fe
                && fe.DataContext is VideoCardVm vm)
            {
                vm.StartEdit();
                e.Handled = true;
            }
        }

        private void VideoWatchedBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is VideoCardVm vm)
            {
                vm.ToggleWatched();
                RefreshChapterStats();
                e.Handled = true;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  ОБРАБОТЧИКИ — РЕДАКТИРОВАНИЕ НАЗВАНИЯ ГЛАВЫ (ШАПКА)
        // ════════════════════════════════════════════════════════════════

        private void ChapterName_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2) { BeginChapterHeaderEdit(); e.Handled = true; }
        }

        private void BeginChapterHeaderEdit()
        {
            if (_currentChapter == null) return;
            EdChapterName.Text      = _currentChapter.Title;
            TbChapterName.Visibility = Visibility.Collapsed;
            EdChapterName.Visibility = Visibility.Visible;
            _isEditingChapterHeader  = true;
        }

        private void CommitChapterHeaderEdit()
        {
            if (!_isEditingChapterHeader || _currentChapter == null) return;
            var name = EdChapterName.Text.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                _currentChapter.Title = name;
                TbChapterName.Text    = name;
                _ = SaveChapterAsync(_currentChapter);
            }
            CloseChapterHeaderEdit();
        }

        private void CloseChapterHeaderEdit()
        {
            _isEditingChapterHeader  = false;
            TbChapterName.Visibility = Visibility.Visible;
            EdChapterName.Visibility = Visibility.Collapsed;
        }

        private void ChapterHeaderEditor_LostFocus(object sender, RoutedEventArgs e)
            => CommitChapterHeaderEdit();

        private void ChapterHeaderEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)  { CommitChapterHeaderEdit(); e.Handled = true; }
            if (e.Key == Key.Escape) { CloseChapterHeaderEdit();  e.Handled = true; }
        }

        // ════════════════════════════════════════════════════════════════
        //  ОБРАБОТЧИКИ — РЕДАКТИРОВАНИЕ НАЗВАНИЯ ГЛАВЫ (КАРТОЧКА)
        // ════════════════════════════════════════════════════════════════

        private void ChapterCardEditor_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox { DataContext: ChapterCardVm vm })
                vm.CommitEdit();
        }

        private void ChapterCardEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox { DataContext: ChapterCardVm vm }) return;
            if (e.Key == Key.Enter)  { vm.CommitEdit();  e.Handled = true; }
            if (e.Key == Key.Escape) { vm.CancelEdit();  e.Handled = true; }
        }

        // ════════════════════════════════════════════════════════════════
        //  ОБРАБОТЧИКИ — РЕДАКТИРОВАНИЕ НАЗВАНИЯ ВИДЕО (КАРТОЧКА)
        // ════════════════════════════════════════════════════════════════

        private void VideoCardEditor_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox { DataContext: VideoCardVm vm })
                vm.CommitEdit();
        }

        private void VideoCardEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox { DataContext: VideoCardVm vm }) return;
            if (e.Key == Key.Enter)  { vm.CommitEdit();  e.Handled = true; }
            if (e.Key == Key.Escape) { vm.CancelEdit();  e.Handled = true; }
        }

        // ── Фокус при появлении редактора ────────────────────────────────────

        private void Editor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox tb && (bool)e.NewValue)
            {
                tb.Dispatcher.BeginInvoke(() => { tb.SelectAll(); tb.Focus(); });
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  ТАЙМКОДЫ
        // ════════════════════════════════════════════════════════════════

        private void AddTimecode_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentVideo == null) return;

            var dlg = new DialogWindows.AddTimecodeDialog(_currentPos)
            {
                Owner = Window.GetWindow(this)
            };

            if (dlg.ShowDialog() != true) return;

            var tc = VideoTimecode.Create(dlg.ResultPosition, dlg.ResultLabel);
            _currentVideo.Timestamps.Add(tc);
            _currentVideo.Timestamps.Sort((a, b) => a.Position.CompareTo(b.Position));

            _ = SaveNewTimecodeAsync(tc);
            RebuildTimecodes();
            TimecodeChanged?.Invoke(_currentVideo);
        }

        private void TimecodeRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TimecodeVm vm)
                vm.Seek();
        }

        private void DeleteTimecode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TimecodeVm vm)
            {
                vm.Delete();
                e.Handled = true;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  БД
        // ════════════════════════════════════════════════════════════════

        private async Task SaveChapterAsync(ChapterCourseModel chapter)
        {
            if (_db == null) return;
            try
            {
                _db.Entry(chapter).Property(c => c.Title).IsModified = true;
                await _db.SaveChangesAsync();
            }
            catch { }
        }

        private async Task SaveVideoAsync(VideoModel video)
        {
            if (_db == null) return;
            try
            {
                var entry = _db.Entry(video);
                entry.Property(v => v.Title).IsModified     = true;
                entry.Property(v => v.IsWatched).IsModified = true;
                await _db.SaveChangesAsync();
            }
            catch { }
        }

        private async Task MarkWatchedAsync(VideoModel video, bool watched)
        {
            video.IsWatched = watched;

            if (_videoVmById.TryGetValue(video.Id, out var vm))
                vm.IsWatched = watched;

            RefreshChapterStats();
            await SaveVideoAsync(video);
        }

        private async Task SaveNewTimecodeAsync(VideoTimecode tc)
        {
            if (_db == null) return;
            try
            {
                _db.Add(tc);
                await _db.SaveChangesAsync();
            }
            catch { }
        }

        private async Task DeleteTimecodeAsync(TimecodeVm vm)
        {
            if (_currentVideo == null) return;

            _currentVideo.Timestamps.Remove(vm.Model);

            if (_db != null)
            {
                try { _db.Remove(vm.Model); await _db.SaveChangesAsync(); }
                catch { }
            }

            RebuildTimecodes();
            TimecodeChanged?.Invoke(_currentVideo);
        }
    }
}

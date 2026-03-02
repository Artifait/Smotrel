using Smotrel.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Smotrel.ViewModels
{
    // ══════════════════════════════════════════════════════════════════
    //  RelayCommand
    // ══════════════════════════════════════════════════════════════════

    public sealed class RelayCommand(
        Action<object?> execute,
        Func<object?, bool>? canExecute = null) : ICommand
    {
        public bool CanExecute(object? p) => canExecute?.Invoke(p) ?? true;
        public void Execute(object? p)    => execute(p);

        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Base ViewModel
    // ══════════════════════════════════════════════════════════════════

    public abstract class BaseVm : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void Notify([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    // ══════════════════════════════════════════════════════════════════
    //  ChapterCardViewModel
    // ══════════════════════════════════════════════════════════════════

    public sealed class ChapterCardVm : BaseVm
    {
        public ChapterCourseModel Model { get; }

        // ── Название ─────────────────────────────────────────────────

        private string _label;
        public string Label
        {
            get => _label;
            set { _label = value; Notify(); }
        }

        // ── Режим редактирования ──────────────────────────────────────

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing == value) return;
                _isEditing = value;
                Notify();
                Notify(nameof(IsNotEditing));
                if (value) EditText = _label;
            }
        }
        public bool IsNotEditing => !_isEditing;

        private string _editText = string.Empty;
        public string EditText
        {
            get => _editText;
            set { _editText = value; Notify(); }
        }

        // ── Коллбэки (устанавливает контрол) ─────────────────────────

        public Action<ChapterCardVm>? OnNavigate;
        public Action<ChapterCardVm>? OnRenamed;

        // ── Конструктор ───────────────────────────────────────────────

        public ChapterCardVm(ChapterCourseModel model)
        {
            Model  = model;
            _label = model.Title;
        }

        // ── Вызывается из code-behind ─────────────────────────────────

        public void StartEdit()
        {
            IsEditing = true;
        }

        public void CommitEdit()
        {
            var name = EditText.Trim();
            if (!string.IsNullOrWhiteSpace(name) && name != Model.Title)
            {
                Label       = name;
                Model.Title = name;
                OnRenamed?.Invoke(this);
            }
            IsEditing = false;
        }

        public void CancelEdit()
        {
            IsEditing = false;
        }

        public void Navigate()
        {
            if (!_isEditing) OnNavigate?.Invoke(this);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  VideoCardViewModel
    // ══════════════════════════════════════════════════════════════════

    public sealed class VideoCardVm : BaseVm
    {
        public VideoModel Model { get; }

        // ── Название ─────────────────────────────────────────────────

        private string _title;
        public string Title
        {
            get => _title;
            set { _title = value; Notify(); }
        }

        // ── IsWatched ─────────────────────────────────────────────────

        private bool _isWatched;
        public bool IsWatched
        {
            get => _isWatched;
            set
            {
                _isWatched      = value;
                Model.IsWatched = value;
                Notify();
                Notify(nameof(WatchedGlyph));
                Notify(nameof(WatchedColorHex));
            }
        }

        /// <summary>E73E = просмотрено, E739 = не просмотрено.</summary>
        public string WatchedGlyph    => IsWatched ? "\uE73E" : "\uE739";
        public string WatchedColorHex => IsWatched ? "#4CAF50" : "#666666";

        // ── Текущее видео ─────────────────────────────────────────────

        private bool _isCurrent;
        public bool IsCurrent
        {
            get => _isCurrent;
            set { _isCurrent = value; Notify(); }
        }

        // ── Редактирование ────────────────────────────────────────────

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing == value) return;
                _isEditing = value;
                Notify();
                Notify(nameof(IsNotEditing));
                if (value) EditText = _title;
            }
        }
        public bool IsNotEditing => !_isEditing;

        private string _editText = string.Empty;
        public string EditText
        {
            get => _editText;
            set { _editText = value; Notify(); }
        }

        // ── Коллбэки ─────────────────────────────────────────────────

        public Action<VideoCardVm>? OnSelect;
        public Action<VideoCardVm>? OnWatchedToggled;
        public Action<VideoCardVm>? OnRenamed;

        // ── Конструктор ───────────────────────────────────────────────

        public VideoCardVm(VideoModel model)
        {
            Model      = model;
            _title     = model.Title;
            _isWatched = model.IsWatched;
        }

        // ── Вызывается из code-behind ─────────────────────────────────

        public void ToggleWatched()
        {
            IsWatched = !IsWatched;
            OnWatchedToggled?.Invoke(this);
        }

        public void Select()
        {
            if (!_isEditing) OnSelect?.Invoke(this);
        }

        public void StartEdit()  => IsEditing = true;

        public void CommitEdit()
        {
            var name = EditText.Trim();
            if (!string.IsNullOrWhiteSpace(name) && name != Model.Title)
            {
                Title       = name;
                Model.Title = name;
                OnRenamed?.Invoke(this);
            }
            IsEditing = false;
        }

        public void CancelEdit() => IsEditing = false;
    }

    // ══════════════════════════════════════════════════════════════════
    //  TimecodeViewModel
    // ══════════════════════════════════════════════════════════════════

    public sealed class TimecodeVm : BaseVm
    {
        public VideoTimecode Model    { get; }
        public TimeSpan      Position => Model.Position;
        public string        Label    => Model.Label;
        public string        TimeText => FmtTime(Model.Position);

        public Action<TimecodeVm>? OnSeek;
        public Action<TimecodeVm>? OnDelete;

        public TimecodeVm(VideoTimecode model) => Model = model;

        public void Seek()   => OnSeek?.Invoke(this);
        public void Delete() => OnDelete?.Invoke(this);

        private static string FmtTime(TimeSpan ts) =>
            ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}

using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Smotrel.Models;

namespace Smotrel.Controls
{
    public partial class CourseNavigation : UserControl
    {
        // Dependency properties
        public static readonly DependencyProperty CourseProperty = DependencyProperty.Register(
            nameof(Course), typeof(CourseModel), typeof(CourseNavigation), new PropertyMetadata(null, OnCourseChanged));

        public static readonly DependencyProperty CurrentChapterProperty = DependencyProperty.Register(
            nameof(CurrentChapter), typeof(ChapterCourseModel), typeof(CourseNavigation), new PropertyMetadata(null, OnCurrentChapterChanged));

        // Routed events
        public static readonly RoutedEvent VideoSelectedEvent = EventManager.RegisterRoutedEvent(
            "VideoSelected", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CourseNavigation));

        public static readonly RoutedEvent ChapterOpenedEvent = EventManager.RegisterRoutedEvent(
            "ChapterOpened", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CourseNavigation));

        public static readonly RoutedEvent ParentChapterRequestedEvent = EventManager.RegisterRoutedEvent(
            "ParentChapterRequested", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CourseNavigation));

        public static readonly RoutedEvent TimestampSelectedEvent = EventManager.RegisterRoutedEvent(
            "TimestampSelected", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CourseNavigation));

        public static readonly RoutedEvent CreateTimestampRequestedEvent = EventManager.RegisterRoutedEvent(
            "CreateTimestampRequested", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CourseNavigation));

        // CLR wrappers
        public CourseModel Course
        {
            get => (CourseModel)GetValue(CourseProperty);
            set => SetValue(CourseProperty, value);
        }

        public ChapterCourseModel CurrentChapter
        {
            get => (ChapterCourseModel)GetValue(CurrentChapterProperty);
            set => SetValue(CurrentChapterProperty, value);
        }

        // Events (add/remove)
        public event RoutedEventHandler VideoSelected { add => AddHandler(VideoSelectedEvent, value); remove => RemoveHandler(VideoSelectedEvent, value); }
        public event RoutedEventHandler ChapterOpened { add => AddHandler(ChapterOpenedEvent, value); remove => RemoveHandler(ChapterOpenedEvent, value); }
        public event RoutedEventHandler ParentChapterRequested { add => AddHandler(ParentChapterRequestedEvent, value); remove => RemoveHandler(ParentChapterRequestedEvent, value); }
        public event RoutedEventHandler TimestampSelected { add => AddHandler(TimestampSelectedEvent, value); remove => RemoveHandler(TimestampSelectedEvent, value); }
        public event RoutedEventHandler CreateTimestampRequested { add => AddHandler(CreateTimestampRequestedEvent, value); remove => RemoveHandler(CreateTimestampRequestedEvent, value); }

        public CourseNavigation()
        {
            InitializeComponent();
        }

        private static void OnCourseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (CourseNavigation)d;
            var course = e.NewValue as CourseModel;
            if (course?.MainChapter != null)
            {
                ctrl.SetCurrentChapter(course.MainChapter);
            }
        }

        private static void OnCurrentChapterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (CourseNavigation)d;
            var ch = e.NewValue as ChapterCourseModel;
            ctrl.HeaderTitle.Text = ch?.Title ?? string.Empty;
            ctrl.HeaderEditBox.Visibility = Visibility.Collapsed;
            ctrl.ParentBtn.Visibility = (ch == null || ch.Parent == null) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SetCurrentChapter(ChapterCourseModel ch)
        {
            CurrentChapter = ch;
        }

        #region Header rename handlers

        private void HeaderTitle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // single click -> enable edit
            if (e.ClickCount == 1 && CurrentChapter != null)
            {
                HeaderEditBox.Text = CurrentChapter.Title;
                HeaderTitle.Visibility = Visibility.Collapsed;
                HeaderEditBox.Visibility = Visibility.Visible;
                HeaderEditBox.Focus();
                HeaderEditBox.SelectAll();
                e.Handled = true;
            }
        }

        private void HeaderEditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitHeaderRename();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelHeaderRename();
                e.Handled = true;
            }
        }

        private void HeaderEditBox_LostFocus(object sender, RoutedEventArgs e) => CommitHeaderRename();

        private void CommitHeaderRename()
        {
            if (CurrentChapter == null) return;
            var newTitle = HeaderEditBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(newTitle))
            {
                CancelHeaderRename();
                return;
            }
            if (newTitle != CurrentChapter.Title)
            {
                var old = CurrentChapter.Title;
                CurrentChapter.Title = newTitle;
                TrySaveTitleToDb(CurrentChapter);
            }
            HeaderEditBox.Visibility = Visibility.Collapsed;
            HeaderTitle.Visibility = Visibility.Visible;
        }

        private void CancelHeaderRename()
        {
            HeaderEditBox.Visibility = Visibility.Collapsed;
            HeaderTitle.Visibility = Visibility.Visible;
        }

        #endregion

        #region Chapter handlers (inline edit, open)

        private void ChapterTitleBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // single click -> inline edit for chapter
            if (sender is TextBlock tb && e.ClickCount == 1)
            {
                var parent = FindAncestor<Grid>(tb);
                if (parent == null) return;
                var edit = parent.FindName("ChapterTitleEdit") as TextBox;
                if (edit == null) return;
                edit.Text = tb.Text;
                tb.Visibility = Visibility.Collapsed;
                edit.Visibility = Visibility.Visible;
                edit.Focus();
                edit.SelectAll();
                e.Handled = true;
            }
        }

        private void ChapterTitleEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitChapterTitleEdit(sender as TextBox);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelChapterTitleEdit(sender as TextBox);
                e.Handled = true;
            }
        }

        private void ChapterTitleEdit_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitChapterTitleEdit(sender as TextBox);
        }

        private void CommitChapterTitleEdit(TextBox? edit)
        {
            if (edit == null) return;
            var panel = FindAncestor<Grid>(edit);
            if (panel == null) return;
            var tb = panel.FindName("ChapterTitleBlock") as TextBlock;
            if (tb == null) return;

            var data = edit.DataContext as ChapterCourseModel;
            if (data == null) { edit.Visibility = Visibility.Collapsed; tb.Visibility = Visibility.Visible; return; }

            var newTitle = edit.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(newTitle) && newTitle != data.Title)
            {
                data.Title = newTitle;
                TrySaveTitleToDb(data);
                tb.Text = newTitle;
            }

            edit.Visibility = Visibility.Collapsed;
            tb.Visibility = Visibility.Visible;
        }

        private void CancelChapterTitleEdit(TextBox? edit)
        {
            if (edit == null) return;
            var panel = FindAncestor<Grid>(edit);
            if (panel == null) return;
            var tb = panel.FindName("ChapterTitleBlock") as TextBlock;
            if (tb != null) tb.Visibility = Visibility.Visible;
            edit.Visibility = Visibility.Collapsed;
        }

        private void ChapterTitleBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ChapterTitleBlock_MouseDoubleClick(sender, e);
            }
        }

        private void ChapterTitleBlock_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock tb && tb.DataContext is ChapterCourseModel ch)
            {
                // Navigate into this chapter
                SetCurrentChapter(ch);
                RaiseEvent(new RoutedEventArgs(ChapterOpenedEvent, ch));
                e.Handled = true;
            }
        }

        private void OpenChapterBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is ChapterCourseModel ch)
            {
                SetCurrentChapter(ch);
                RaiseEvent(new RoutedEventArgs(ChapterOpenedEvent, ch));
            }
        }

        #endregion

        #region Video handlers (inline edit, expand, double-click play)

        private void VideoTitleBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // single click -> inline edit
            if (sender is TextBlock tb && e.ClickCount == 1)
            {
                var parent = FindAncestor<Grid>(tb);
                if (parent == null) return;
                var edit = parent.FindName("VideoTitleEdit") as TextBox;
                if (edit == null) return;
                edit.Text = tb.Text;
                tb.Visibility = Visibility.Collapsed;
                edit.Visibility = Visibility.Visible;
                edit.Focus();
                edit.SelectAll();
                e.Handled = true;
            }
        }

        private void VideoTitleEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitVideoTitleEdit(sender as TextBox);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelVideoTitleEdit(sender as TextBox);
                e.Handled = true;
            }
        }

        private void VideoTitleEdit_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitVideoTitleEdit(sender as TextBox);
        }

        private void CommitVideoTitleEdit(TextBox? edit)
        {
            if (edit == null) return;
            var panel = FindAncestor<Grid>(edit);
            if (panel == null) return;
            var tb = panel.FindName("VideoTitleBlock") as TextBlock;
            if (tb == null) return;

            var data = edit.DataContext as VideoModel;
            if (data == null) { edit.Visibility = Visibility.Collapsed; tb.Visibility = Visibility.Visible; return; }

            var newTitle = edit.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(newTitle) && newTitle != data.Title)
            {
                data.Title = newTitle;
                TrySaveTitleToDb(data);
                tb.Text = newTitle;
            }

            edit.Visibility = Visibility.Collapsed;
            tb.Visibility = Visibility.Visible;
        }

        private void CancelVideoTitleEdit(TextBox? edit)
        {
            if (edit == null) return;
            var panel = FindAncestor<Grid>(edit);
            if (panel == null) return;
            var tb = panel.FindName("VideoTitleBlock") as TextBlock;
            if (tb != null) tb.Visibility = Visibility.Visible;
            edit.Visibility = Visibility.Collapsed;
        }


        private void VideoTitleBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                VideoTitleBlock_MouseDoubleClick(sender, e);
            }
        }

        private void VideoTitleBlock_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock tb && tb.DataContext is VideoModel vm)
            {
                // Play video from start (generate timestamp with 0)
                var args = new RoutedEventArgs(VideoSelectedEvent, vm);
                RaiseEvent(args);
                e.Handled = true;
            }
        }

        private void VideoExpandBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is VideoModel vm)
            {
                // find parent TimestampsPanel in visual tree and toggle visibility
                var container = FindAncestor<StackPanel>(b);
                if (container == null) return;
                var timestampsPanel = container.FindName("TimestampsPanel") as StackPanel;
                if (timestampsPanel == null) return;
                timestampsPanel.Visibility = timestampsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        #endregion

        #region Timestamps and create timestamp

        private void Timestamp_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock tb && tb.Tag is VideoTimestamp ts)
            {
                var args = new RoutedEventArgs(TimestampSelectedEvent, ts);
                RaiseEvent(args);
                e.Handled = true;
            }
        }

        private void AddTimestampBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is VideoModel vm)
            {
                var args = new RoutedEventArgs(CreateTimestampRequestedEvent, vm);
                RaiseEvent(args);
            }
        }

        #endregion

        #region Parent button

        private void ParentBtn_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentChapter?.Parent != null)
            {
                SetCurrentChapter(CurrentChapter.Parent);
                RaiseEvent(new RoutedEventArgs(ParentChapterRequestedEvent, CurrentChapter.Parent));
            }
        }

        #endregion

        #region Helpers: DB save and visual tree

        private void TrySaveTitleToDb(object model)
        {
            // Attempt to persist title change to DB. If fails — leave in-memory and notify user.
            try
            {
                using var ctx = new Smotrel.Models.SmotrelContext();
                if (model is ChapterCourseModel ch)
                {
                    // Find chapter by id in stored course
                    var dbCourse = ctx.Courses.Find(Course.Id);
                    if (dbCourse != null)
                    {
                        var found = FindChapterIn(dbCourse.MainChapter, ch.Id);
                        if (found != null)
                        {
                            found.Title = ch.Title;
                            ctx.SaveChanges();
                            return;
                        }
                    }

                    // fallback: attach and update
                    ctx.Attach(ch);
                    ctx.Entry(ch).Property("Title").IsModified = true;
                    ctx.SaveChanges();
                    return;
                }
                else if (model is VideoModel vm)
                {
                    var dbCourse = ctx.Courses.Find(Course.Id);
                    if (dbCourse != null)
                    {
                        var found = FindVideoIn(dbCourse.MainChapter, vm.Id);
                        if (found != null)
                        {
                            found.Title = vm.Title;
                            ctx.SaveChanges();
                            return;
                        }
                    }

                    ctx.Attach(vm);
                    ctx.Entry(vm).Property("Title").IsModified = true;
                    ctx.SaveChanges();
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось сохранить имя в БД: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private ChapterCourseModel? FindChapterIn(ChapterCourseModel root, int id)
        {
            if (root == null) return null;
            if (root.Id == id) return root;
            if (root.Chapters == null) return null;
            foreach (var c in root.Chapters)
            {
                var f = FindChapterIn(c, id);
                if (f != null) return f;
            }
            return null;
        }

        private VideoModel? FindVideoIn(ChapterCourseModel root, int id)
        {
            if (root == null) return null;
            if (root.Videos != null)
            {
                var v = root.Videos.FirstOrDefault(x => x.Id == id);
                if (v != null) return v;
            }
            if (root.Chapters != null)
            {
                foreach (var c in root.Chapters)
                {
                    var f = FindVideoIn(c, id);
                    if (f != null) return f;
                }
            }
            return null;
        }

        // Helper to find ancestor of specific type
        private static T? FindAncestor<T>(DependencyObject? child) where T : DependencyObject
        {
            var current = child;
            while (current != null)
            {
                if (current is T t) return t;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        #endregion
    }
}
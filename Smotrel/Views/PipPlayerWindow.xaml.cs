using System.Windows;
using System.Windows.Input;
using Smotrel.Controls;

namespace Smotrel.Views
{
    /// <summary>
    /// PiP-окно с отдельным SmotrelPlayer.
    /// Перетаскивается за любую точку.
    /// При закрытии активирует MainPlayer.
    /// </summary>
    public partial class PipPlayerWindow : Window
    {
        private readonly MainPlayer _owner;

        /// <summary>Флаг: закрытие инициировано MainPlayer, а не пользователем.</summary>
        private bool _forceClosing;

        public PipPlayerWindow(MainPlayer owner)
        {
            InitializeComponent();
            _owner = owner;
        }

        // ── Перетаскивание ────────────────────────────────────────────────────

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Одиночный клик — DragMove только если не по кнопке
            if (e.OriginalSource is System.Windows.Controls.Button) return;
            if (e.OriginalSource is System.Windows.Controls.TextBlock tb
                && tb.Parent is System.Windows.Controls.Button)       return;

            try { DragMove(); } catch { }
        }

        // ── Закрытие ──────────────────────────────────────────────────────────

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);

            if (!_forceClosing)
            {
                // Пользователь закрыл PiP вручную → возвращаем в Normal
                // Не отменяем — пусть окно закроется, MainPlayer восстановит Normal
                ActivateMainPlayer();
            }
        }

        /// <summary>Активирует MainPlayer: Topmost → Activate → снимаем Topmost.</summary>
        private void ActivateMainPlayer()
        {
            _owner.Topmost = true;
            _owner.Activate();
            _owner.Topmost = false;
        }

        /// <summary>Закрытие из кода MainPlayer (без повторного вызова SwitchToNormal).</summary>
        public void ForceClose()
        {
            _forceClosing = true;
            ActivateMainPlayer();
            Close();
        }

        private void PipWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Если закрывает пользователь (не ForceClose) — просим MainPlayer вернуться в Normal
            if (!_forceClosing)
            {
                // Используем Dispatcher чтобы не вызывать SwitchToNormal из середины Closing
                Dispatcher.BeginInvoke(() =>
                {
                    // Вызываем через reflection-like доступ к публичному методу
                    // MainPlayer подпишется на это через собственный механизм
                    // или можно сделать публичный метод:
                    _owner.OnPipWindowClosedByUser();
                });
            }
        }
    }
}

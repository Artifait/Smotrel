using System.Reflection;
using System.Windows;

namespace Smotrel
{
    public partial class TestBenchHost : Window
    {
        private record BenchInfo(string Name, Type Type);

        public TestBenchHost()
        {
            InitializeComponent();
            LoadBenches();
        }

        private void LoadBenches()
        {
            // Сканируем пространство имён Smotrel.TestBenches
            var benches = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => t.IsSubclassOf(typeof(Window)) &&
                            t.Namespace == "Smotrel.TestBenches")
                .Select(t => new BenchInfo(t.Name.Replace("Bench", ""), t))
                .OrderBy(t => t.Name)
                .ToList();

            BenchList.ItemsSource = benches;
        }

        private void BenchList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (BenchList.SelectedItem is BenchInfo info)
            {
                var window = (Window)Activator.CreateInstance(info.Type)!;
                window.Show();
                BenchList.SelectedItem = null;
            }
        }
    }
}


using Microsoft.Extensions.DependencyInjection;
using Smotrel.Services;
using Smotrel.ViewModels;
using Smotrel.Views;
using System.Windows;

namespace Smotrel
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private IServiceProvider _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);

            _serviceProvider = serviceCollection.BuildServiceProvider();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<ICourseScanner, CourseScanner>();
            services.AddSingleton<ICourseMergeService, CourseMergeService>();
            services.AddSingleton<ICourseRepository, EfCourseRepository>();
            services.AddSingleton<IPlaybackService, PlaybackService>();
            services.AddSingleton<IVideoLibraryService, FileSystemVideoLibraryService>();

            services.AddSingleton<MainViewModel>(); // <- важно
            services.AddSingleton<MainWindow>();
            services.AddLogging();
        }

        private void OnExit(object sender, ExitEventArgs e)
        {
            // Dispose of services if needed
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

}

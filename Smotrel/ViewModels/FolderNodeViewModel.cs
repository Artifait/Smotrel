using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Smotrel.Messages;

namespace Smotrel.ViewModels
{
    public class FolderNodeViewModel : BaseViewModel
    {
        public string Title { get; }
        public string FolderPath { get; }
        public ObservableCollection<BaseViewModel> Children { get; }

        public IEnumerable<FolderNodeViewModel> SubFolders
            => Children.OfType<FolderNodeViewModel>();

        public IRelayCommand OpenFolderCommand { get; }

        public FolderNodeViewModel(string title, string path)
        {
            Title = title;
            FolderPath = path;
            Children = new ObservableCollection<BaseViewModel>();

            OpenFolderCommand = new RelayCommand(() =>
                WeakReferenceMessenger.Default.Send(new OpenFolderMessage(FolderPath)));
        }
    }
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using TronEvolutionPatcher.ViewModels;

namespace TronEvolutionPatcher.Views
{
    public partial class MainWindow : Window
    {
        private bool initialized;
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            MaxHeight = SystemParameters.WorkArea.Height;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (initialized) return;
            initialized = true;
            if (DataContext is MainViewModel model) await model.InitializeAsync();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (DataContext is MainViewModel model && model.IsBusy) { e.Cancel = true; model.NotifyCloseBlocked(); }
        }

        private void OnLogChanged(object sender, TextChangedEventArgs e) { (sender as TextBox)?.ScrollToEnd(); }
    }
}

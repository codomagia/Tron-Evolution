using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace TronEvolutionPatcher.ViewModels
{
    public sealed class AsyncCommand : ICommand
    {
        private readonly Func<Task> execute;
        private readonly Func<bool> canExecute;
        private readonly Action<Exception> reportError;
        private bool executing;

        public AsyncCommand(Func<Task> execute, Func<bool> canExecute, Action<Exception> reportError)
        {
            this.execute = execute;
            this.canExecute = canExecute;
            this.reportError = reportError;
        }

        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter) { return !executing && canExecute(); }
        public async void Execute(object parameter) { await ExecuteAsync(); }

        public async Task ExecuteAsync()
        {
            if (!CanExecute(null)) return;
            executing = true;
            RaiseCanExecuteChanged();
            try { await execute(); }
            catch (Exception error) { reportError(error); }
            finally { executing = false; RaiseCanExecuteChanged(); }
        }

        public void RaiseCanExecuteChanged() { CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace TronEvolutionPatcher.Services
{
    internal interface IDesktopInteraction
    {
        string ChooseDirectory(string initialDirectory);
        void OpenSupport();
    }

    internal sealed class DesktopInteraction : IDesktopInteraction
    {
        private sealed class DialogOwner : Forms.IWin32Window
        {
            public IntPtr Handle { get; }
            public DialogOwner(Window window) { Handle = new WindowInteropHelper(window).Handle; }
        }

        public string ChooseDirectory(string initialDirectory)
        {
            using (var dialog = new Forms.FolderBrowserDialog
            {
                Description = "Выберите корень TRON: Evolution или папку с GridGame.exe",
                ShowNewFolderButton = false,
                SelectedPath = Directory.Exists(initialDirectory) ? initialDirectory : AppDomain.CurrentDomain.BaseDirectory
            })
            {
                var owner = Application.Current?.MainWindow;
                var result = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(new DialogOwner(owner));
                return result == Forms.DialogResult.OK ? dialog.SelectedPath : null;
            }
        }

        public void OpenSupport()
        {
            using (var process = Process.Start(new ProcessStartInfo("https://boosty.to/codomagia/donate") { UseShellExecute = true })) { }
        }
    }
}

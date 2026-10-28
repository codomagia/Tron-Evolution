using System;
using System.Diagnostics;
using System.IO;
using TronEvolutionPatcher.Models;

namespace TronEvolutionPatcher.Services
{
    internal interface IGameProcessStarter
    {
        int Start(ProcessStartInfo startInfo);
    }

    internal sealed class GameProcessStarter : IGameProcessStarter
    {
        public int Start(ProcessStartInfo startInfo)
        {
            using (var process = Process.Start(startInfo))
            {
                if (process == null) throw new InvalidOperationException("Не удалось создать процесс игры.");
                // Disposing this handle does not stop the child process.
                return process.Id;
            }
        }
    }

    public sealed class GameLaunchService
    {
        private readonly IGameProcessStarter starter;

        public GameLaunchService() : this(new GameProcessStarter()) { }
        internal GameLaunchService(IGameProcessStarter starter) { this.starter = starter; }

        public ProcessStartInfo CreateStartInfo(string win32Directory)
        {
            if (string.IsNullOrWhiteSpace(win32Directory)) throw new ArgumentException("Не задана папка игры.", nameof(win32Directory));
            var directory = Path.GetFullPath(win32Directory);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Папка игры не найдена: " + directory);
            foreach (var name in new[] { "GridGameLauncherc.exe", "GridGameLauncher.exe", "GridGame.exe" })
            {
                var path = Path.Combine(directory, name);
                var attributes = ProfileDirectories.Attributes(path);
                if (!attributes.HasValue || (attributes.Value & FileAttributes.Directory) != 0) continue;
                return new ProcessStartInfo
                {
                    FileName = path, WorkingDirectory = directory,
                    Arguments = string.Empty, UseShellExecute = false, CreateNoWindow = false
                };
            }
            throw new FileNotFoundException("В папке игры не найден GridGameLauncherc.exe, GridGameLauncher.exe или GridGame.exe.");
        }

        public GameLaunchResult Launch(string win32Directory)
        {
            var startInfo = CreateStartInfo(win32Directory);
            var id = starter.Start(startInfo);
            if (id <= 0) throw new InvalidOperationException("Не получен идентификатор процесса игры.");
            return new GameLaunchResult
            {
                ExecutablePath = startInfo.FileName, WorkingDirectory = startInfo.WorkingDirectory, ProcessId = id
            };
        }
    }
}

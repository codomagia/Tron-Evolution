using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using TronEvolutionPatcher.Services;

namespace TronEvolutionPatcher.Tests
{
    [TestFixture]
    public class GameLaunchServiceTests
    {
        private sealed class RecordingStarter : IGameProcessStarter
        {
            public ProcessStartInfo Received;
            public int Calls;
            public int ProcessId = 1234;
            public Exception Failure;

            public int Start(ProcessStartInfo startInfo)
            {
                Calls++;
                Received = startInfo;
                if (Failure != null) throw Failure;
                return ProcessId;
            }
        }

        [TestCase(true, true, true, "GridGameLauncherc.exe")]
        [TestCase(true, false, false, "GridGameLauncherc.exe")]
        [TestCase(false, true, true, "GridGameLauncher.exe")]
        [TestCase(false, true, false, "GridGameLauncher.exe")]
        [TestCase(false, false, true, "GridGame.exe")]
        public void UsesOriginalLauncherPriorityAndCorrectWorkingDirectory(bool launcherc, bool launcher, bool game, string expected)
        {
            using (var temp = new TemporaryDirectory())
            {
                var directory = Path.Combine(temp.Root, "Игра с пробелами", "Binaries", "Win32Live");
                Directory.CreateDirectory(directory);
                // These fixtures are only passed to the recording starter, never executed.
                if (launcherc) File.WriteAllText(Path.Combine(directory, "GridGameLauncherc.exe"), "fixture");
                if (launcher) File.WriteAllText(Path.Combine(directory, "GridGameLauncher.exe"), "fixture");
                if (game) File.WriteAllText(Path.Combine(directory, "GridGame.exe"), "fixture");
                var starter = new RecordingStarter();
                var service = new GameLaunchService(starter);
                var beforeDirectory = Environment.CurrentDirectory;
                var result = service.Launch(directory);
                Assert.That(starter.Calls, Is.EqualTo(1));
                Assert.That(starter.Received.FileName, Is.EqualTo(Path.Combine(directory, expected)));
                Assert.That(starter.Received.WorkingDirectory, Is.EqualTo(directory));
                Assert.That(starter.Received.UseShellExecute, Is.False);
                Assert.That(starter.Received.Arguments, Is.Empty);
                Assert.That(starter.Received.Verb, Is.Empty);
                Assert.That(starter.Received.CreateNoWindow, Is.False);
                Assert.That(result.ProcessId, Is.EqualTo(1234));
                Assert.That(result.ExecutablePath, Is.EqualTo(starter.Received.FileName));
                Assert.That(result.WorkingDirectory, Is.EqualTo(directory));
                Assert.That(Environment.CurrentDirectory, Is.EqualTo(beforeDirectory));
            }
        }

        [Test]
        public void PreparingLaunchDoesNotStartProcessAndNormalizesDirectory()
        {
            using (var temp = new TemporaryDirectory())
            {
                temp.WriteText("Game/GridGame.exe", "fixture");
                var directory = Path.Combine(temp.Root, "Game");
                var starter = new RecordingStarter();
                var info = new GameLaunchService(starter).CreateStartInfo(Path.Combine(directory, "..", "Game"));
                Assert.That(info.WorkingDirectory, Is.EqualTo(directory));
                Assert.That(starter.Calls, Is.Zero);
            }
        }

        [Test]
        public void DirectoryNamedAfterLauncherIsSkipped()
        {
            using (var temp = new TemporaryDirectory())
            {
                Directory.CreateDirectory(Path.Combine(temp.Root, "GridGameLauncherc.exe"));
                var fallback = temp.WriteText("GridGame.exe", "fixture");
                Assert.That(new GameLaunchService(new RecordingStarter()).CreateStartInfo(temp.Root).FileName, Is.EqualTo(fallback));
            }
        }

        [Test]
        public void MissingExecutableDoesNotStartOrCreateAnything()
        {
            using (var temp = new TemporaryDirectory())
            {
                var starter = new RecordingStarter();
                Assert.Throws<FileNotFoundException>(() => new GameLaunchService(starter).Launch(temp.Root));
                Assert.That(starter.Calls, Is.Zero);
                Assert.That(Directory.GetFileSystemEntries(temp.Root), Is.Empty);
            }
        }

        [Test]
        public void MissingDirectoryDoesNotStartProcess()
        {
            using (var temp = new TemporaryDirectory())
            {
                var starter = new RecordingStarter();
                var missing = Path.Combine(temp.Root, "missing");
                Assert.Throws<DirectoryNotFoundException>(() => new GameLaunchService(starter).Launch(missing));
                Assert.That(starter.Calls, Is.Zero);
                Assert.That(Directory.Exists(missing), Is.False);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void InvalidDirectoryRejectsBeforeStarting(string directory)
        {
            var starter = new RecordingStarter();
            Assert.Throws<ArgumentException>(() => new GameLaunchService(starter).Launch(directory));
            Assert.That(starter.Calls, Is.Zero);
        }

        [Test]
        public void ProcessStartErrorIsReportedWithoutTryingOtherLaunchers()
        {
            using (var temp = new TemporaryDirectory())
            {
                temp.WriteText("GridGameLauncherc.exe", "fixture");
                temp.WriteText("GridGame.exe", "fixture");
                var failure = new Win32Exception(193, "Fixture is not an executable");
                var starter = new RecordingStarter { Failure = failure };
                Assert.That(Assert.Throws<Win32Exception>(() => new GameLaunchService(starter).Launch(temp.Root)), Is.SameAs(failure));
                Assert.That(starter.Calls, Is.EqualTo(1));
            }
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void MissingProcessIdIsNotReportedAsSuccess(int id)
        {
            using (var temp = new TemporaryDirectory())
            {
                temp.WriteText("GridGame.exe", "fixture");
                var starter = new RecordingStarter { ProcessId = id };
                Assert.Throws<InvalidOperationException>(() => new GameLaunchService(starter).Launch(temp.Root));
                Assert.That(starter.Calls, Is.EqualTo(1));
            }
        }
    }
}

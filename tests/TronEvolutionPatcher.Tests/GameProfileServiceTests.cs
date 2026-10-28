using System.IO;
using NUnit.Framework;
using TronEvolutionPatcher.Services;
using TronEvolutionPatcher.Patching;

namespace TronEvolutionPatcher.Tests
{
    [TestFixture]
    public class GameProfileServiceTests
    {
        private const string Profile = "Disney Interactive Studios/Tron Evolution/";
        private const string Ini = Profile + "UnrealEngine3/GridGame/Config/GridEngine.ini";
        private const string Save = Profile + "SaveData/profile/Save/CHECKPT";
        private static string DirectoryFor(TemporaryDirectory temp) => Path.Combine(temp.Root, "Game/Binaries/Win32Live");
        private static GameProfileService Service(TemporaryDirectory temp) => new GameProfileService(Path.Combine(temp.Root, "Documents"));

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ReadinessRequiresGameGeneratedConfigurationButNotGameplayCheckpoint(bool ini, bool checkpoint)
        {
            using (var temp = new TemporaryDirectory())
            {
                if (ini) {
                    temp.WriteText("Documents/" + Ini, "[SystemSettings]\nResX=1280");
                    temp.WriteText("Documents/" + Ini.Replace("GridEngine.ini", "GridGame.ini"), "game");
                    temp.WriteText("Documents/" + Ini.Replace("GridEngine.ini", "GridInput.ini"), "input");
                }
                if (checkpoint) temp.Write("Documents/" + Save, new byte[] { 3, 4 });
                var profile = Service(temp).Inspect(DirectoryFor(temp), false);
                Assert.That(profile.IsInitialized, Is.EqualTo(ini));
                if (!profile.IsInitialized)
                    Assert.Throws<IOException>(() => Service(temp).RequireInitialized(DirectoryFor(temp), false));
            }
        }

        [Test]
        public void KeyBindingsAndSingleEngineIniDoNotReplaceMissingConfiguration()
        {
            using (var temp = new TemporaryDirectory())
            {
                temp.WriteText("Documents/" + Ini, "[SystemSettings]");
                temp.Write("Documents/" + Save, new byte[0]);
                temp.WriteText("Documents/" + Profile + "SaveData/profile/DefaultGridKeyBindings.ini", "bindings");
                Assert.That(Service(temp).Inspect(DirectoryFor(temp), false).IsInitialized, Is.False);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ExplicitCopyWorksBothDirectionsAndNeverModifiesExecutable(bool toPortable)
        {
            using (var temp = new TemporaryDirectory())
            {
                var source = toPortable ? "Documents/" : "Game/UserData/";
                var target = toPortable ? "Game/UserData/" : "Documents/";
                var exe = temp.Write("Game/Binaries/Win32Live/GridGame.exe", new byte[] { 9, 8, 7 });
                temp.WriteText(source + Ini.Replace("GridEngine.ini", "GridGame.ini"), "game");
                var ini = temp.WriteText(source + Ini, "[SystemSettings]\nResX=1280");
                var save = temp.Write(source + Save, new byte[] { 1, 2, 3 });
                temp.WriteText(source + Profile + "UnrealEngine3/GridGame/Config/GridInput.ini", "input settings");
                Service(temp).Copy(DirectoryFor(temp), toPortable);
                Assert.That(File.ReadAllBytes(Path.Combine(temp.Root, target + Ini)), Is.EqualTo(File.ReadAllBytes(ini)));
                Assert.That(File.ReadAllBytes(Path.Combine(temp.Root, target + Save)), Is.EqualTo(File.ReadAllBytes(save)));
                Assert.That(File.Exists(ini), Is.True);
                Assert.That(File.ReadAllBytes(exe), Is.EqualTo(new byte[] { 9, 8, 7 }));
                Assert.That(Service(temp).Inspect(DirectoryFor(temp), toPortable).IsInitialized, Is.True);
            }
        }

        [Test]
        public void ConfigurationCanBeCopiedBeforeFirstGameplayCheckpoint()
        {
            using (var temp = new TemporaryDirectory())
            {
                System.IO.Directory.CreateDirectory(DirectoryFor(temp));
                foreach (var name in new[] { "GridEngine.ini", "GridGame.ini", "GridInput.ini" })
                    temp.WriteText("Documents/" + Ini.Replace("GridEngine.ini", name), "game-generated config");
                Service(temp).Copy(DirectoryFor(temp), true);
                var target = Service(temp).Inspect(DirectoryFor(temp), true);
                Assert.That(target.IsInitialized, Is.True);
                Assert.That(target.HasCheckpoint, Is.False);
                Assert.That(File.Exists(Path.Combine(temp.Root, "Game/UserData/" + Save)), Is.False);
            }
        }

        [Test]
        public void IncompleteNonEmptyTargetIsNotMixedOrOverwritten()
        {
            using (var temp = new TemporaryDirectory())
            {
                temp.WriteText("Documents/" + Ini.Replace("GridEngine.ini", "GridGame.ini"), "game");
                temp.WriteText("Documents/" + Ini.Replace("GridEngine.ini", "GridInput.ini"), "input");
                temp.WriteText("Documents/" + Ini, "source"); temp.Write("Documents/" + Save, new byte[] { 1 });
                var target = temp.WriteText("Game/UserData/" + Ini, "old partial config");
                Assert.Throws<IOException>(() => Service(temp).Copy(DirectoryFor(temp), true));
                Assert.That(File.ReadAllText(target), Is.EqualTo("old partial config"));
                Assert.That(File.Exists(Path.Combine(temp.Root, "Game/UserData/" + Save)), Is.False);
            }
        }

        private sealed class FailAfterInstall : IFileMutation
        {
            private readonly FileMutation actual = new FileMutation();
            private bool failed;
            public void Install(string staged, string target, bool replace)
            {
                actual.Install(staged, target, replace);
                if (!failed) { failed = true; throw new IOException("Copy failed after installation"); }
            }
            public void Delete(string path) { actual.Delete(path); }
            public void SetAttributes(string path, FileAttributes attributes) { actual.SetAttributes(path, attributes); }
        }

        [Test]
        public void FailedCopyRollsBackFilesAndNewDirectoriesWithoutTouchingSource()
        {
            using (var temp = new TemporaryDirectory())
            {
                temp.WriteText("Documents/" + Ini.Replace("GridEngine.ini", "GridGame.ini"), "game");
                temp.WriteText("Documents/" + Ini.Replace("GridEngine.ini", "GridInput.ini"), "input");
                var source = temp.WriteText("Documents/" + Ini, "source config");
                temp.Write("Documents/" + Save, new byte[] { 1 });
                System.IO.Directory.CreateDirectory(Path.Combine(temp.Root, "Game"));
                var service = new GameProfileService(Path.Combine(temp.Root, "Documents"), new FailAfterInstall());
                Assert.Throws<IOException>(() => service.Copy(DirectoryFor(temp), true));
                Assert.That(File.ReadAllText(source), Is.EqualTo("source config"));
                Assert.That(System.IO.Directory.Exists(Path.Combine(temp.Root, "Game/UserData")), Is.False);
            }
        }
    }
}

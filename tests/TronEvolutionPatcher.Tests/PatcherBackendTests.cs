using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TronEvolutionPatcher.Models;
using TronEvolutionPatcher.Patching;
using TronEvolutionPatcher.Services;
using TronEvolutionPatcher.ViewModels;

namespace TronEvolutionPatcher.Tests
{
    [TestFixture]
    public class PatcherBackendTests
    {
        private const string Ini = "Disney Interactive Studios/Tron Evolution/UnrealEngine3/GridGame/Config/GridEngine.ini";

        [Test]
        public void DiscoveryFindsGameRelativeToApplicationWithoutFixedDrive()
        {
            using (var temp = new TemporaryDirectory())
            {
                var exe = temp.Write("Game/Binaries/Win32Live/GridGame.exe", PeFixture.Create());
                var app = Path.Combine(temp.Root, "Game", "Patcher");
                Directory.CreateDirectory(app);
                var backend = new PatcherBackend(app, Path.Combine(temp.Root, "Documents"));
                Assert.That(backend.Discover(), Is.EqualTo(Path.GetDirectoryName(exe)));
            }
        }

        [Test]
        public void ActiveProfileControlsReadAndWriteEvenIfPortableIniStillExists()
        {
            using (var temp = new TemporaryDirectory())
            {
                var exe = temp.Write("Game/Binaries/Win32Live/GridGame.exe", PeFixture.Create());
                temp.WriteText("Documents/" + Ini.Replace("GridEngine.ini", "GridGame.ini"), "game");
                temp.WriteText("Documents/" + Ini.Replace("GridEngine.ini", "GridInput.ini"), "input");
                var documents = temp.WriteText("Documents/" + Ini, "[SystemSettings]\nResX=1280");
                temp.Write("Documents/Disney Interactive Studios/Tron Evolution/SaveData/profile/Save/CHECKPT", new byte[] { 1 });
                var portable = temp.WriteText("Game/UserData/" + Ini, "[SystemSettings]\nResX=3840");
                var backend = new PatcherBackend(temp.Root, Path.Combine(temp.Root, "Documents"));
                var directory = Path.GetDirectoryName(exe);
                Assert.That(backend.Load(directory).Settings.ResolutionWidth, Is.EqualTo(1280));
                Assert.That(backend.Load(directory).IniPath, Is.EqualTo(documents));
                backend.ApplySettings(directory, new GameSettings { ResolutionWidth = 2560 }, false, false, false, false);
                Assert.That(File.ReadAllText(documents), Does.Contain("ResX=2560"));
                Assert.That(File.ReadAllText(portable), Does.Contain("ResX=3840"));
                Assert.That(File.Exists(Path.Combine(directory, GameStatusService.BackupName)), Is.False,
                    "Unchanged portable flags must not run the binary patcher.");
            }
        }

        [Test]
        public void PartialApplyReportsCompletedPortableStepAndKeepsItsActualState()
        {
            using (var temp = new TemporaryDirectory())
            {
                var exe = temp.Write("Game/Binaries/Win32Live/GridGame.exe", PortablePeFixture.Create());
                var directory = Path.GetDirectoryName(exe);
                var backend = new PatcherBackend(temp.Root, Path.Combine(temp.Root, "Documents"));
                temp.WriteText("Documents/" + Ini.Replace("GridEngine.ini", "GridGame.ini"), "game");
                temp.WriteText("Documents/" + Ini.Replace("GridEngine.ini", "GridInput.ini"), "input");
                var ini = temp.WriteText("Documents/" + Ini, "[SystemSettings]\nResX=1280");
                temp.Write("Documents/Disney Interactive Studios/Tron Evolution/SaveData/profile/Save/CHECKPT", new byte[] { 1 });
                using (var locked = new FileStream(ini, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var error = Assert.Throws<IOException>(() => backend.ApplySettings(directory, new GameSettings(), true, false, false, false));
                    Assert.That(error.Message, Does.Contain("Уже выполнено"));
                    Assert.That(error.Message, Does.Contain("GridEngine.ini"));
                }
                Assert.That(backend.Load(directory).Status.NoRegistryPatched, Is.True);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingProfileBlocksEveryApplyStepBeforeChangingExecutableOrCreatingIni(bool partialIni)
        {
            using (var temp = new TemporaryDirectory())
            {
                var original = PortablePeFixture.Create();
                var exe = temp.Write("Game/Binaries/Win32Live/GridGame.exe", original);
                temp.WriteText("Game/GridGame/Config/DefaultEngine.ini", "[SystemSettings]\nResX=1280");
                if (partialIni)
                {
                    var partial = temp.WriteText("Game/UserData/" + Ini, "[SystemSettings]\nResX=1920");
                    File.SetAttributes(partial, FileAttributes.ReadOnly);
                }
                var directory = Path.GetDirectoryName(exe);
                var backend = new PatcherBackend(temp.Root, Path.Combine(temp.Root, "Documents"));
                var error = Assert.Throws<IOException>(() => backend.ApplySettings(directory, new GameSettings(), true, true, false, false));
                Assert.That(error.Message, Does.Contain("Проверка профиля"));
                Assert.That(error.Message, Does.Contain("создания пользовательских конфигов"));
                Assert.That(PortableExecutablePatcherTests.Hash(File.ReadAllBytes(exe)), Is.EqualTo(PortableExecutablePatcherTests.Hash(original)));
                Assert.That(File.Exists(Path.Combine(directory, GameStatusService.BackupName)), Is.False);
                Assert.That(Directory.Exists(Path.Combine(temp.Root, "Game/UserData")), Is.EqualTo(partialIni));
                if (partialIni) Assert.That(File.ReadAllText(Path.Combine(temp.Root, "Game/UserData/" + Ini)), Does.Contain("ResX=1920"));
                Assert.That(Directory.Exists(Path.Combine(temp.Root, "Documents")), Is.False);
            }
        }

        [Test]
        public void ProfileCopyCanBeAppliedOnlyAfterExplicitCopyAndKeepsExistingSource()
        {
            using (var temp = new TemporaryDirectory())
            {
                var exe = temp.Write("Game/Binaries/Win32Live/GridGame.exe", PortablePeFixture.Create());
                var directory = Path.GetDirectoryName(exe);
                temp.WriteText("Documents/" + Ini.Replace("GridEngine.ini", "GridGame.ini"), "game");
                temp.WriteText("Documents/" + Ini.Replace("GridEngine.ini", "GridInput.ini"), "input");
                var sourceIni = temp.WriteText("Documents/" + Ini, "[SystemSettings]\nResX=1280");
                var sourceSave = temp.Write("Documents/Disney Interactive Studios/Tron Evolution/SaveData/profile/Save/CHECKPT", new byte[] { 1, 2 });
                var backend = new PatcherBackend(temp.Root, Path.Combine(temp.Root, "Documents"));
                Assert.Throws<IOException>(() => backend.ApplySettings(directory, new GameSettings(), false, true, false, false));
                Assert.That(backend.Load(directory).Status.UserDirectoryRedirected, Is.False);
                backend.CopyProfile(directory, true);
                Assert.That(backend.Load(directory).Status.UserDirectoryRedirected, Is.False, "Copying files must not change binary patches.");
                backend.ApplySettings(directory, new GameSettings(), false, true, false, false);
                Assert.That(backend.Load(directory).Status.UserDirectoryRedirected, Is.True);
                Assert.That(File.ReadAllText(sourceIni), Does.Contain("ResX=1280"));
                Assert.That(File.ReadAllBytes(sourceSave), Is.EqualTo(new byte[] { 1, 2 }));
            }
        }

        [Test]
        public void GameCreatedPortableConfigsAllowApplyWithoutAnyCheckpointOrSaveDirectory()
        {
            using (var temp = new TemporaryDirectory())
            {
                var bytes = PortableExecutablePatcher.Apply(PortablePeFixture.Create(), null, true);
                var exe = temp.Write("Game/Binaries/Win32Live/GridGame.exe", bytes);
                foreach (var name in new[] { "GridEngine.ini", "GridGame.ini", "GridInput.ini" })
                    temp.WriteText("Game/UserData/" + Ini.Replace("GridEngine.ini", name), "[SystemSettings]\nResX=1280");
                var backend = new PatcherBackend(temp.Root, Path.Combine(temp.Root, "Documents"));
                var directory = Path.GetDirectoryName(exe);
                Assert.That(backend.Load(directory).PortableProfile.HasCheckpoint, Is.False);
                Assert.DoesNotThrow(() => backend.ApplySettings(directory, new GameSettings(), false, true, false, false));
                Assert.That(File.ReadAllText(Path.Combine(temp.Root, "Game/UserData/" + Ini)), Does.Contain("ResX=1920"));
                Assert.That(Directory.Exists(Path.Combine(temp.Root, "Game/UserData/Disney Interactive Studios/Tron Evolution/SaveData")), Is.False);
            }
        }

        [Test]
        public void ExecutableContainsCompleteProjectAndDxvkLicenseNotices()
        {
            var assembly = typeof(PatcherBackend).Assembly;
            foreach (var entry in new[] {
                new[] { "TronEvolutionPatcher.Licenses.MIT.txt", "LICENSE.txt" },
                new[] { "TronEvolutionPatcher.Licenses.DXVK.txt", Path.Combine("Assets", "LICENSE.dxvk.txt") }
            })
            {
                using (var resource = assembly.GetManifestResourceStream(entry[0]))
                {
                    Assert.That(resource, Is.Not.Null, entry[0]);
                    using (var reader = new StreamReader(resource))
                        Assert.That(reader.ReadToEnd(), Is.EqualTo(File.ReadAllText(
                            Path.Combine(TestContext.CurrentContext.TestDirectory, entry[1]))));
                }
            }
        }

        [Test]
        public void PackagedPayloadMatchesReferenceAndPassesValidator()
        {
            var assets = Path.Combine(TestContext.CurrentContext.TestDirectory, "Assets");
            var bytes = File.ReadAllBytes(Path.Combine(assets, "vk09.dll"));
            Assert.That(PortableExecutablePatcherTests.Hash(bytes), Is.EqualTo("265888c31ca78dffa290c39cb7e50bfb02762590e41927906e46fb32f01497fa"));
            Assert.DoesNotThrow(() => DxvkPayloadValidator.Validate(bytes));
            Assert.That(File.ReadAllText(Path.Combine(assets, "LICENSE.dxvk.txt")), Does.Contain("zlib/libpng"));
            Assert.That(File.Exists(Path.Combine(TestContext.CurrentContext.TestDirectory, "LICENSE.txt")), Is.True);
        }

        [TestCase("2560x1440 (QHD)", "144 FPS", 2560, 144, true)]
        [TestCase("3840×2160", "Без лимита", 3840, 200, false)]
        [TestCase("1920х1080", "0 FPS", 1920, 200, false)]
        [TestCase("1600 X 900", "120", 1600, 120, true)]
        public void ParsesPresetAndCustomUiValues(string resolution, string fps, int width, int limit, bool smooth)
        {
            var settings = SettingsTextParser.Parse(resolution, fps, "int", false, true);
            Assert.That(settings.ResolutionWidth, Is.EqualTo(width));
            Assert.That(settings.FpsLimit, Is.EqualTo(limit));
            Assert.That(settings.SmoothFrameRate, Is.EqualTo(smooth));
            Assert.That(settings.Language, Is.EqualTo("INT"));
        }

        [TestCase("0x1080", "62", "RUS")]
        [TestCase("1920x-1", "62", "RUS")]
        [TestCase("1920x1080", "-1", "RUS")]
        [TestCase("1920x1080", "9999999999999999", "RUS")]
        [TestCase("1920x1080", "62", "INT\nResX=1")]
        public void InvalidUiValuesCannotReachFileWriting(string resolution, string fps, string language)
        {
            Assert.Throws<ArgumentException>(() => SettingsTextParser.Parse(resolution, fps, language, false, true));
        }
    }
}

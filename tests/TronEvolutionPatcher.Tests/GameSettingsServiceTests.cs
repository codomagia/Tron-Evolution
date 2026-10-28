using System.Globalization;
using System.IO;
using NUnit.Framework;
using TronEvolutionPatcher.Services;

namespace TronEvolutionPatcher.Tests
{
    [TestFixture]
    public class GameSettingsServiceTests
    {
        private const string IniSuffix = "Disney Interactive Studios/Tron Evolution/UnrealEngine3/GridGame/Config/GridEngine.ini";

        [Test]
        public void MissingIniReturnsOriginalDefaultsWithoutCreatingFiles()
        {
            using (var temp = new TemporaryDirectory())
            {
                var service = new GameSettingsService(temp.Root);
                var settings = service.Read();
                Assert.Multiple(() =>
                {
                    Assert.That(settings.ResolutionWidth, Is.EqualTo(1920));
                    Assert.That(settings.ResolutionHeight, Is.EqualTo(1080));
                    Assert.That(settings.MotionBlur, Is.False);
                    Assert.That(settings.VSync, Is.True);
                    Assert.That(settings.Language, Is.EqualTo("RUS"));
                    Assert.That(settings.FpsLimit, Is.EqualTo(62));
                    Assert.That(settings.SmoothFrameRate, Is.True);
                    Assert.That(Directory.GetFileSystemEntries(temp.Root), Is.Empty);
                });
            }
        }

        [Test]
        public void ParsesAllSettingsOnlyInTheirSections()
        {
            var settings = new GameSettingsService(Path.GetTempPath()).Parse(new StringReader(
                "ResX=1\n[Other]\nResY=1\nLanguage=BAD\n# ResX=1\n" +
                "[SystemSettings]\nResX=2560\nResY=1440\nMotionBlur=1\nUseVsync=False\n" +
                "[Engine.Engine]\nLanguage=int\nbSmoothFrameRate=0\nMaxSmoothedFrameRate=144.9\n"));
            Assert.Multiple(() =>
            {
                Assert.That(settings.ResolutionWidth, Is.EqualTo(2560));
                Assert.That(settings.ResolutionHeight, Is.EqualTo(1440));
                Assert.That(settings.MotionBlur, Is.True);
                Assert.That(settings.VSync, Is.False);
                Assert.That(settings.Language, Is.EqualTo("INT"));
                Assert.That(settings.FpsLimit, Is.EqualTo(144));
                Assert.That(settings.SmoothFrameRate, Is.False);
            });
        }

        [Test]
        public void AcceptsWhitespaceCaseAndLastValidDuplicate()
        {
            var settings = new GameSettingsService(Path.GetTempPath()).Parse(new StringReader(
                "; comment\n[ SYSTEMSETTINGS ]\n resx = 2560\nResX=3840\nresy = 2160\n" +
                "usevsync = 0\nmotionblur = TRUE\n[ ENGINE.ENGINE ]\n language = int\n"));
            Assert.That(settings.ResolutionWidth, Is.EqualTo(3840));
            Assert.That(settings.ResolutionHeight, Is.EqualTo(2160));
            Assert.That(settings.VSync, Is.False);
            Assert.That(settings.MotionBlur, Is.True);
            Assert.That(settings.Language, Is.EqualTo("INT"));
        }

        [TestCase("garbage")]
        [TestCase("-1")]
        [TestCase("0")]
        [TestCase("99999999999999999999999")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        public void MalformedValuesDoNotOverwriteDefaults(string value)
        {
            var settings = new GameSettingsService(Path.GetTempPath()).Parse(new StringReader(
                "[SystemSettings]\nResX=" + value + "\nResY=" + value +
                "\nUseVsync=invalid\n[Engine.Engine]\nLanguage=\nMaxSmoothedFrameRate=" + value + "\nbSmoothFrameRate=invalid"));
            Assert.That(settings.ResolutionWidth, Is.EqualTo(1920));
            Assert.That(settings.ResolutionHeight, Is.EqualTo(1080));
            Assert.That(settings.FpsLimit, Is.EqualTo(62));
            Assert.That(settings.Language, Is.EqualTo("RUS"));
            Assert.That(settings.VSync, Is.True);
            Assert.That(settings.SmoothFrameRate, Is.True);
        }

        [Test]
        public void FpsDecimalUsesInvariantCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
                var settings = new GameSettingsService(Path.GetTempPath()).Parse(new StringReader(
                    "[Engine.Engine]\nMaxSmoothedFrameRate=165.75"));
                Assert.That(settings.FpsLimit, Is.EqualTo(165));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Test]
        public void ExistingPortableIniHasPriorityOverDocumentsAndRemainsUnchanged()
        {
            using (var temp = new TemporaryDirectory())
            {
                var win32 = Path.GetDirectoryName(temp.Write("Game/Binaries/Win32Live/GridGame.exe", new byte[0]));
                var portable = temp.WriteText("Game/UserData/" + IniSuffix, "[SystemSettings]\nResX=3840");
                var documents = temp.WriteText("Documents/" + IniSuffix, "[SystemSettings]\nResX=1280");
                var original = File.ReadAllBytes(portable);
                var service = new GameSettingsService(Path.Combine(temp.Root, "Documents"));
                Assert.That(service.GetIniPath(win32), Is.EqualTo(portable));
                Assert.That(service.Read(win32).ResolutionWidth, Is.EqualTo(3840));
                Assert.That(File.ReadAllBytes(portable), Is.EqualTo(original));
                Assert.That(File.ReadAllText(documents), Does.Contain("1280"));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MissingPortableIniUsesPatchStateToSelectLocation(bool redirected)
        {
            using (var temp = new TemporaryDirectory())
            {
                var exe = temp.Write("Game/Binaries/Win32Live/GridGame.exe", new byte[0]);
                if (redirected)
                {
                    using (var file = File.OpenWrite(exe))
                    {
                        file.Position = 0xC249;
                        file.Write(new byte[] { 0xE8, 0x02, 0x48, 0x7A, 0x01, 0x90 }, 0, 6);
                        file.Position = 0x1139623;
                        file.Write(new byte[] { 0xE8, 0x28, 0x74, 0x67, 0x00, 0x90 }, 0, 6);
                    }
                }
                var documentsRoot = Path.Combine(temp.Root, "Documents");
                var documents = temp.WriteText("Documents/" + IniSuffix, "[SystemSettings]\nResX=1280");
                var service = new GameSettingsService(documentsRoot);
                var expected = redirected ? Path.Combine(temp.Root, "Game", "UserData", IniSuffix.Replace('/', '\\')) : documents;
                Assert.That(service.GetIniPath(Path.GetDirectoryName(exe)), Is.EqualTo(expected));
                Assert.That(service.Read(Path.GetDirectoryName(exe)).ResolutionWidth, Is.EqualTo(redirected ? 1920 : 1280));
                Assert.That(Directory.Exists(Path.Combine(temp.Root, "Game", "UserData")), Is.False);
            }
        }

        [Test]
        public void LockedSettingsFileReportsIoErrorInsteadOfPretendingDefaultsWereRead()
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = temp.WriteText(IniSuffix, "[SystemSettings]\nResX=2560");
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Assert.Throws<IOException>(() => new GameSettingsService(temp.Root).Read());
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TronEvolutionPatcher.Models;
using TronEvolutionPatcher.Patching;
using TronEvolutionPatcher.Services;

namespace TronEvolutionPatcher.Tests
{
    [TestFixture]
    public class GameSettingsWriteTests
    {
        private const string IniSuffix = "Disney Interactive Studios/Tron Evolution/UnrealEngine3/GridGame/Config/GridEngine.ini";
        private const string Template = "; Комментарий\n[SystemSettings]\nResX=1280\nResY=720\nMotionBlur=True\nUseVsync=True\nCustomSetting=unchanged\n" +
            "[Engine.Engine]\nLanguage=RUS\nbSmoothFrameRate=TRUE\nMaxSmoothedFrameRate=62\n[Other]\nResX=123\n";

        private static GameSettings Desired()
        {
            return new GameSettings
            {
                ResolutionWidth = 2560, ResolutionHeight = 1440, MotionBlur = false,
                VSync = false, Language = " int ", FpsLimit = 165, SmoothFrameRate = false
            };
        }

        private static string Documents(TemporaryDirectory temp) { return Path.Combine(temp.Root, "Documents"); }
        private static string Existing(TemporaryDirectory temp, string text = Template)
        {
            return temp.WriteText("Documents/" + IniSuffix, text);
        }

        private static void AssertSettings(GameSettings settings)
        {
            Assert.Multiple(() =>
            {
                Assert.That(settings.ResolutionWidth, Is.EqualTo(2560));
                Assert.That(settings.ResolutionHeight, Is.EqualTo(1440));
                Assert.That(settings.MotionBlur, Is.False);
                Assert.That(settings.VSync, Is.False);
                Assert.That(settings.Language, Is.EqualTo("INT"));
                Assert.That(settings.FpsLimit, Is.EqualTo(165));
                Assert.That(settings.SmoothFrameRate, Is.False);
            });
        }

        public enum Failure { InstallBefore, InstallAfter, ProtectBefore, ProtectAfter }

        private sealed class FaultMutation : IFileMutation
        {
            private readonly FileMutation actual = new FileMutation();
            private readonly Failure failure;
            private bool failed;
            public bool FailRollback;
            public int Calls;

            public FaultMutation(Failure failure) { this.failure = failure; }

            public void Install(string stagedFile, string destination, bool replace)
            {
                Calls++;
                if (failed && FailRollback) throw new IOException("Simulated rollback failure");
                if (!failed && (failure == Failure.InstallBefore || failure == Failure.InstallAfter))
                {
                    failed = true;
                    if (failure == Failure.InstallAfter) actual.Install(stagedFile, destination, replace);
                    throw new IOException("Simulated settings installation failure");
                }
                actual.Install(stagedFile, destination, replace);
            }

            public void Delete(string path) { Calls++; actual.Delete(path); }

            public void SetAttributes(string path, FileAttributes attributes)
            {
                Calls++;
                if (!failed && (attributes & FileAttributes.ReadOnly) != 0 &&
                    (failure == Failure.ProtectBefore || failure == Failure.ProtectAfter))
                {
                    failed = true;
                    if (failure == Failure.ProtectAfter) actual.SetAttributes(path, attributes);
                    throw new IOException("Simulated settings protection failure");
                }
                actual.SetAttributes(path, attributes);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AppliesEverySettingPreservesOtherContentAndProtectsFile(bool readOnly)
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = Existing(temp);
                if (readOnly) File.SetAttributes(path, FileAttributes.ReadOnly | FileAttributes.Hidden);
                var service = new GameSettingsService(Documents(temp));
                var input = Desired();
                var result = service.Apply(input);
                AssertSettings(service.Read());
                Assert.That(result.IniPath, Is.EqualTo(path));
                Assert.That(result.ContentChanged, Is.True);
                Assert.That(result.CreatedFromDefault, Is.False);
                Assert.That(result.RecoveryDirectory, Is.Null);
                Assert.That(File.ReadAllText(path), Does.StartWith("; Комментарий\n"));
                Assert.That(File.ReadAllText(path), Does.Contain("CustomSetting=unchanged\n[Engine.Engine]"));
                Assert.That(File.ReadAllText(path), Does.EndWith("[Other]\nResX=123\n"));
                Assert.That(File.GetAttributes(path) & FileAttributes.ReadOnly, Is.Not.Zero);
                if (readOnly) Assert.That(File.GetAttributes(path) & FileAttributes.Hidden, Is.Not.Zero);
                Assert.That(input.Language, Is.EqualTo(" int "), "Normalization must not mutate the caller's model.");
            }
        }

        [TestCase("")]
        [TestCase("[Other]\nResX=42")]
        [TestCase("[SystemSettings]\nResX=1\n[Other]\nLanguage=DEU")]
        [TestCase("[Engine.Engine]\nLanguage=RUS\n[Other]\nResY=9")]
        public void InsertsMissingSectionsAndKeysInCorrectSections(string original)
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = Existing(temp, original);
                var service = new GameSettingsService(Documents(temp));
                service.Apply(Desired());
                AssertSettings(service.Read());
                var text = File.ReadAllText(path);
                using (var reader = new StringReader(text))
                {
                    var entries = IniEntries.Read(reader).ToList();
                    Assert.That(entries.Count(entry => entry.Section == "systemsettings" && entry.Key == "resx"), Is.EqualTo(1));
                    Assert.That(entries.Count(entry => entry.Section == "engine.engine" && entry.Key == "language"), Is.EqualTo(1));
                }
                if (original.Contains("ResX=42")) Assert.That(text, Does.Contain("[Other]\nResX=42"));
                if (original.Contains("Language=DEU")) Assert.That(text, Does.Contain("[Other]\nLanguage=DEU"));
            }
        }

        [Test]
        public void RepeatedSectionsAndDuplicateKeysReceiveConsistentValues()
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = Existing(temp, "[SystemSettings]\nResX=1\nresx=2\n[Other]\nResX=3\n[ SYSTEMSETTINGS ]\nResY=4\n[Last]\nKeep=True");
                var service = new GameSettingsService(Documents(temp));
                service.Apply(Desired());
                AssertSettings(service.Read());
                using (var reader = new StringReader(File.ReadAllText(path)))
                {
                    var entries = IniEntries.Read(reader).ToList();
                    Assert.That(entries.Where(entry => entry.Section == "systemsettings" && entry.Key == "resx")
                        .Select(entry => entry.Value), Is.EqualTo(new[] { "2560", "2560" }));
                    Assert.That(entries.Single(entry => entry.Section == "systemsettings" && entry.Key == "usevsync").Value, Is.EqualTo("False"));
                    Assert.That(entries.Single(entry => entry.Section == "other" && entry.Key == "resx").Value, Is.EqualTo("3"));
                    Assert.That(entries.Single(entry => entry.Section == "last" && entry.Key == "keep").Value, Is.EqualTo("True"));
                }
            }
        }

        [Test]
        public void PreservesSpacingKeyCaseAndInlineComments()
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = Existing(temp, "[ SystemSettings ]\n  resx = 1280  ; размер\nResY=720# высота\n" +
                    "MotionBlur= ; пусто\nUseVsync=1   \n[Engine.Engine]\nLanguage=rus # язык\n");
                var service = new GameSettingsService(Documents(temp));
                service.Apply(Desired());
                AssertSettings(service.Read());
                var text = File.ReadAllText(path);
                Assert.That(text, Does.Contain("  resx = 2560  ; размер\n"));
                Assert.That(text, Does.Contain("ResY=1440# высота\n"));
                Assert.That(text, Does.Contain("MotionBlur= False; пусто\n"));
                Assert.That(text, Does.Contain("UseVsync=False   \n"));
                Assert.That(text, Does.Contain("Language=INT # язык\n"));
            }
        }

        [TestCase("\n", false)]
        [TestCase("\r\n", true)]
        [TestCase("\r", false)]
        public void PreservesExistingLineEndingsAndFinalNewline(string newline, bool finalNewline)
        {
            using (var temp = new TemporaryDirectory())
            {
                var source = Template.Replace("\n", newline);
                if (!finalNewline) source = source.Substring(0, source.Length - newline.Length);
                var path = Existing(temp, source);
                new GameSettingsService(Documents(temp)).Apply(Desired());
                var updated = File.ReadAllText(path);
                Assert.That(updated.Count(value => value == '\n'), Is.EqualTo(source.Count(value => value == '\n')));
                Assert.That(updated.Count(value => value == '\r'), Is.EqualTo(source.Count(value => value == '\r')));
                Assert.That(updated.EndsWith(newline), Is.EqualTo(finalNewline));
            }
        }

        [TestCase("utf8")]
        [TestCase("utf8-bom")]
        [TestCase("utf16le")]
        [TestCase("utf16be")]
        [TestCase("utf32le")]
        [TestCase("utf32be")]
        public void PreservesUnicodeEncodingAndPreamble(string kind)
        {
            using (var temp = new TemporaryDirectory())
            {
                Encoding encoding = kind == "utf8" ? new UTF8Encoding(false) : kind == "utf8-bom" ? new UTF8Encoding(true) :
                    kind == "utf16le" ? new UnicodeEncoding(false, true) : kind == "utf16be" ? new UnicodeEncoding(true, true) :
                    kind == "utf32le" ? (Encoding)new UTF32Encoding(false, true) : new UTF32Encoding(true, true);
                var path = temp.Write("Documents/" + IniSuffix, encoding.GetPreamble().Concat(encoding.GetBytes(Template)).ToArray());
                var service = new GameSettingsService(Documents(temp));
                service.Apply(Desired());
                var actual = File.ReadAllBytes(path);
                Assert.That(actual.Take(encoding.GetPreamble().Length), Is.EqualTo(encoding.GetPreamble()));
                var expected = Template.Replace("ResX=1280", "ResX=2560").Replace("ResY=720", "ResY=1440")
                    .Replace("MotionBlur=True", "MotionBlur=False").Replace("UseVsync=True", "UseVsync=False")
                    .Replace("Language=RUS", "Language=INT").Replace("bSmoothFrameRate=TRUE", "bSmoothFrameRate=FALSE")
                    .Replace("MaxSmoothedFrameRate=62", "MaxSmoothedFrameRate=165");
                Assert.That(actual, Is.EqualTo(encoding.GetPreamble().Concat(encoding.GetBytes(expected)).ToArray()));
                AssertSettings(service.Read());
            }
        }

        [Test]
        public void RepeatedApplicationDoesNotRewriteProtectedFile()
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = Existing(temp);
                var service = new GameSettingsService(Documents(temp));
                service.Apply(Desired());
                var before = File.ReadAllBytes(path);
                var time = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetAttributes(path, FileAttributes.Normal);
                File.SetLastWriteTimeUtc(path, time);
                File.SetAttributes(path, FileAttributes.ReadOnly);
                var result = service.Apply(Desired());
                Assert.That(result.ContentChanged, Is.False);
                Assert.That(result.CreatedFromDefault, Is.False);
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
                Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(time));
                Assert.That(Directory.GetDirectories(Path.GetDirectoryName(path)), Is.Empty);
            }
        }

        [Test]
        public void MatchingWritableFileGetsProtectionWithoutContentRewrite()
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = Existing(temp);
                var service = new GameSettingsService(Documents(temp));
                service.Apply(Desired());
                File.SetAttributes(path, FileAttributes.Normal);
                var time = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(path, time);
                Assert.That(service.Apply(Desired()).ContentChanged, Is.False);
                Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(time));
                Assert.That(File.GetAttributes(path) & FileAttributes.ReadOnly, Is.Not.Zero);
            }
        }

        [Test]
        public void MissingIniIsRejectedEvenWhenDefaultTemplateExists()
        {
            using (var temp = new TemporaryDirectory())
            {
                var exe = temp.Write("Game/Binaries/Win32Live/GridGame.exe", new byte[0]);
                var template = temp.WriteText("Game/GridGame/Config/DefaultEngine.ini", Template);
                File.SetAttributes(template, FileAttributes.ReadOnly);
                var before = File.ReadAllBytes(template);
                var service = new GameSettingsService(Documents(temp));
                Assert.Throws<FileNotFoundException>(() => service.Apply(Desired(), Path.GetDirectoryName(exe)));
                Assert.That(Directory.Exists(Documents(temp)), Is.False);
                Assert.That(File.ReadAllBytes(template), Is.EqualTo(before));
                Assert.That(File.GetAttributes(template) & FileAttributes.ReadOnly, Is.Not.Zero);
            }
        }

        [Test]
        public void PortableIniTakesPriorityAndDocumentsRemainUnchanged()
        {
            using (var temp = new TemporaryDirectory())
            {
                var exe = temp.Write("Game/Binaries/Win32Live/GridGame.exe", new byte[0]);
                var path = temp.WriteText("Game/UserData/" + IniSuffix, Template);
                var documents = Existing(temp);
                var before = File.ReadAllBytes(documents);
                var service = new GameSettingsService(Documents(temp));
                Assert.That(service.Apply(Desired(), Path.GetDirectoryName(exe)).IniPath, Is.EqualTo(path));
                AssertSettings(service.Read(Path.GetDirectoryName(exe)));
                Assert.That(File.ReadAllBytes(documents), Is.EqualTo(before));
                Assert.That(File.ReadAllBytes(exe), Is.Empty);
            }
        }

        [Test]
        public void MissingTemplateDoesNotCreateDirectories()
        {
            using (var temp = new TemporaryDirectory())
            {
                var exe = temp.Write("Game/Binaries/Win32Live/GridGame.exe", new byte[0]);
                var service = new GameSettingsService(Documents(temp));
                Assert.Throws<FileNotFoundException>(() => service.Apply(Desired(), Path.GetDirectoryName(exe)));
                Assert.That(Directory.Exists(Documents(temp)), Is.False);
            }
        }

        [TestCase("width")]
        [TestCase("height")]
        [TestCase("fps")]
        [TestCase("language")]
        [TestCase("injection")]
        [TestCase("null-language")]
        [TestCase("null-model")]
        public void InvalidSettingsRejectBeforeChangingFileOrAttributes(string kind)
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = Existing(temp);
                File.SetAttributes(path, FileAttributes.ReadOnly);
                var original = File.ReadAllBytes(path);
                var input = Desired();
                if (kind == "width") input.ResolutionWidth = 0;
                if (kind == "height") input.ResolutionHeight = -1;
                if (kind == "fps") input.FpsLimit = 0;
                if (kind == "language") input.Language = "INVALID";
                if (kind == "injection") input.Language = "INT\nUseVsync=True";
                if (kind == "null-language") input.Language = null;
                if (kind == "null-model") input = null;
                Assert.That(() => new GameSettingsService(Documents(temp)).Apply(input), Throws.InstanceOf<ArgumentException>());
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
                Assert.That(File.GetAttributes(path) & FileAttributes.ReadOnly, Is.Not.Zero);
                Assert.That(Directory.GetDirectories(Path.GetDirectoryName(path)), Is.Empty);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LockedIniRejectsWithoutChangingAttributes(bool readOnly)
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = Existing(temp);
                if (readOnly) File.SetAttributes(path, FileAttributes.ReadOnly);
                var attributes = File.GetAttributes(path);
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    Assert.Throws<IOException>(() => new GameSettingsService(Documents(temp)).Apply(Desired()));
                Assert.That(File.ReadAllText(path), Is.EqualTo(Template));
                Assert.That(File.GetAttributes(path), Is.EqualTo(attributes));
            }
        }

        [TestCase("invalid-utf8")]
        [TestCase("utf16-without-bom")]
        public void UnsupportedEncodingRejectsWithoutDataLoss(string kind)
        {
            using (var temp = new TemporaryDirectory())
            {
                var bytes = kind == "invalid-utf8" ? new byte[] { 0x80, 0xFF, 0x81 } : new UnicodeEncoding(false, false).GetBytes(Template);
                var path = temp.Write("Documents/" + IniSuffix, bytes);
                File.SetAttributes(path, FileAttributes.ReadOnly);
                Assert.Throws<InvalidDataException>(() => new GameSettingsService(Documents(temp)).Apply(Desired()));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
                Assert.That(File.GetAttributes(path) & FileAttributes.ReadOnly, Is.Not.Zero);
            }
        }

        [TestCase(Failure.InstallBefore, false)]
        [TestCase(Failure.InstallAfter, false)]
        [TestCase(Failure.ProtectBefore, false)]
        [TestCase(Failure.ProtectAfter, false)]
        [TestCase(Failure.InstallBefore, true)]
        [TestCase(Failure.InstallAfter, true)]
        [TestCase(Failure.ProtectBefore, true)]
        [TestCase(Failure.ProtectAfter, true)]
        public void FailureRestoresContentAttributesAndTimestamp(Failure failure, bool readOnly)
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = Existing(temp);
                var before = File.ReadAllBytes(path);
                var time = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(path, time);
                if (readOnly) File.SetAttributes(path, FileAttributes.ReadOnly | FileAttributes.Hidden);
                var attributes = File.GetAttributes(path);
                var service = new GameSettingsService(Documents(temp), new FaultMutation(failure));
                Assert.Throws<IOException>(() => service.Apply(Desired()));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
                Assert.That(File.GetAttributes(path), Is.EqualTo(attributes));
                Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(time));
                Assert.That(Directory.GetDirectories(Path.GetDirectoryName(path)), Is.Empty);
            }
        }

        [TestCase(Failure.InstallBefore)]
        [TestCase(Failure.InstallAfter)]
        [TestCase(Failure.ProtectAfter)]
        public void MissingIniIsRejectedBeforeAnyFileMutation(Failure failure)
        {
            using (var temp = new TemporaryDirectory())
            {
                var exe = temp.Write("Game/Binaries/Win32Live/GridGame.exe", new byte[0]);
                temp.WriteText("Game/GridGame/Config/DefaultEngine.ini", Template);
                Directory.CreateDirectory(Documents(temp));
                var mutation = new FaultMutation(failure);
                var service = new GameSettingsService(Documents(temp), mutation);
                Assert.Throws<FileNotFoundException>(() => service.Apply(Desired(), Path.GetDirectoryName(exe)));
                Assert.That(mutation.Calls, Is.Zero);
                Assert.That(Directory.Exists(Documents(temp)), Is.True);
                Assert.That(Directory.GetFileSystemEntries(Documents(temp)), Is.Empty);
                Assert.That(File.ReadAllText(Path.Combine(temp.Root, "Game/GridGame/Config/DefaultEngine.ini")), Is.EqualTo(Template));
            }
        }

        [Test]
        public void FailedRollbackRetainsOriginalIniAndRecoveryManifest()
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = Existing(temp);
                var original = File.ReadAllBytes(path);
                var service = new GameSettingsService(Documents(temp), new FaultMutation(Failure.ProtectBefore) { FailRollback = true });
                var error = Assert.Throws<PatchTransactionException>(() => service.Apply(Desired()));
                Assert.That(File.ReadAllBytes(Path.Combine(error.RecoveryDirectory, "original-0.bin")), Is.EqualTo(original));
                Assert.That(File.ReadAllText(Path.Combine(error.RecoveryDirectory, "recovery.txt")), Does.Contain(path));
            }
        }
    }
}

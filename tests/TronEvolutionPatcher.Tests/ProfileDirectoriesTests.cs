using System.IO;
using NUnit.Framework;
using TronEvolutionPatcher.Services;

namespace TronEvolutionPatcher.Tests
{
    [TestFixture]
    public class ProfileDirectoriesTests
    {
        [Test]
        public void MigrationRejectsDestinationNestedInsideSourceProfile()
        {
            using (var temp = new TemporaryDirectory())
            {
                var documents = Path.Combine(temp.Root, "Documents");
                var gameRoot = Path.Combine(documents, "Disney Interactive Studios", "Tron Evolution", "SaveData", "Game");
                Directory.CreateDirectory(gameRoot);
                Assert.Throws<IOException>(() => new PortableProfileMigration().Prepare(documents, gameRoot));
                Assert.That(Directory.GetFileSystemEntries(gameRoot), Is.Empty);
            }
        }

        [Test]
        public void ProfilePathCannotEscapeItsRoot()
        {
            using (var temp = new TemporaryDirectory())
                Assert.Throws<IOException>(() => ProfileDirectories.ValidateChain(temp.Root, Path.Combine(temp.Root, "..", "outside")));
        }
    }
}

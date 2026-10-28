using System.IO;
using NUnit.Framework;
using TronEvolutionPatcher.Services;

namespace TronEvolutionPatcher.Tests
{
    [TestFixture]
    public class GameDirectoryServiceTests
    {
        [TestCase("root")]
        [TestCase("binaries")]
        [TestCase("exe")]
        public void ResolvesSupportedInputPaths(string input)
        {
            using (var temp = new TemporaryDirectory())
            {
                var exe = temp.Write("Игра с пробелами/Binaries/Win32Live/GridGame.exe", new byte[0]);
                var binaries = Path.GetDirectoryName(exe);
                var root = Path.Combine(temp.Root, "Игра с пробелами");
                var path = input == "root" ? root : input == "exe" ? exe : binaries;
                Assert.That(new GameDirectoryService().Resolve(path), Is.EqualTo(binaries));
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("bad\0path")]
        public void InvalidInputReturnsNoGame(string input)
        {
            Assert.That(new GameDirectoryService().Resolve(input), Is.Null);
        }

        [Test]
        public void RejectsOtherExecutablesAndDirectoriesNamedGridGame()
        {
            using (var temp = new TemporaryDirectory())
            {
                var other = temp.Write("Other.exe", new byte[0]);
                Directory.CreateDirectory(Path.Combine(temp.Root, "GridGame.exe"));
                var service = new GameDirectoryService();
                Assert.That(service.Resolve(other), Is.Null);
                Assert.That(service.Resolve(temp.Root), Is.Null);
                Assert.That(service.Resolve(Path.Combine(temp.Root, "missing")), Is.Null);
            }
        }

        [Test]
        public void DiscoverySkipsInvalidCandidatesAndUsesFirstMatch()
        {
            using (var temp = new TemporaryDirectory())
            {
                var first = Path.GetDirectoryName(temp.Write("first/GridGame.exe", new byte[0]));
                var second = Path.GetDirectoryName(temp.Write("second/GridGame.exe", new byte[0]));
                var service = new GameDirectoryService();
                Assert.That(service.Discover(new[] { null, Path.Combine(temp.Root, "missing"), first, second }), Is.EqualTo(first));
                Assert.That(service.Discover(new string[0]), Is.Null);
            }
        }
    }
}

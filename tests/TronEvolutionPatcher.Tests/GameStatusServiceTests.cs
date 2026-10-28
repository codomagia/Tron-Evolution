using System.IO;
using NUnit.Framework;
using TronEvolutionPatcher.Models;
using TronEvolutionPatcher.Services;

namespace TronEvolutionPatcher.Tests
{
    [TestFixture]
    public class GameStatusServiceTests
    {
        [Test]
        public void MissingGameIsNotFoundEvenIfVulkanDllExists()
        {
            using (var temp = new TemporaryDirectory())
            {
                temp.Write("vk09.dll", new byte[0]);
                var status = new GameStatusService().Read(temp.Root);
                Assert.That(status.Renderer, Is.EqualTo(RendererState.NotFound));
                Assert.That(status.ExeExists, Is.False);
                Assert.That(status.VulkanDllExists, Is.True);
            }
        }

        [TestCase("d3d9.dll", RendererState.DirectX9)]
        [TestCase("vk09.dll", RendererState.Vulkan)]
        [TestCase("kernel32.dll", RendererState.Unknown)]
        public void UsesImportToDetermineRendererAndDoesNotModifyFiles(string library, RendererState expected)
        {
            using (var temp = new TemporaryDirectory())
            {
                var bytes = PeFixture.Create(library);
                var exe = temp.Write("GridGame.exe", bytes);
                var status = new GameStatusService().Read(temp.Root);
                Assert.That(status.Renderer, Is.EqualTo(expected));
                Assert.That(status.ImportedLibrary, Is.EqualTo(expected == RendererState.Unknown ? null : library));
                Assert.That(status.Error, Is.Null);
                Assert.That(status.BackupExists, Is.False);
                Assert.That(File.ReadAllBytes(exe), Is.EqualTo(bytes));
                Assert.That(Directory.GetFiles(temp.Root).Length, Is.EqualTo(1));
            }
        }

        [Test]
        public void CorruptExeReturnsUnknownWithError()
        {
            using (var temp = new TemporaryDirectory())
            {
                temp.WriteText("GridGame.exe", "d3d9.dll");
                var status = new GameStatusService().Read(temp.Root);
                Assert.That(status.Renderer, Is.EqualTo(RendererState.Unknown));
                Assert.That(status.Error, Is.Not.Empty);
            }
        }

        [Test]
        public void LockedExeReturnsUnknownWithError()
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = temp.Write("GridGame.exe", PeFixture.Create());
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var status = new GameStatusService().Read(temp.Root);
                    Assert.That(status.Renderer, Is.EqualTo(RendererState.Unknown));
                    Assert.That(status.Error, Is.Not.Empty);
                }
            }
        }

        [TestCase("none", "0", false, false)]
        [TestCase("off", "false", false, false)]
        [TestCase("FALSE", "off", false, false)]
        [TestCase("0", "", false, false)]
        [TestCase("fps,devinfo,api", "16", true, true)]
        [TestCase("fps", "0", true, false)]
        public void ReadsDxvkOptionsAndArtifactPresence(string hud, string anisotropy, bool expectedHud, bool expectedAnisotropy)
        {
            using (var temp = new TemporaryDirectory())
            {
                temp.Write("GridGame.exe", PeFixture.Create());
                temp.Write(GameStatusService.BackupName, new byte[0]);
                temp.Write("vk09.dll", new byte[0]);
                temp.WriteText("dxvk.conf", "# dxvk.hud = fps\n; d3d9.samplerAnisotropy=16\n" +
                    "dxvk.hud.extra = fps\ndxvk.hud = " + hud + "\nd3d9.samplerAnisotropy = " + anisotropy);
                var status = new GameStatusService().Read(temp.Root);
                Assert.That(status.HudEnabled, Is.EqualTo(expectedHud));
                Assert.That(status.AnisotropyEnabled, Is.EqualTo(expectedAnisotropy));
                Assert.That(status.BackupExists, Is.True);
                Assert.That(status.VulkanDllExists, Is.True);
                Assert.That(status.DxvkConfigExists, Is.True);
                Assert.That(status.Renderer, Is.EqualTo(RendererState.DirectX9));
            }
        }

        [Test]
        public void LockedDxvkConfigPreservesRendererAndReportsError()
        {
            using (var temp = new TemporaryDirectory())
            {
                temp.Write("GridGame.exe", PeFixture.Create("vk09.dll"));
                var path = temp.WriteText("dxvk.conf", "dxvk.hud=fps");
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var status = new GameStatusService().Read(temp.Root);
                    Assert.That(status.Renderer, Is.EqualTo(RendererState.Vulkan));
                    Assert.That(status.Error, Is.Not.Empty);
                }
            }
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, true)]
        public void PortableRedirectionRequiresBothCallSites(bool patchA, bool patchB, bool expected)
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = temp.Write("GridGame.exe", PeFixture.Create());
                using (var file = File.OpenWrite(path))
                {
                    file.Position = 0x11383E0;
                    file.Write(new byte[] { 0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3 }, 0, 6);
                    if (patchA)
                    {
                        file.Position = 0xC249;
                        file.Write(new byte[] { 0xE8, 0x02, 0x48, 0x7A, 0x01, 0x90 }, 0, 6);
                    }
                    if (patchB)
                    {
                        file.Position = 0x1139623;
                        file.Write(new byte[] { 0xE8, 0x28, 0x74, 0x67, 0x00, 0x90 }, 0, 6);
                    }
                }
                var status = new GameStatusService().Read(temp.Root);
                Assert.That(status.NoRegistryPatched, Is.True);
                Assert.That(status.UserDirectoryRedirected, Is.EqualTo(expected));
                Assert.That(status.Renderer, Is.EqualTo(RendererState.DirectX9));
            }
        }
    }
}

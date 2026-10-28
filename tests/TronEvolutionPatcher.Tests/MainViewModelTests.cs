using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TronEvolutionPatcher.Models;
using TronEvolutionPatcher.Services;
using TronEvolutionPatcher.ViewModels;

namespace TronEvolutionPatcher.Tests
{
    [TestFixture]
    public class MainViewModelTests
    {
        internal sealed class FakeBackend : IPatcherBackend
        {
            public string Directory = @"C:\Games\Tron\Binaries\Win32Live";
            public bool ResolveFails;
            public int ApplyCalls, DxvkCalls, VulkanCalls, LaunchCalls;
            public int CopyProfileCalls;
            public bool CopiedToPortable;
            public Exception ApplyError;
            public Exception DxvkError;
            public ManualResetEventSlim Gate;
            public GameSettings Requested;
            public PatcherSnapshot Current = new PatcherSnapshot
            {
                Status = new GameStatus { ExeExists = true, Renderer = RendererState.DirectX9, ImportedLibrary = "d3d9.dll" },
                Settings = new GameSettings(), IniPath = @"C:\Documents\GridEngine.ini", IniExists = true
            };
            public string Resolve(string path) { return ResolveFails ? null : Directory; }
            public string Discover() { return ResolveFails ? null : Directory; }
            public PatcherSnapshot Load(string directory) { return Current; }
            public IList<string> SwitchToVulkan(string directory, bool hud, bool anisotropy)
            {
                VulkanCalls++;
                if (Gate != null && !Gate.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                Current.Status.Renderer = RendererState.Vulkan;
                Current.Status.HudEnabled = hud; Current.Status.AnisotropyEnabled = anisotropy;
                return new[] { "Vulkan применён." };
            }
            public IList<string> SwitchToDirectX(string directory) { Current.Status.Renderer = RendererState.DirectX9; return new[] { "DirectX применён." }; }
            public IList<string> ApplySettings(string directory, GameSettings settings, bool registry, bool userDirectory, bool hud, bool anisotropy)
            {
                ApplyCalls++; Requested = settings;
                if (ApplyError != null) { Current.Status.NoRegistryPatched = true; throw ApplyError; }
                Current.Settings = settings; Current.Status.NoRegistryPatched = registry; Current.Status.UserDirectoryRedirected = userDirectory;
                return new[] { "Настройки применены." };
            }
            public IList<string> UpdateDxvk(string directory, bool hud, bool anisotropy)
            {
                if (DxvkError != null) throw DxvkError;
                DxvkCalls++; Current.Status.HudEnabled = hud; Current.Status.AnisotropyEnabled = anisotropy;
                return new[] { "DXVK сохранён." };
            }
            public GameLaunchResult Launch(string directory) { LaunchCalls++; return new GameLaunchResult { ExecutablePath = "GridGame.exe", ProcessId = 42 }; }
            public IList<string> CopyProfile(string directory, bool toPortable)
            {
                CopyProfileCalls++; CopiedToPortable = toPortable;
                var target = toPortable ? Current.PortableProfile : Current.DocumentsProfile;
                target.HasEngineIni = true; target.HasGameIni = true; target.HasInputIni = true; target.HasCheckpoint = true; target.ContainsFiles = true;
                return new[] { "Профиль скопирован." };
            }
        }

        internal sealed class FakeDesktop : IDesktopInteraction
        {
            public string Chosen { get; set; }
            public int SupportCalls;
            public Action OnSupport;
            public string ChooseDirectory(string initial) { return Chosen; }
            public void OpenSupport() { SupportCalls++; OnSupport?.Invoke(); }
        }

        private static async Task<MainViewModel> Loaded(FakeBackend backend)
        {
            var model = new MainViewModel(backend, new FakeDesktop()) { DirectoryInput = backend.Directory };
            await model.SelectDirectoryCommand.ExecuteAsync();
            return model;
        }

        [Test]
        public void EmptySelectionDisablesGameCommands()
        {
            var model = new MainViewModel(new FakeBackend(), new FakeDesktop());
            Assert.That(model.VulkanCommand.CanExecute(null), Is.False);
            Assert.That(model.ApplyCommand.CanExecute(null), Is.False);
            Assert.That(model.LaunchCommand.CanExecute(null), Is.False);
            Assert.That(model.BrowseCommand.CanExecute(null), Is.True);
        }

        [TestCase(RendererState.DirectX9)]
        [TestCase(RendererState.Vulkan)]
        [TestCase(RendererState.Unknown)]
        public async Task LoadedStateControlsAvailableActions(RendererState renderer)
        {
            var backend = new FakeBackend(); backend.Current.Status.Renderer = renderer;
            var model = await Loaded(backend);
            Assert.That(model.VulkanCommand.CanExecute(null), Is.EqualTo(renderer == RendererState.DirectX9));
            Assert.That(model.DirectXCommand.CanExecute(null), Is.EqualTo(renderer == RendererState.Vulkan));
            Assert.That(model.LaunchCommand.CanExecute(null), Is.EqualTo(renderer != RendererState.Unknown));
            Assert.That(model.RefreshCommand.CanExecute(null), Is.True);
            Assert.That(backend.DxvkCalls, Is.Zero, "Loading checkboxes must not write the config.");
        }

        [Test]
        public async Task EditingPathDisablesActionsUntilNewSelectionIsLoaded()
        {
            var backend = new FakeBackend(); var model = await Loaded(backend);
            model.DirectoryInput = @"C:\Another game";
            Assert.That(model.ApplyCommand.CanExecute(null), Is.False);
            Assert.That(model.LaunchCommand.CanExecute(null), Is.False);
            await model.SelectDirectoryCommand.ExecuteAsync();
            Assert.That(model.ApplyCommand.CanExecute(null), Is.True);
        }

        [Test]
        public async Task InvalidDirectoryShowsErrorWithoutChangingPreviousSelection()
        {
            var backend = new FakeBackend(); var model = await Loaded(backend);
            backend.ResolveFails = true; model.DirectoryInput = "missing";
            await model.SelectDirectoryCommand.ExecuteAsync();
            Assert.That(model.ErrorMessage, Does.Contain("GridGame.exe"));
            Assert.That(model.GameDirectory, Is.EqualTo(backend.Directory));
            Assert.That(model.CanUseGame, Is.False);
            Assert.That(model.IsBusy, Is.False);
        }

        [Test]
        public async Task SettingsReadErrorDisablesApplyWithoutBlockingRendererAndLaunch()
        {
            var backend = new FakeBackend(); backend.Current.Settings = null; backend.Current.SettingsError = "INI locked";
            var model = await Loaded(backend);
            Assert.That(model.ErrorMessage, Does.Contain("INI locked"));
            Assert.That(model.ApplyCommand.CanExecute(null), Is.False);
            Assert.That(model.VulkanCommand.CanExecute(null), Is.True);
            Assert.That(model.LaunchCommand.CanExecute(null), Is.True);
        }

        [Test]
        public async Task BusyOperationBlocksDuplicateAndOtherMutations()
        {
            using (var gate = new ManualResetEventSlim())
            {
                var backend = new FakeBackend { Gate = gate }; var model = await Loaded(backend);
                var pending = model.VulkanCommand.ExecuteAsync();
                try
                {
                    Assert.That(model.IsBusy, Is.True);
                    Assert.That(model.ApplyCommand.CanExecute(null), Is.False);
                    Assert.That(model.BrowseCommand.CanExecute(null), Is.False);
                    await model.VulkanCommand.ExecuteAsync();
                    await model.ApplyCommand.ExecuteAsync();
                }
                finally { gate.Set(); await pending; }
                Assert.That(backend.VulkanCalls, Is.EqualTo(1));
                Assert.That(backend.ApplyCalls, Is.Zero);
                Assert.That(model.IsBusy, Is.False);
                Assert.That(model.Renderer, Is.EqualTo(RendererState.Vulkan));
            }
        }

        [Test]
        public async Task LiveDxvkUpdatePreservesOtherUnsavedEdits()
        {
            var backend = new FakeBackend(); backend.Current.Status.Renderer = RendererState.Vulkan;
            var model = await Loaded(backend);
            model.ResolutionText = "3840x2160"; model.NoRegistry = true;
            model.HudEnabled = true;
            await model.PendingDxvkUpdate;
            Assert.That(backend.DxvkCalls, Is.EqualTo(1));
            Assert.That(model.ResolutionText, Is.EqualTo("3840x2160"));
            Assert.That(model.NoRegistry, Is.True);
            Assert.That(model.ErrorMessage, Is.Empty);
        }

        [Test]
        public async Task ApplyValidatesTextBeforeCallingBackend()
        {
            var backend = new FakeBackend(); var model = await Loaded(backend);
            model.ResolutionText = "invalid";
            await model.ApplyCommand.ExecuteAsync();
            Assert.That(backend.ApplyCalls, Is.Zero);
            Assert.That(model.ErrorMessage, Does.Contain("разрешение"));
            Assert.That(model.IsBusy, Is.False);
            model.ResolutionText = "2560x1440 (QHD)"; model.FpsText = "Без лимита";
            await model.ApplyCommand.ExecuteAsync();
            Assert.That(backend.ApplyCalls, Is.EqualTo(1));
            Assert.That(backend.Requested.ResolutionWidth, Is.EqualTo(2560));
            Assert.That(backend.Requested.SmoothFrameRate, Is.False);
        }

        [Test]
        public async Task PartialFailureIsVisibleAndStateIsReread()
        {
            var backend = new FakeBackend { ApplyError = new IOException("Уже выполнено: Portable. Не завершено: INI locked") };
            var model = await Loaded(backend);
            await model.ApplyCommand.ExecuteAsync();
            Assert.That(model.ErrorMessage, Does.Contain("Уже выполнено"));
            Assert.That(model.LogText, Does.Contain("INI locked"));
            Assert.That(model.NoRegistry, Is.True);
            Assert.That(model.ApplyCommand.CanExecute(null), Is.True);
        }

        [Test]
        public async Task FailedLiveUpdateDoesNotDiscardOtherDraftSettings()
        {
            var backend = new FakeBackend { DxvkError = new IOException("DXVK locked") };
            backend.Current.Status.Renderer = RendererState.Vulkan;
            var model = await Loaded(backend);
            model.ResolutionText = "3840x2160"; model.NoRegistry = true;
            model.HudEnabled = true;
            await model.PendingDxvkUpdate;
            Assert.That(model.ErrorMessage, Does.Contain("DXVK locked"));
            Assert.That(model.ResolutionText, Is.EqualTo("3840x2160"));
            Assert.That(model.NoRegistry, Is.True);
            Assert.That(model.HudEnabled, Is.False);
            Assert.That(model.IsBusy, Is.False);
        }

        [Test]
        public async Task CompleteGameConfigurationWithoutCheckpointDoesNotShowMissingProfileWarning()
        {
            var backend = new FakeBackend();
            backend.Current.DocumentsProfile = new GameProfileStatus
            { HasEngineIni = true, HasGameIni = true, HasInputIni = true, ContainsFiles = true };
            backend.Current.PortableProfile = new GameProfileStatus();
            var model = await Loaded(backend);
            Assert.That(model.ProfileNotice, Is.Empty);
            Assert.That(model.ApplyCommand.CanExecute(null), Is.True);
            Assert.That(model.CopyProfileCommand.CanExecute(null), Is.False);
        }

        [Test]
        public async Task MissingProfileOffersCurrentGameLaunchAndExplicitCopyToSelectedLocation()
        {
            var backend = new FakeBackend();
            backend.Current.DocumentsProfile = new GameProfileStatus
            { DirectoryPath = @"C:\Documents\Profile", HasEngineIni = true, HasGameIni = true, HasInputIni = true, HasCheckpoint = true, ContainsFiles = true };
            backend.Current.PortableProfile = new GameProfileStatus { DirectoryPath = @"C:\Game\UserData\Profile" };
            var model = await Loaded(backend);
            Assert.That(model.ProfileNotice, Is.Empty);
            model.UserDirectory = true;
            Assert.That(model.ProfileNotice, Does.Contain("Копировать профиль"));
            Assert.That(model.CopyProfileCommand.CanExecute(null), Is.True);
            Assert.That(model.LaunchCommand.CanExecute(null), Is.True);
            await model.CopyProfileCommand.ExecuteAsync();
            Assert.That(backend.CopyProfileCalls, Is.EqualTo(1));
            Assert.That(backend.CopiedToPortable, Is.True);
            Assert.That(backend.ApplyCalls, Is.Zero);
            Assert.That(model.UserDirectory, Is.True, "Keep the requested profile selection after copying.");
            Assert.That(model.ProfileNotice, Is.Empty);
            Assert.That(model.CopyProfileCommand.CanExecute(null), Is.False);
        }

        [Test]
        public async Task MissingBothProfilesOrPartialTargetNeverOffersMergingProfiles()
        {
            var backend = new FakeBackend();
            backend.Current.DocumentsProfile = new GameProfileStatus { DirectoryPath = "Documents" };
            backend.Current.PortableProfile = new GameProfileStatus { DirectoryPath = "UserData" };
            var model = await Loaded(backend);
            Assert.That(model.ProfileNotice, Does.Contain("пользовательских конфигов"));
            Assert.That(model.CopyProfileCommand.CanExecute(null), Is.False);
            Assert.That(model.LaunchCommand.CanExecute(null), Is.True);
            backend.Current.DocumentsProfile.HasEngineIni = true;
            backend.Current.DocumentsProfile.HasGameIni = true;
            backend.Current.DocumentsProfile.HasInputIni = true;
            backend.Current.DocumentsProfile.HasCheckpoint = true;
            backend.Current.PortableProfile.ContainsFiles = true;
            model.UserDirectory = true;
            Assert.That(model.ProfileNotice, Does.Contain("не пустая"));
            Assert.That(model.CopyProfileCommand.CanExecute(null), Is.False);
        }

        [Test]
        public async Task SupportDoesNotActivateGameOperationProgressOrChangeOperationStatus()
        {
            var desktop = new FakeDesktop();
            var model = new MainViewModel(new FakeBackend(), desktop);
            var status = model.OperationStatus;
            var busyChanges = 0;
            bool? busyWhileOpening = null;
            model.PropertyChanged += (sender, args) => { if (args.PropertyName == nameof(model.IsBusy)) busyChanges++; };
            desktop.OnSupport = () => { busyWhileOpening = model.IsBusy; };

            await model.SupportCommand.ExecuteAsync();

            Assert.That(desktop.SupportCalls, Is.EqualTo(1));
            Assert.That(busyWhileOpening, Is.False);
            Assert.That(busyChanges, Is.Zero);
            Assert.That(model.OperationStatus, Is.EqualTo(status));
            Assert.That(model.LogText, Does.Contain("Открыта страница поддержки"));
        }

        [Test]
        public async Task SupportBrowserFailureIsReportedWithoutActivatingProgress()
        {
            var desktop = new FakeDesktop { OnSupport = () => { throw new InvalidOperationException("Browser unavailable"); } };
            var model = new MainViewModel(new FakeBackend(), desktop);
            var busyChanges = 0;
            model.PropertyChanged += (sender, args) => { if (args.PropertyName == nameof(model.IsBusy)) busyChanges++; };

            await model.SupportCommand.ExecuteAsync();

            Assert.That(model.ErrorMessage, Does.Contain("Browser unavailable"));
            Assert.That(model.LogText, Does.Not.Contain("Открыта страница поддержки"));
            Assert.That(busyChanges, Is.Zero);
            Assert.That(model.IsBusy, Is.False);
            Assert.That(model.SupportCommand.CanExecute(null), Is.True);
        }

        [Test]
        public async Task DiscoverBrowseCancelSupportAndLaunchUseTheirServices()
        {
            var backend = new FakeBackend(); var desktop = new FakeDesktop();
            var model = new MainViewModel(backend, desktop);
            await model.InitializeAsync();
            Assert.That(model.GameDirectory, Is.EqualTo(backend.Directory));
            await model.BrowseCommand.ExecuteAsync();
            Assert.That(model.GameDirectory, Is.EqualTo(backend.Directory));
            await model.SupportCommand.ExecuteAsync();
            await model.LaunchCommand.ExecuteAsync();
            Assert.That(desktop.SupportCalls, Is.EqualTo(1));
            Assert.That(backend.LaunchCalls, Is.EqualTo(1));
            Assert.That(model.LogText, Does.Contain("PID 42"));
        }
    }
}

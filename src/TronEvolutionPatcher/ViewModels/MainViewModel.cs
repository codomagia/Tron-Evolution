using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TronEvolutionPatcher.Models;
using TronEvolutionPatcher.Services;

namespace TronEvolutionPatcher.ViewModels
{
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        private readonly IPatcherBackend backend;
        private readonly IDesktopInteraction desktop;
        private PatcherSnapshot snapshot;
        private bool loadingFields;
        private bool busy;
        private string directoryInput = "";
        private string gameDirectory;
        private string resolution = "1920x1080";
        private string fps = "62 FPS";
        private string language = "RUS";
        private bool motionBlur;
        private bool vsync = true;
        private bool hud = true;
        private bool anisotropy = true;
        private bool noRegistry;
        private bool userDirectory;
        private string errorMessage = "";
        private string operationStatus = "Выберите папку игры или нажмите «Найти».";
        private bool settingsLoaded;

        public MainViewModel() : this(new PatcherBackend(), new DesktopInteraction()) { }

        internal MainViewModel(IPatcherBackend backend, IDesktopInteraction desktop)
        {
            this.backend = backend;
            this.desktop = desktop;
            SelectDirectoryCommand = Command(() => RunAsync("Проверка папки", () => LoadDirectoryAsync(DirectoryInput)), () => IsIdle && !string.IsNullOrWhiteSpace(DirectoryInput));
            BrowseCommand = Command(() => RunAsync("Выбор папки", async () =>
            {
                var selected = desktop.ChooseDirectory(GameDirectory);
                if (selected != null) { DirectoryInput = selected; await LoadDirectoryAsync(selected); }
            }), () => IsIdle);
            DiscoverCommand = Command(() => RunAsync("Поиск игры рядом с патчером", async () =>
            {
                var selected = await Task.Run(() => backend.Discover());
                if (selected == null) { AddLog("Игра рядом с патчером не найдена. Укажите папку вручную."); return; }
                await LoadDirectoryAsync(selected);
            }), () => IsIdle);
            RefreshCommand = Command(() => RunAsync("Обновление состояния", () => ReloadAsync(true)), () => CanUseGame || (IsIdle && SelectionMatches));
            VulkanCommand = Command(() => RunMutationAsync("Переключение на Vulkan", () =>
            {
                var directory = GameDirectory; var selectedHud = HudEnabled; var selectedAnisotropy = AnisotropyEnabled;
                return () => backend.SwitchToVulkan(directory, selectedHud, selectedAnisotropy);
            }, false), () => CanUseGame && Renderer == RendererState.DirectX9);
            DirectXCommand = Command(() => RunMutationAsync("Возврат DirectX 9", () =>
            {
                var directory = GameDirectory;
                return () => backend.SwitchToDirectX(directory);
            }, false), () => CanUseGame && Renderer == RendererState.Vulkan);
            ApplyCommand = Command(() => RunMutationAsync("Применение настроек", () =>
            {
                var requested = SettingsTextParser.Parse(ResolutionText, FpsText, LanguageCode, MotionBlur, VSync);
                var directory = GameDirectory; var registry = NoRegistry; var redirect = UserDirectory;
                var selectedHud = HudEnabled; var selectedAnisotropy = AnisotropyEnabled;
                return () => backend.ApplySettings(directory, requested, registry, redirect, selectedHud, selectedAnisotropy);
            }, true), () => CanEditSettings);
            UpdateDxvkCommand = Command(() => RunMutationAsync("Сохранение параметров DXVK", () =>
            {
                var directory = GameDirectory; var selectedHud = HudEnabled; var selectedAnisotropy = AnisotropyEnabled;
                return () => backend.UpdateDxvk(directory, selectedHud, selectedAnisotropy);
            }, false), () => CanUseGame && Renderer == RendererState.Vulkan);
            LaunchCommand = Command(() => RunAsync("Запуск игры", async () =>
            {
                var directory = GameDirectory;
                var result = await Task.Run(() => backend.Launch(directory));
                AddLog("Запущен " + Path.GetFileName(result.ExecutablePath) + ", PID " + result.ProcessId + ".");
            }), () => CanUseGame);
            SupportCommand = Command(async () =>
            {
                await Task.Run(() => desktop.OpenSupport());
                AddLog("Открыта страница поддержки CodoMagia.");
            }, () => IsIdle);
            CopyProfileCommand = Command(() => RunMutationAsync("Копирование существующего профиля", () =>
            {
                var directory = GameDirectory; var target = UserDirectory;
                return () => backend.CopyProfile(directory, target);
            }, false), () => CanCopyProfile);
            AddLog("TRON: Evolution Patcher 1.0.0 готов к работе.");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public AsyncCommand SelectDirectoryCommand { get; }
        public AsyncCommand BrowseCommand { get; }
        public AsyncCommand DiscoverCommand { get; }
        public AsyncCommand RefreshCommand { get; }
        public AsyncCommand VulkanCommand { get; }
        public AsyncCommand DirectXCommand { get; }
        public AsyncCommand ApplyCommand { get; }
        public AsyncCommand UpdateDxvkCommand { get; }
        public AsyncCommand LaunchCommand { get; }
        public AsyncCommand SupportCommand { get; }
        public AsyncCommand CopyProfileCommand { get; }
        public Task PendingDxvkUpdate { get; private set; } = Task.CompletedTask;
        public ObservableCollection<string> LogEntries { get; } = new ObservableCollection<string>();
        public string LogText => string.Join(Environment.NewLine, LogEntries);
        public string[] Resolutions { get; } = { "1920x1080 (Full HD)", "2560x1440 (QHD)", "2880x1620 (DLDSR)", "3840x2160 (4K UHD)", "1680x1050", "1600x900", "1366x768", "1280x720", "1024x768" };
        public string[] FpsOptions { get; } = { "62 FPS", "120 FPS", "144 FPS", "165 FPS", "200 FPS", "Без лимита" };
        public string[] Languages { get; } = { "RUS", "INT" };
        public string DirectoryInput { get => directoryInput; set { if (Set(ref directoryInput, value)) Invalidate(); } }
        public string GameDirectory { get => gameDirectory; private set { if (Set(ref gameDirectory, value)) Invalidate(); } }
        public string ResolutionText { get => resolution; set => Set(ref resolution, value); }
        public string FpsText { get => fps; set => Set(ref fps, value); }
        public string LanguageCode { get => language; set => Set(ref language, value); }
        public bool MotionBlur { get => motionBlur; set => Set(ref motionBlur, value); }
        public bool VSync { get => vsync; set => Set(ref vsync, value); }
        public bool NoRegistry { get => noRegistry; set => Set(ref noRegistry, value); }
        public bool UserDirectory { get => userDirectory; set { if (Set(ref userDirectory, value)) Invalidate(); } }
        private GameProfileStatus SelectedProfile => UserDirectory ? snapshot?.PortableProfile : snapshot?.DocumentsProfile;
        private GameProfileStatus OtherProfile => UserDirectory ? snapshot?.DocumentsProfile : snapshot?.PortableProfile;
        public bool CanCopyProfile => CanUseGame && SelectedProfile != null && !SelectedProfile.ContainsFiles && OtherProfile?.IsInitialized == true;
        public string ProfileNotice
        {
            get
            {
                var target = SelectedProfile;
                if (target == null || target.IsInitialized) return "";
                var location = UserDirectory ? "UserData" : "Документы";
                var missing = !target.HasEngineIni && !target.HasCheckpoint ? "GridEngine.ini и сохранение CHECKPT" :
                    !target.HasEngineIni ? "GridEngine.ini" : "сохранение CHECKPT";
                var text = "В профиле «" + location + "» отсутствует " + missing + ". Настройки не будут применены.\n" + target.DirectoryPath;
                if (OtherProfile?.IsInitialized == true)
                    return text + "\nНайден другой профиль: " + OtherProfile.DirectoryPath +
                        (target.ContainsFiles ? "\nЦелевая папка не пустая: выберите существующий профиль или сохраните неполный профиль отдельно перед копированием. Перезапись и смешивание отключены." :
                        "\nНажмите «Копировать профиль»: источник останется на месте. Затем примените настройки.");
                return text + "\nЗапустите игру, создайте профиль и дойдите до первого сохранения, затем нажмите «Обновить». Сейчас игра использует «" +
                    (snapshot.Status.UserDirectoryRedirected ? "UserData" : "Документы") + "». Патчер не создаёт игровые файлы вместо игры.";
            }
        }
        public bool HudEnabled { get => hud; set { if (Set(ref hud, value)) QueueDxvkUpdate(); } }
        public bool AnisotropyEnabled { get => anisotropy; set { if (Set(ref anisotropy, value)) QueueDxvkUpdate(); } }
        public string ErrorMessage { get => errorMessage; private set => Set(ref errorMessage, value); }
        public string OperationStatus { get => operationStatus; private set => Set(ref operationStatus, value); }
        public bool IsBusy { get => busy; private set { if (Set(ref busy, value)) Invalidate(); } }
        public bool IsIdle => !IsBusy;
        private bool SelectionMatches => GameDirectory != null && string.Equals(DirectoryInput?.Trim(), GameDirectory, StringComparison.OrdinalIgnoreCase);
        public bool CanUseGame => IsIdle && SelectionMatches && (Renderer == RendererState.DirectX9 || Renderer == RendererState.Vulkan);
        public bool CanEditSettings => CanUseGame && settingsLoaded;
        public bool CanRedirectUserData => CanEditSettings && string.Equals(new DirectoryInfo(GameDirectory).Name, "Win32Live", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(new DirectoryInfo(GameDirectory).Parent?.Name, "Binaries", StringComparison.OrdinalIgnoreCase);
        public RendererState Renderer => snapshot?.Status.Renderer ?? RendererState.NotFound;
        public string StatusTitle => snapshot == null ? "ИГРА НЕ ВЫБРАНА" : Renderer == RendererState.Vulkan ? "АКТИВЕН VULKAN / DXVK" :
            Renderer == RendererState.DirectX9 ? "АКТИВЕН DIRECTX 9" : "РЕНДЕР НЕ ОПРЕДЕЛЁН";
        public string StatusDetails => snapshot == null ? "Выберите корень игры или папку Binaries\\Win32Live." :
            "Импорт: " + (snapshot.Status.ImportedLibrary ?? "не определён") + "  ·  Бэкап: " + (snapshot.Status.BackupExists ? "есть" : "нет") +
            "  ·  No-Registry: " + (snapshot.Status.NoRegistryPatched ? "включён" : "выключен") +
            "  ·  Сохранения: " + (snapshot.Status.UserDirectoryRedirected ? "UserData" : "Документы");
        public string IniPath => snapshot?.IniPath ?? "";
        public string IniDescription => snapshot == null ? "GridEngine.ini" : snapshot.SettingsError != null ? "Не удалось прочитать настройки" :
            "GridEngine.ini · " + (snapshot.IniPath?.IndexOf("UserData", StringComparison.OrdinalIgnoreCase) >= 0 ? "UserData" : "Документы") +
            (snapshot.IniExists ? "" : " · файл ещё не создан");

        public Task InitializeAsync() { return DiscoverCommand.ExecuteAsync(); }
        public void NotifyCloseBlocked() { AddLog("Дождитесь завершения операции перед закрытием патчера."); }

        private async Task LoadDirectoryAsync(string input)
        {
            var resolved = await Task.Run(() => backend.Resolve(input));
            if (resolved == null) throw new DirectoryNotFoundException("В выбранной папке не найден GridGame.exe.");
            var loaded = await Task.Run(() => backend.Load(resolved));
            GameDirectory = resolved;
            DirectoryInput = resolved;
            ApplySnapshot(loaded, true);
            AddLog("Выбрана игра: " + resolved);
        }

        private async Task ReloadAsync(bool editors)
        {
            var directory = GameDirectory;
            ApplySnapshot(await Task.Run(() => backend.Load(directory)), editors);
        }

        private void ApplySnapshot(PatcherSnapshot value, bool editors)
        {
            loadingFields = true;
            try
            {
                snapshot = value;
                settingsLoaded = value.Settings != null && string.IsNullOrEmpty(value.SettingsError);
                if (value.Status.Renderer == RendererState.Vulkan || value.Status.DxvkConfigExists)
                {
                    HudEnabled = value.Status.HudEnabled;
                    AnisotropyEnabled = value.Status.AnisotropyEnabled;
                }
                if (editors)
                {
                    NoRegistry = value.Status.NoRegistryPatched;
                    UserDirectory = value.Status.UserDirectoryRedirected;
                    if (settingsLoaded)
                    {
                        ResolutionText = value.Settings.ResolutionWidth + "x" + value.Settings.ResolutionHeight;
                        FpsText = value.Settings.SmoothFrameRate ? value.Settings.FpsLimit + " FPS" : "Без лимита";
                        LanguageCode = value.Settings.Language;
                        MotionBlur = value.Settings.MotionBlur;
                        VSync = value.Settings.VSync;
                    }
                }
                ErrorMessage = string.Join(Environment.NewLine, new[] { value.Status.Error,
                    value.SettingsError == null ? null : "Настройки не прочитаны: " + value.SettingsError });
                ErrorMessage = ErrorMessage.Trim();
                if (value.Status.Renderer == RendererState.Unknown || value.Status.Renderer == RendererState.NotFound)
                    ErrorMessage = ("Рендер не определён. Действия с игрой отключены. " + ErrorMessage).Trim();
                if (ErrorMessage.Length > 0) AddLog(ErrorMessage);
                foreach (var property in new[] { nameof(Renderer), nameof(StatusTitle), nameof(StatusDetails), nameof(IniPath), nameof(IniDescription) }) Notify(property);
            }
            finally { loadingFields = false; Invalidate(); }
        }

        private async Task RunMutationAsync(string label, Func<Func<System.Collections.Generic.IList<string>>> capture, bool reloadEditors)
        {
            await RunAsync(label, async () =>
            {
                var action = capture(); // Parse and capture editable values on the UI thread before starting work.
                try
                {
                    var messages = await Task.Run(action);
                    foreach (var message in messages) AddLog(message);
                    await ReloadAsync(reloadEditors);
                }
                catch
                {
                    try { await ReloadAsync(reloadEditors); }
                    catch (Exception refreshError) { AddLog("Не удалось перечитать состояние: " + refreshError.Message); }
                    throw;
                }
            });
        }

        private async Task RunAsync(string label, Func<Task> action)
        {
            if (IsBusy) return;
            IsBusy = true;
            ErrorMessage = "";
            OperationStatus = label + "…";
            AddLog(OperationStatus);
            try { await action(); OperationStatus = ErrorMessage.Length == 0 ? "Готово" : "Есть замечания — см. журнал"; }
            catch (Exception error) { ReportError(error); }
            finally { IsBusy = false; }
        }

        private void QueueDxvkUpdate()
        {
            if (!loadingFields && UpdateDxvkCommand != null && UpdateDxvkCommand.CanExecute(null))
                PendingDxvkUpdate = UpdateDxvkCommand.ExecuteAsync();
        }

        private AsyncCommand Command(Func<Task> action, Func<bool> allowed) { return new AsyncCommand(action, allowed, ReportError); }
        private void ReportError(Exception error) { ErrorMessage = error.Message; OperationStatus = "Операция не завершена"; AddLog("ОШИБКА: " + error.Message); }
        private void AddLog(string text)
        {
            LogEntries.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text);
            while (LogEntries.Count > 200) LogEntries.RemoveAt(0);
            Notify(nameof(LogText));
        }

        private void Invalidate()
        {
            foreach (var property in new[] { nameof(IsIdle), nameof(CanUseGame), nameof(CanEditSettings), nameof(CanRedirectUserData), nameof(ProfileNotice), nameof(CanCopyProfile) }) Notify(property);
            foreach (var command in new[] { SelectDirectoryCommand, BrowseCommand, DiscoverCommand, RefreshCommand, VulkanCommand,
                DirectXCommand, ApplyCommand, UpdateDxvkCommand, LaunchCommand, SupportCommand, CopyProfileCommand }) command?.RaiseCanExecuteChanged();
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            Notify(name);
            return true;
        }

        private void Notify(string name) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
    }
}

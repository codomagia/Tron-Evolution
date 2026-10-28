param([int]$Width = 960, [int]$Height = 880, [switch]$Empty, [ValidateSet("Ready", "Missing", "Copyable")][string]$Profile = "Ready")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assemblyPath = Join-Path $projectRoot 'src\TronEvolutionPatcher\bin\Release\net472\TronEvolutionPatcher.exe'
$patcherAssembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
[Windows.Application]::ResourceAssembly = $patcherAssembly
$app = [Activator]::CreateInstance($patcherAssembly.GetType('TronEvolutionPatcher.App'))
$app.ShutdownMode = [Windows.ShutdownMode]::OnExplicitShutdown
$app.InitializeComponent()
$trace = New-Object IO.StringWriter
$listener = New-Object Diagnostics.TextWriterTraceListener($trace)
[Diagnostics.PresentationTraceSources]::DataBindingSource.Listeners.Add($listener) | Out-Null
[Diagnostics.PresentationTraceSources]::DataBindingSource.Switch.Level = [Diagnostics.SourceLevels]::Warning
$window = [Activator]::CreateInstance($patcherAssembly.GetType('TronEvolutionPatcher.Views.MainWindow'))
function Set-PreviewProperty($model, $name, $value) { $model.GetType().GetProperty($name).SetValue($model, $value, $null) }
if (-not $Empty) {
    # Sample values only: no game directory is inspected and no patch service is invoked.
    $state = [Activator]::CreateInstance($patcherAssembly.GetType('TronEvolutionPatcher.Models.GameStatus'))
    Set-PreviewProperty $state 'Renderer' ([Enum]::Parse($patcherAssembly.GetType('TronEvolutionPatcher.Models.RendererState'), 'Vulkan'))
    foreach ($name in @('ExeExists', 'BackupExists', 'VulkanDllExists', 'DxvkConfigExists', 'HudEnabled', 'AnisotropyEnabled', 'NoRegistryPatched', 'UserDirectoryRedirected')) { Set-PreviewProperty $state $name $true }
    Set-PreviewProperty $state 'ImportedLibrary' 'vk09.dll'
    $settings = [Activator]::CreateInstance($patcherAssembly.GetType('TronEvolutionPatcher.Models.GameSettings'))
    $settings.ResolutionWidth = 2560; $settings.ResolutionHeight = 1440; $settings.FpsLimit = 144
    $snapshot = [Activator]::CreateInstance($patcherAssembly.GetType('TronEvolutionPatcher.Models.PatcherSnapshot'))
    Set-PreviewProperty $snapshot 'Status' $state
    Set-PreviewProperty $snapshot 'Settings' $settings
    Set-PreviewProperty $snapshot 'IniPath' 'F:\Games Portable\Tron Evolution\UserData\GridEngine.ini'
    Set-PreviewProperty $snapshot 'IniExists' ($Profile -eq 'Ready')
    $documentsProfile = [Activator]::CreateInstance($patcherAssembly.GetType('TronEvolutionPatcher.Models.GameProfileStatus'))
    $portableProfile = [Activator]::CreateInstance($patcherAssembly.GetType('TronEvolutionPatcher.Models.GameProfileStatus'))
    Set-PreviewProperty $documentsProfile 'DirectoryPath' 'C:\Documents\Disney Interactive Studios\Tron Evolution'
    Set-PreviewProperty $portableProfile 'DirectoryPath' 'F:\Games Portable\Tron Evolution\UserData\Disney Interactive Studios\Tron Evolution'
    foreach ($field in @('HasEngineIni', 'HasGameIni', 'HasInputIni', 'HasCheckpoint', 'ContainsFiles')) {
        Set-PreviewProperty $documentsProfile $field ($Profile -ne 'Missing')
        Set-PreviewProperty $portableProfile $field ($Profile -eq 'Ready')
    }
    Set-PreviewProperty $snapshot 'DocumentsProfile' $documentsProfile
    Set-PreviewProperty $snapshot 'PortableProfile' $portableProfile
    $viewModel = $window.DataContext
    Set-PreviewProperty $viewModel 'GameDirectory' 'F:\Games Portable\Tron Evolution\Binaries\Win32Live'
    $viewModel.DirectoryInput = $viewModel.GameDirectory
    $apply = $viewModel.GetType().GetMethod('ApplySnapshot', [Reflection.BindingFlags]'Instance,NonPublic')
    $null = $apply.Invoke($viewModel, [object[]]@($snapshot, $true))
    Set-PreviewProperty $viewModel 'OperationStatus' 'Готово'
}
$content = $window.Content
$content.Background = $window.Background
$content.Measure([Windows.Size]::new($Width, $Height))
$content.Arrange([Windows.Rect]::new(0, 0, $Width, $Height))
$content.UpdateLayout()
$content.Dispatcher.Invoke([Action]{}, [Windows.Threading.DispatcherPriority]::ApplicationIdle)
$bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($Width, $Height, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($content)
$outputDirectory = Join-Path $projectRoot 'artifacts\ui'
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$suffix = if ($Empty) { 'empty' } elseif ($Profile -ne 'Ready') { 'profile-' + $Profile.ToLowerInvariant() } else { 'vulkan' }
$outputPath = Join-Path $outputDirectory ("preview-$Width-$Height-$suffix.png")
$encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$stream = [IO.File]::Create($outputPath)
try { $encoder.Save($stream) } finally { $stream.Dispose() }
$listener.Flush()
$bindingErrors = $trace.ToString()
$window.Close()
$app.Shutdown()
if ($bindingErrors) { throw $bindingErrors }
Write-Output $outputPath

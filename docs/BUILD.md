# Сборка из исходников

[Русский](BUILD.md) | [English](BUILD.en.md)

Патчер написан на C# / WPF под .NET Framework 4.7.2 и распространяется под MIT. Исходный код приложения находится в `src/TronEvolutionPatcher`, тесты NUnit — в `tests/TronEvolutionPatcher.Tests`.

## Требования

- Windows.
- .NET SDK с поддержкой сборки WPF и .NET Framework 4.7.2 Developer Pack.
- .NET Framework 4.7.2 или совместимый runtime для запуска приложения и тестов.
- Доступ к NuGet при первом восстановлении зависимостей тестов.

Solution можно открыть в Visual Studio с компонентами разработки классических приложений .NET. Python для сборки и запуска C#-патчера не нужен.

## Сборка и тесты

Из корня репозитория:

```powershell
dotnet build .\TronEvolutionPatcher.sln -c Release
dotnet test .\TronEvolutionPatcher.sln -c Release --no-build --no-restore
```

Или одной командой:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Build.ps1 -Test
```

Результат: `src/TronEvolutionPatcher/bin/Release/net472/TronEvolutionPatcher.exe`.

Тесты используют искусственные PE-файлы, временные папки и подставной запуск процессов. Для них не нужна установленная игра. Наличие тестов не заменяет проверку игрового сценария на отдельной копии игры.

## Ресурсы и распространение

`src/TronEvolutionPatcher/Assets/vk09.dll` включён в исходный комплект как готовая сторонняя библиотека DXVK. Он необходим для переключения на Vulkan; сборка самого DXVK не входит в solution. Происхождение, SHA-256 и лицензия описаны в [ASSETS.md](ASSETS.md).

Иконка, логотип и полные тексты лицензий встроены в EXE. Для минимального пользовательского комплекта скопируйте только:

```text
TronEvolutionPatcher.exe
Assets/vk09.dll
```

Сохраняйте относительную структуру папок. Файлы `bin`, `obj`, результаты тестов и локальные настройки IDE в исходный репозиторий не включаются.

## Устройство приложения

- `Views`, `ViewModels`, `Resources` — WPF-интерфейс, команды и оформление.
- `Services` — поиск игры, настройки, переключение рендера, перенос профиля и запуск.
- `Patching` — проверки PE, бинарные патчи и файловый откат.
- `Models` — настройки и результаты операций.

Подробности: [рендер](RENDERER_SWITCH.md), [INI](SETTINGS_WRITE.md), [portable-профиль](PORTABLE_PATCHES.md), [DXVK и запуск](DXVK_AND_LAUNCH.md), [интерфейс](WPF_INTERFACE.md).

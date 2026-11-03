# Реализация

`Metrics` читает общие системные времена CPU, физическую память и доступное текущему пользователю место на C: через Win32. Первый отсчёт CPU задаёт базу. Ошибки не заменяются нулями. Провайдеры изолированы, выполняются последовательно в фоновом задании без накопления параллельных опросов.

`StatusEvaluator` получает монотонное время, ведёт независимые интервалы жёлтого и красного превышения, допускает прямые переходы, использует запас и выдержку восстановления. Разрывы и отсутствующие данные сбрасывают непрерывность. Интерфейс проверяет свежесть: max(5 секунд, 3 × интервал).

`BarForm` регистрирует AppBar через ABM_NEW / ABM_QUERYPOS / ABM_SETPOS и использует возвращённые координаты. ABM_REMOVE освобождает полосу при скрытии и закрытии. Обработаны уведомления позиции, активации, изменения окна, DPI, экрана, fullscreen, восстановления Explorer и питания. При отсутствии выбранного монитора используется основной; координаты могут быть отрицательными. Для предотвращения рекурсивного позиционирования используются флаги и очередь UI.

`Temperature` использует только уже установленную NVML из System32. GPU #0, sensor GPU core. Нулевые, ошибочные и неправдоподобные значения отвергаются. Пороги не назначаются автоматически; пользователь может ввести собственные в °C. CPU Package не подменяется ACPI-зоной. После отсутствия DLL приложение продолжает работу без этого блока.

`Settings` хранит пользовательские настройки атомарно. UI проверяет диапазоны, порядок порогов и достижимость восстановления. Логи ограничены двумя файлами примерно по 128 KiB. По запросу создаётся один локальный снимок диагностики.

Один экземпляр обеспечивается сеансовым mutex. Повторный запуск передаёт команду зарегистрированным оконным сообщением. Обычная панель не забирает фокус при обновлении. Настройки — отдельное активируемое окно. Установка self-contained; ярлыки имеют общий AppUserModelID. TaskbarManager проверяется через desktop marker interface, LAF, IsPinningAllowed и IsCurrentAppPinnedAsync. Установщик не вызывает запрос закрепления.

## Ограничения и непроверенные сценарии

- Основной сценарий размещения — нижняя штатная панель Windows 11. При другом расположении системной панели AppBar всё равно резервирует нижний край выбранного монитора.
- Поддерживается одна панель на одном мониторе; собственного автоскрытия нет. Существующее автоскрытие Windows не меняется.
- Несколько processor groups не агрегируются: CPU явно недоступен при >64 логических процессорах.
- Температурный модуль поддерживает одну NVIDIA GPU из существующего драйвера. Смена GPU, CPU, AMD/Intel требуют новых провайдеров.
- Выбор температурных порогов оставлен пользователю; GPU-цвет сравнивается с ними при каждом чтении без отдельного температурного гистерезиса.
- Сон, Explorer restart, другое DPI, отключение монитора, настоящий fullscreen и ARM64 требуют дополнительных проверок на соответствующих конфигурациях.
- Подтверждение TaskbarManager и реальный запуск после нового входа в Windows не автоматизировались.

## Источники API

- [GetSystemTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes)
- [GlobalMemoryStatusEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex)
- [GetDiskFreeSpaceExW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getdiskfreespaceexw)
- [Application desktop toolbars](https://learn.microsoft.com/en-us/windows/win32/shell/application-desktop-toolbars)
- [Taskbar pinning](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/pin-to-taskbar)
- [NVML initialization and cleanup](https://docs.nvidia.com/deploy/nvml-api/latest/api/group__nvmlInitializationAndCleanup.html)
- [NVML device queries](https://docs.nvidia.com/deploy/nvml-api/latest/api/group__nvmlDeviceQueries.html)

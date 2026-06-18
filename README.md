# PcStressTester

Дипломный проект — приложение для стресс-тестирования и мониторинга ПК (CPU/GPU).

## Скачать

- **Исполняемый файл:** [dist/PcStressTester.exe](dist/PcStressTester.exe) (~82 МБ, Windows x64, self-contained)
- **Видео демонстрации:** [video/PcStressTeast.mp4](video/PcStressTeast.mp4) (~103 МБ)

## Запуск

1. Скачайте `dist/PcStressTester.exe` или соберите проект сами.
2. Запустите от имени администратора (для доступа к датчикам железа).
3. Результаты тестов сохраняются в SQLite — см. [Data/README.md](Data/README.md).

## Сборка из исходников

Требуется [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet restore
dotnet run
```

Публикация одного exe-файла:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/win-x64-single
```

## Технологии

- .NET 8, Avalonia UI 11
- LibreHardwareMonitor — мониторинг датчиков
- SQLite — хранение результатов тестов
- CommunityToolkit.Mvvm

## Структура

| Папка | Описание |
|-------|----------|
| `Views/` | Окна и формы интерфейса |
| `ViewModels/` | Логика представления (MVVM) |
| `Services/` | Стресс-тесты, мониторинг, БД |
| `Models/` | Модели данных |
| `Controls/` | Пользовательские элементы UI |
| `Data/` | Схема SQLite и документация |

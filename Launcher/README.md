# Переулки

Лаунчер Windows для игры об улице Дунина-Горкавича в Ханты-Мансийске. Игра и лаунчер распространяются отдельно. Лаунчер находит текущую HDRP-сборку проекта или открывает указанный в настройках файл.

## Запуск

1. Распакуйте архив Windows x64 целиком. Рядом с `Pereulki.Launcher.exe` должна остаться папка `GalleryContent`.
2. Откройте `Pereulki.Launcher.exe`. Автономная сборка включает среду .NET и запускается с правами текущего пользователя.
3. Нажмите **Играть**. При необходимости укажите в настройках полный путь к `DuninaGorkavicha.exe`. Сохраняйте структуру папки Unity-сборки рядом с этим файлом.

Пустое поле пути включает автопоиск. Лаунчер сначала проверяет свою папку и подкаталог `Game`, затем текущий HDRP-проект в `Документы/Games/opd-hdrp-texture-experiment` и родительские папки лаунчера. Путь внутри проекта — `Unity_Walk/Build/StreetWindowsHDRPAutumnMenu/DuninaGorkavicha.exe`.

Настройки лежат в `%LOCALAPPDATA%\PereulkiLauncher\settings.json`. Можно отключить анимации, снизить качество эффектов и выбрать поведение окна после запуска игры. По умолчанию окно сворачивается. В панели **Обновления** показывается версия лаунчера и состояние подключения к серверу.

## Кадры и свет

Положите свои кадры JPEG, PNG или BMP в `GalleryContent/Screenshots/` рядом с опубликованным exe и перезапустите лаунчер. Кадры из `frames.json` показываются в указанном порядке. Остальные изображения добавляются по имени файла. Стрелки в галерее переключают кадры вручную. Индивидуальные цвет, интенсивность и направление света настраиваются в том же JSON.

```json
[
  {
    "file": "street-sunset.jpg",
    "caption": "Последний свет",
    "color": "#EE804D",
    "intensity": 0.86,
    "direction": "left"
  }
]
```

Допустимая интенсивность — от 0 до 1. Направление — `left` или `right`. Лаунчер пропускает недоступные и повреждённые изображения. Если пригодных кадров нет, он показывает состояние ожидания.

В комплект входят восемь кадров HDRP без HUD. Источники указаны относительно корня HDRP-проекта.

| Файл галереи | Исходный кадр |
| --- | --- |
| `street-sunset.jpg` | `Reports/Autumn-Atmosphere-20261123/shafts/street-rays.png` |
| `street-night.jpg` | `Reports/Autumn-Atmosphere-20261123/street-night/lamp-and-road.png` |
| `autumn-courtyard.jpg` | `TextureExperiment/after/02_courtyard_7.png` |
| `street-eye-level.jpg` | `TextureExperiment/after/03_street_eye_level.png` |
| `canopy-backlight.jpg` | `Reports/Autumn-Atmosphere-20261123/canopy-sunset/canopy-backlight.png` |
| `east-edge.jpg` | `TextureExperiment/after/05_east_edge.png` |
| `autumn-crossing.jpg` | `TextureExperiment/after/19_tree_and_garage_flank.png` |
| `district-overview.jpg` | `TextureExperiment/after-graphics/04_overview.png` |

## Разработка и публикация

Требуется Windows x64 и [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Выполните из папки `Launcher`:

```powershell
dotnet run --project .\Pereulki.Launcher.csproj
dotnet build .\Pereulki.Launcher.csproj -c Release -warnaserror
dotnet publish .\Pereulki.Launcher.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -o .\Build\Windows-x64
```

Для первой автономной публикации требуется доступ к NuGet для загрузки пакетов среды выполнения Windows x64. В архив для пользователей входят exe и папка `GalleryContent`. WPF использует аппаратное ускорение, когда его предоставляет система. Название отрисовывается вектором отдельным слоем над галереей. Размытие под кнопками ограничено их скруглёнными контурами. Галерея декодирует изображения в фоновом потоке и держит ограниченное число готовых кадров. Длительность показа — около 7 секунд, переход занимает 1,75 секунды.

| Папка | Назначение |
| --- | --- |
| `Controls/` | Общая тема, логотип и состояние микроанимаций |
| `Visuals/` | Галерея, загрузка кадров, динамическое освещение |
| `Views/` | Настройки и панель обновлений |
| `Models/`, `Services/` | Настройки, запуск игры и данные о версии |

Версия лаунчера задаётся в проекте. Точка подключения будущего источника обновлений — `Services/UpdateService.cs`. Сейчас сервис возвращает локальную версию и состояние сервера.

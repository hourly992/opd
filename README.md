> Пропустите этот репозиторий.

# opd

Основа 3D-игры про уборку Ханты-Мансийска. Первый участок — улица Дунина-Горкавича у дома №5. Unity 6.6 (6000.6.7f1), C#. Сейчас разрабатывается окружение, без экономики и механик уборки.

Откройте папку `Unity_Walk` в Unity Hub и сцену `Assets/Scenes/Street.unity`. Сборка Windows запускается через `Unity_Walk/Build_Windows.cmd`; результат появляется в `Unity_Walk/Build/StreetWindows/`.

WASD / стрелки — ходьба, мышь — обзор, Esc — меню.

Исходники моделей, геоданные, префабы и ресурсы Unity хранятся в Git. Кэш редактора, готовые игры, архивы и локальные приложения остаются только на компьютере. Структура участка, пересборка и источники: [Unity_Walk/STREET_README.md](Unity_Walk/STREET_README.md). План этапа: [Unity_Walk/ENVIRONMENT_PLAN.md](Unity_Walk/ENVIRONMENT_PLAN.md).

Для работы в OpenCode откройте корневую папку `opd`, содержащую этот файл и `.git`. Инструкции проекта находятся в `AGENTS.md`.

Обновление графики: [Unity_Walk/GRAPHICS_REFRESH.md](Unity_Walk/GRAPHICS_REFRESH.md). Дороги и тротуары сверены с панорамами 2025 года: [Unity_Walk/ROAD_RECONSTRUCTION.md](Unity_Walk/ROAD_RECONSTRUCTION.md). Типы покрытий и характерные детали восстановлены по видам; ширины и высоты остаются оценочными, без обмерной точности 1:1.

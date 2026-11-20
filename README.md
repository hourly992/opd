> Пропустите этот репозиторий.

# opd

Основа 3D-игры про уборку Ханты-Мансийска. Первый участок — улица Дунина-Горкавича у дома №5. Unity 6.6 (6000.6.7f1), C#. Сейчас разрабатывается окружение, без экономики и механик уборки.

Откройте папку `Unity_Walk` в Unity Hub и сцену `Assets/Scenes/Street.unity`. Текущая проверенная HDRP-сборка находится в `Unity_Walk/Build/StreetWindowsHDRPWindowAC/`. Для запуска используйте `Play_High.cmd`, `Play_Ultra.cmd`, `Play_Night.cmd` или `Play_DLSS_Quality.cmd`. Повторная сборка сохранённых городских сцен выполняется через `StreetFacadeUtilities.BuildCondensers`; генерацию карты для неё запускать не требуется.

WASD / стрелки — ходьба, мышь — обзор, Esc — меню.

Исходники моделей, геоданные, префабы и ресурсы Unity хранятся в Git. Кэш редактора, готовые игры, архивы и локальные приложения остаются только на компьютере. Структура участка, пересборка и источники: [Unity_Walk/STREET_README.md](Unity_Walk/STREET_README.md). План этапа: [Unity_Walk/ENVIRONMENT_PLAN.md](Unity_Walk/ENVIRONMENT_PLAN.md).

Для работы в OpenCode откройте корневую папку `opd`, содержащую этот файл и `.git`. Инструкции проекта находятся в `AGENTS.md`.

Обновление графики: [Unity_Walk/GRAPHICS_REFRESH.md](Unity_Walk/GRAPHICS_REFRESH.md). Дороги и тротуары сверены с панорамами 2025 года: [Unity_Walk/ROAD_RECONSTRUCTION.md](Unity_Walk/ROAD_RECONSTRUCTION.md). Типы покрытий и характерные детали восстановлены по видам; ширины и высоты остаются оценочными, без обмерной точности 1:1.

Обновление атмосферы от 17.11.2026: [отчёт и ограничения](Reports/Atmosphere-Upgrade.md), [PDF с парными кадрами до и после](Reports/Atmosphere-Upgrade.pdf). Open_Report.cmd открывает текстовый отчёт, Фотоальбом_игры.pdf содержит 18 новых снимков рассвета, дня и ночи.

Южный двор у домов № 5 и № 7: [отчёт и парные кадры до и после](Reports/Garage-Court.pdf), [замеры и проверки](Reports/Garage-Court-Checks.json).

Гаражный двор и дорожки в границах присланных кадров — [отчёт с фотографиями до и после](Reports/Garage-Photo-Court.pdf).

Разнесённые гаражные навесы и подходы к домам — [четыре пары кадров до и после](Reports/Garage-Canopies.pdf).

Поднятые газоны и пешеходный переход дома №5 — [проверки сохранённой сцены](Reports/Lawn-Grades-Checks.json).

Высота, плотность и дальность травы — [результаты проверок](Reports/Grass-Canopy-Checks.json).

Уличные и дворовые фонари Street Lamp от Yni Viar — [результаты проверок](Reports/Yni-Viar-Lamps-Checks.json).

Памятник Дунину-Горкавичу и шаровые фонари во дворе №7 — [кадры до и после](Reports/Dunin-Landmarks.pdf).

Лианы и трубы на фасадах домов — [парные игровые кадры и проверка скорости](Reports/Facade-Utilities.pdf).

Наружные блоки кондиционеров у окон — [парные игровые кадры и проверка расстановки](Reports/Window-Condensers.pdf).

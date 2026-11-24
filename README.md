# Amazing Online — бот-рыбак (vision automation)

Внешний бот для **Amazing Online**: смотрит на экран эмулятора/телефона и нажимает
как человек. Ничего не читает из памяти игры и не лезет в сеть — только пиксели и
тапы. Поэтому он не привязан к версии клиента и не «палится» на памяти.

```
заброс → ждём поклёвку → вываживаем → собираем улов → (мусор? багажник? еда?)
   ↑                                                                    │
   └────────────────────────  автоматический цикл  ←─────────────────────┘
```

## Что умеет

| # | Функция | Как работает |
|---|---------|--------------|
| 1 | **Снятие мусора с сетки** (необозначенная территория) | Ищет мусор шаблоном, при накоплении чистит сетку. Если игра показывает **случайную латинскую букву** (QTE) — распознаёт её через Tesseract + проверку формы (P/D, Q/O, X) и **удерживает** до исчезновения подсказки. Ошибётся — не нажимает ничего. |
| 2 | **Авто-еда и питьё** | Читает полоски голода/жажды по цвету. Ниже порога → открывает сумку, ест/пьёт, закрывает. Кулдаун, чтобы не спамить. |
| 3 | **Пополнение сеток из багажника машины** | Сетка порвалась → бот идёт к машине (`/car`, `/trunk`, Enter — настраивается), берёт новую сетку, возвращается и **ловит снова**. Проверяет, что индикатор «сетка порвана» реально исчез, иначе не зацикливается. |
| 4 | **Анти-АФК** | Каждые ~45 с (с рандомом) лёгкий свайп камеры и возврат назад — можно заниматься своими делами. |
| 5 | **Авто-стоп на 500 кг** | Журнал уловов + чтение полоски веса + запас 2 кг. Стоп **до** лимита, а не после. Счётчик переживает перезапуск. |

Дополнительно (взято из разбора чужой версии, см. `docs/review-of-prototype.md`):

* **страховки экрана** — «Загрузка»/«Потеря соединения»: ждёт до 100 с, потом стоп;
* **детектор зависания** — экран не меняется 5 минут и прогресса нет → стоп;
* **глобальные хоткеи** — `F8` пауза/продолжить, `F9` сброс счётчика, `F12` стоп;
* **ходьба и клавиши** — шаги `walk` / `hold_key` / `chat_command` в YAML;
* **уведомление о стопе** — бип + опциональный webhook (Discord/Telegram).

## Скачать готовый exe (Windows)

Ничего ставить не нужно:

**[⬇ AmazingBot — последняя сборка](https://github.com/FsbAidol/fyiuj-/releases/tag/latest)**

* `AmazingBot-onefile-win64.zip` — **один exe**, распаковать и запустить (проще всего);
* `AmazingBot-win64.zip` — папка с exe (стартует быстрее), внутри уже лежат
  `config.yaml`, `templates/`, `scripts/`, демо-шаблоны и инструкция.

Двойной клик по `AmazingBot.exe` (или `AmazingBot-onefile.exe`) → меню:

```
1) Запустить бота
2) Проверка (check) — конфиг, шаблоны, устройство
3) Диагностика (doctor)
4) Прогон на симуляторе (demo, без игры)
5) Открыть папку логов (путь печатается)
6) Открыть config.yaml
0) Выход
```

Сборка идёт автоматически на Windows-раннере GitHub Actions
(`.github/workflows/build-exe.yml`): сначала тесты, потом PyInstaller, смоук-тест
exe и публикация в Release. Пересобрать локально: `tools\build_exe.bat`
(или `pyinstaller tools/amazingbot.spec`).

> Антивирус может ругаться на «неизвестный exe» — это нормально для
> неподписанных сборок PyInstaller. UPX отключён специально, чтобы меньше
> походить на упаковщик; можно добавить папку в исключения.
> Нужны **Visual C++ Redistributable** (обычно уже стоят) и, для буквы QTE,
> Tesseract OCR — но и без него бот работает.

## Установка из исходников

```bash
python -m venv .venv
# Windows: .venv\Scripts\activate
source .venv/bin/activate
pip install -r requirements.txt
```

Для QTE-очистки нужен **Tesseract OCR** (иначе работает только геометрический
фолбэк по форме букв):

* Windows: `winget install UB-Mannheim.TesseractOCR` (или установщик с UB-Mannheim),
* Linux: `sudo apt install tesseract-ocr`,
* macOS: `brew install tesseract`.

## Быстрый старт

```bash
# 1. проверка окружения (adb/эмулятор, шаблоны, скрипты, Tesseract, хоткеи)
python -m bot check

# 2. посмотреть, что бот видит
python -m bot snapshot -o samples/shot.png

# 3. ПРОГОН БЕЗ ИГРЫ: встроенный симулятор Amazing Online
python -m bot demo --kg 500 --fish-kg 5 --speed 120 --break-every 9

# 4. боевой запуск
python -m bot run
```

`demo` — это полноценный реплей: симулятор рисует интерфейс, реагирует на тапы,
ломает сетку и сыпет мусор. Именно так проверены 500 кг, багажник и QTE.

Пример реального вывода демо-прогона:

```
weight limit reached: 500.00 / 500 kg - stopping automation
bot stopped (weight_limit_reached): uptime=00:01:59 fish=103 kg=500.00
  broken_nets=5 restocks=5 trash=34 afk=5
stop reason: weight_limit_reached      # exit code 0
```

## Подключение к игре

### Android-эмулятор (рекомендуется)

```bash
adb devices                       # эмулятор должен быть в списке
python -m bot run                 # backend: adb (по умолчанию)
```

### Окно эмулятора на ПК

```yaml
device:
  backend: desktop
  monitor: 1
```

Нужны `mss` и `pyautogui` (`pip install mss pyautogui`).

### Телефон по USB

Включите «Отладку по USB», `adb devices` → `device.backend: adb`.

## Шаблоны (главное!)

Бот узнаёт элементы по картинкам из `templates/`. В репозитории лежат
**демо-шаблоны** (`demo_templates/`, сгенерированы симулятором) — они нужны
только для `python -m bot demo`.

Для реальной игры снимите свои:

```bash
python -m bot snapshot -o samples/shot.png
python -m bot.tools.crop_template samples/shot.png 900,600,1100,700 templates/cast_button.png
```

или интерактивно:

```bash
python -m bot calibrate
```

Минимум для старта: `cast_button`, `bite`. Дальше по желанию:
`collect`, `bag_button`, `close_button`, `food_item`, `water_item`,
`net_item`, `trunk_window`, `trunk_button`, `net_broken`, `trash_on_net`,
`connection_lost`, `loading_screen`.

Затем в `config.yaml`:

```yaml
vision:
  templates_dir: templates
```

> Внутри одного шаблона не должно быть «пустого» фона — бот найдёт его везде.
> Снимайте кнопку/предмет с экрана, где он реально виден.

## Настройка поведения

Всё поведение — в `config.yaml` + YAML-скриптах в `scripts/`. Скрипты — это
маленький декларативный язык:

```yaml
name: trunk_restock
steps:
  - hook: pause_fishing
  - chat_command: "/car"
  - wait: 2.5
  - tap_template: net_item      # взять сетку
    timeout: 4
  - key: escape                 # закрыть окно
  - hook: resume_fishing
```

Доступные шаги: `tap`, `tap_region`, `tap_template`, `hold`, `swipe`, `key`,
`hold_key`, `walk`, `type`, `chat_command`, `wait`, `wait_for`, `wait_gone`,
`prompt_key`, `clear_net`, `repeat` (с `until: absent:имя`), `if_exists`
(с `else`), `assert_exists`, `assert_absent`, `log`, `hook`.

Примеры: `scripts/eat.yaml`, `scripts/drink.yaml`,
`scripts/trunk_restock.yaml`, `scripts/clear_trash.yaml`.

### Ключевые настройки

```yaml
safety:
  weight_limit_kg: 500      # автостоп
  stuck_seconds: 300        # зависание экрана
weight:
  safety_margin_kg: 2.0     # стоп чуть раньше лимита
  avg_kg_per_fish: 1.0      # средний вес рыбы (калибруется по полоске веса)
survival:
  enabled: true             # авто-еда и питьё
trunk:
  enabled: true             # пополнение сеток из машины
trash:
  enabled: true             # снятие мусора
anti_afk:
  enabled: true
  interval: 45
qte:
  enabled: true             # распознавание случайной буквы
guards:
  pause_seconds: 20         # сколько ждать загрузку
```

## CLI

```
python -m bot run [--dry-run] [--resume] [--max-kg N] [--max-minutes N]
python -m bot check            # проверка конфига/ассетов/устройства
python -m bot doctor           # подробная диагностика + тест захвата и полосок
python -m bot demo             # прогон на симуляторе
python -m bot snapshot -o file.png
python -m bot calibrate        # подбор регионов HUD
python -m bot state            # сохранённый прогресс
python -m bot reset            # сброс счётчика кг
```

`--dry-run` печатает тапы вместо нажатий — безопасно смотреть, что бот делает.

## Как устроено

```
bot/
  config.py        конфиг + валидация (относительные координаты 0..1)
  geometry.py      Region/Point, масштабирование под любое разрешение
  vision.py        matchTemplate, чтение цветных полосок, OCR, motion
  prompt_key.py    распознавание случайной буквы QTE (OCR + форма P/D Q/O X)
  weight.py        бюджет 500 кг: журнал уловов + наблюдения + запас
  guards.py        загрузка/дисконнект (ждём, а не падаем), детектор зависания
  watch.py         хоткеи F8/F9/F12, пауза
  actions.py       высокоуровневые действия (tap/wait/hold/QTE)
  scripts.py       исполнение YAML-скриптов
  runner.py        оркестратор: приоритеты, автостоп, восстановление
  tasks/           fishing, survival, trunk, trash, anti_afk
  device/          захват (adb/desktop/images/video) и ввод (adb/desktop)
  tools/           check, doctor, calibrate, crop_template, demo
```

Приоритеты на каждом тике:

1. лимит 500 кг → стоп
2. страховки экрана → ждём/стоп
3. сетка порвалась → багажник → снова рыбачим
4. жажда/голод → еда/вода
5. мусор на сетке → очистка
6. анти-АФК
7. рыбалка

## Тесты

```bash
python -m pytest            # 95 тестов, без эмулятора и без сети
```

Покрыто: геометрия, зрение, YAML-скрипты, QTE (включая «не жать при сомнении»),
бюджет 500 кг, страховки, сценарий «сетка → багажник → ловля», автостоп.

## Ограничения и честность

* Это **не** memory-чит: только пиксели. Иногда игра перерисовывает UI и шаблоны
  приходится переснять — это нормально.
* Вес одной рыбы бот знает приблизительно; из-за запаса 2 кг он может
  остановиться на 498–499 кг. Это осознанный выбор в пользу лимита.
* Если авторизация в игре использует капчу/античит по поведению, бот может
  попасть под внимание. Автор не несёт ответственности за блокировки аккаунта:
  используйте на свой риск и в рамках правил сервера.

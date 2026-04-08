# TextureUnshuffle — план разработки

## Анализ веб-версии

Веб-версия (`texture_unshuffle.html`) делает следующее:
- Хранит 64 тайла (8×8 сетка, каждый 16×16 px) **встроенными** в base64 — нельзя загрузить другую текстуру без редактирования HTML
- Хранит **захардкоженную** обратную перестановку для seed 149
- Позволяет кликать по двум тайлам для их обмена
- Скачивает результат как PNG

**Главная проблема**: нельзя сразу работать с текущим файлом текстуры — нужно каждый раз перегенерировать HTML.

---

## Выбор стека: C# + Avalonia

**Почему Avalonia, а не Python:**
- Пользователь знает C# — сможет самостоятельно читать и дорабатывать код
- Нативный перформанс для работы с изображениями (SkiaSharp под капотом)
- Drag-and-drop, кастомные контролы — всё есть из коробки
- Один исполняемый файл, никаких venv и зависимостей при запуске
- Кроссплатформенность (Windows/Linux/macOS) если понадобится

**Ключевые NuGet-пакеты:**
- `Avalonia` — UI фреймворк
- `Avalonia.Desktop` — запуск на десктопе
- `SkiaSharp` — низкоуровневая работа с пикселями (встроен в Avalonia)

---

## Архитектура проекта

```
TextureUnshuffle/
├── TextureUnshuffle.sln
├── src/
│   └── TextureUnshuffle/
│       ├── TextureUnshuffle.csproj
│       ├── Program.cs                  # точка входа
│       ├── App.axaml / App.axaml.cs
│       ├── Models/
│       │   ├── TileGrid.cs             # логика сетки тайлов
│       │   └── ShufflePermutation.cs   # алгоритм seed-перестановки
│       ├── ViewModels/
│       │   └── MainViewModel.cs        # MVVM: состояние и команды
│       └── Views/
│           ├── MainWindow.axaml        # главное окно
│           └── MainWindow.axaml.cs
└── Sample/
    ├── texture.png
    └── texture_unshuffle.html          # оригинальная веб-версия (для справки)
```

**Паттерн:** MVVM (стандарт для Avalonia). ViewModel содержит всю логику, View только биндинги.

---

## Модель данных

### TileGrid
```
- OriginalBitmap: SKBitmap        # загруженная текстура (неизменна)
- TileSize: int                   # размер тайла в пикселях (16 по умолчанию)
- Cols, Rows: int                 # количество тайлов по X и Y (8×8)
- Tiles: SKBitmap[]               # нарезанные тайлы (длина = Cols*Rows)
- Arrangement: int[]              # arrangement[displayPos] = tileIndex
- History: Stack<int[]>           # для Undo/Redo
```

### ShufflePermutation
```
- Seed: int                       # seed для генератора
- GeneratePermutation(seed, n)    # тот же алгоритм что в оригинале
- GetInverse(perm)                # обратная перестановка
```

---

## UI-макет главного окна

```
┌─────────────────────────────────────────────────────────────┐
│ [Открыть файл]  Тайл: [16] px  Сетка: [8]×[8]  [Применить] │  ← Toolbar
├────────────────────┬────────────────────┬────────────────────┤
│                    │                    │                    │
│  Оригинал          │  Результат         │  Сетка тайлов      │
│  (только просмотр) │  (интерактивный)   │  (клик для выбора) │
│                    │                    │                    │
│  [512×512 canvas]  │  [512×512 canvas]  │  [8×8 grid]        │
│                    │                    │                    │
├────────────────────┴────────────────────┴────────────────────┤
│ Seed: [149]  [▶ Авто-восстановить]  [↩ Сброс]  [↩ Undo]    │
│ [💾 Сохранить]  [💾 Сохранить как...]   Статус: ...         │  ← Statusbar
└─────────────────────────────────────────────────────────────┘
```

---

## Функционал

### Обязательный (MVP)

| # | Функция | Описание |
|---|---------|----------|
| 1 | **Открыть текстуру** | Диалог выбора PNG/BMP/TGA файла |
| 2 | **Нарезка на тайлы** | Разбить изображение на `Cols × Rows` тайлов по `TileSize` пикселей |
| 3 | **Авто-восстановление по seed** | Ввести seed → вычислить обратную перестановку → применить |
| 4 | **Ручная замена тайлов** | Клик по тайлу на canvas или в сетке → выбор → второй клик → swap |
| 5 | **Сохранить** | Перезаписать исходный файл восстановленной текстурой |
| 6 | **Сохранить как...** | Диалог выбора нового пути |
| 7 | **Сброс** | Вернуть к исходному состоянию (перемешанному) |
| 8 | **Undo/Redo** | Ctrl+Z / Ctrl+Y для отмены ручных свопов |

### Дополнительный (после MVP)

| # | Функция | Описание |
|---|---------|----------|
| 9 | **Drag-and-drop тайлов** | Перетаскивать тайлы мышью вместо двойного клика |
| 10 | **Масштаб просмотра** | Zoom in/out на канвасах (колёсико мыши) |
| 11 | **Recent files** | Список последних открытых файлов |
| 12 | **Drag-and-drop файла** | Перетащить файл текстуры в окно программы |
| 13 | **Сравнение** | Переключатель "показать оригинал / показать результат" для сравнения |

---

## Алгоритм seed-перестановки

Из веб-версии понятно что используется **Fisher-Yates shuffle** с простым LCG-генератором. Нужно воспроизвести точно тот же алгоритм на C#.

```csharp
// Воспроизводимый shuffle как в оригинальном JS коде
int[] GeneratePermutation(int seed, int n)
{
    var arr = Enumerable.Range(0, n).ToArray();
    var rng = new SeededRandom(seed); // LCG совпадающий с JS Math.random()
    for (int i = n - 1; i > 0; i--)
    {
        int j = rng.Next(i + 1);
        (arr[i], arr[j]) = (arr[j], arr[i]);
    }
    return arr;
}

int[] GetInverse(int[] perm)
{
    var inv = new int[perm.Length];
    for (int i = 0; i < perm.Length; i++)
        inv[perm[i]] = i;
    return inv;
}
```

> **Важно**: нужно точно определить какой генератор использовался для создания `INV_PERM` в HTML (seed=149 даёт конкретный массив). Возможно потребуется подобрать совместимый LCG или просто захардкодить логику из JS.

---

## Работа с изображениями (SkiaSharp)

```csharp
// Загрузка
using var bitmap = SKBitmap.Decode(filePath);

// Нарезка тайла
var tile = new SKBitmap(tileSize, tileSize);
using var canvas = new SKCanvas(tile);
canvas.DrawBitmap(bitmap,
    SKRect.Create(col * tileSize, row * tileSize, tileSize, tileSize),
    SKRect.Create(0, 0, tileSize, tileSize));

// Сборка результата
var result = new SKBitmap(cols * tileSize, rows * tileSize);
using var resultCanvas = new SKCanvas(result);
for (int pos = 0; pos < n; pos++)
{
    int tileIdx = arrangement[pos];
    int dr = pos / cols, dc = pos % cols;
    resultCanvas.DrawBitmap(tiles[tileIdx],
        SKRect.Create(dc * tileSize, dr * tileSize, tileSize, tileSize));
}

// Сохранение
using var image = SKImage.FromBitmap(result);
using var data = image.Encode(SKEncodedImageFormat.Png, 100);
File.WriteAllBytes(outputPath, data.ToArray());
```

---

## Этапы разработки

### Этап 1: Скелет проекта (Setup)
- [ ] Создать solution и Avalonia-проект (`dotnet new avalonia.mvvm`)
- [ ] Настроить структуру папок (Models / ViewModels / Views)
- [ ] Убедиться что приложение запускается (Hello World окно)

### Этап 2: Загрузка и нарезка текстуры
- [ ] `TileGrid.Load(path, tileSize, cols, rows)` — загрузка и нарезка
- [ ] Отображение оригинала в левом панели (Avalonia `Image` control с `WriteableBitmap`)
- [ ] Отображение тайлов в сетке справа

### Этап 3: Авто-восстановление по seed
- [ ] `ShufflePermutation.GeneratePermutation(seed, n)` — с тестом против hardcoded INV_PERM из HTML
- [ ] Кнопка "Применить" + поле ввода seed
- [ ] Отображение результата в центральном канвасе

### Этап 4: Ручная замена тайлов
- [ ] Клик по тайлу на канвасе или в сетке — выделение (жёлтая рамка)
- [ ] Второй клик — swap и обновление обоих канвасов
- [ ] Статус-бар с подсказками

### Этап 5: Сохранение
- [ ] `TileGrid.SaveAs(path)` — сборка и запись PNG
- [ ] Кнопки "Сохранить" и "Сохранить как..."
- [ ] Подтверждение перезаписи при "Сохранить"

### Этап 6: Undo/Redo
- [ ] `Stack<int[]>` для истории состояний `arrangement`
- [ ] Ctrl+Z / Ctrl+Y

### Этап 7: Полировка UI
- [ ] Тёмная тема (как в веб-версии: `#1a1a2e` фон, `#f2a100` акцент)
- [ ] Корректное масштабирование тайлов (pixelated, без сглаживания)
- [ ] Обработка ошибок (некорректный файл, неправильные параметры сетки)
- [ ] Drag-and-drop файла в окно

---

## Конфигурация проекта (`.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.*" />
    <PackageReference Include="Avalonia.Desktop" Version="11.*" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.*" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
  </ItemGroup>
</Project>
```

> SkiaSharp входит в состав Avalonia, отдельно подключать не нужно.

---

## Что изменится по сравнению с веб-версией

| Веб-версия | Десктопная версия |
|------------|-------------------|
| Тайлы захардкожены в base64 | Загрузка любого PNG/BMP файла |
| Seed захардкожен (149) | Настраиваемый seed |
| Сохранение через "Скачать" | Перезапись исходного файла |
| Нет Undo | Undo/Redo (Ctrl+Z/Y) |
| Фиксированные 8×8 16px тайлы | Настраиваемые размер и сетка |
| Только web-браузер | Нативное десктоп-приложение |

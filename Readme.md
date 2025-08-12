# Smotrel — видеоплеер для курсов

**Smotrel** — это десктопный WPF-плеер для офлайн-курсов с поддержкой:
- автоматического сканирования структуры курса (главы / части),
- атомарного хранения метаданных курса в JSON (`.smotrel/course.json`),
- единственной метки возобновления (resume) на курс,
- debounce-логики сохранения позиции воспроизведения,
- баннера «Продолжить / Сбросить» и авто-перехода на следующую главу.

---

## Содержание README
1. [Краткий обзор](#short-review)  
2. [Структура проекта](#Project-structure)  
3. [Ключевые концепции](#key-concepts)  
4. [Формат `course.json` — пример и описание полей](#format-coursejson)  
5. [Как собрать и запустить](#how-to-build-and-run)  
6. [Требования и зависимости](#requirements-and-dependencies)  
7. [DI / Регистрация сервисов (пример)](#di--register-services-example)  
8. [Основные интерфейсы и обязанности](#main-interfaces-and-responsibilities)  
9. [UI: поведение и интеграция](#ui-behavior-and-integration)  
10. [Чек-лист для ручного тестирования (E2E)](#checklist-for-manual-testing-e2e)  
11. [Отладка и распространённые проблемы](#debugging-and-common-problems)  
12. [Roadmap / Идеи для улучшений](#roadmap--ideas-for-improvements)  

---

<h2 id="short-review">Краткий обзор</h2>

Smotrel хранит метаданные курса локально в `.smotrel/course.json`. При выборе директории приложение:
1. сканирует каталог (парсит главы/части, индексы, опционально длительности через `ffprobe`),
2. сравнивает с существующим JSON (если есть),
3. сливает/обновляет данные через `CourseMergeService`,
4. сохраняет результат (atomic save, бэкап),
5. заполняет TreeView и Playlist.

Resume-маркер — **единственный на курс** (`lastPlayedPartId` + `lastPlayedPositionSeconds`) — это предотвращает коллизию множества меток и упрощает UX.

---
<h2 id="Project-structure">Структура проекта</h2>

```
Smotrel/
├─ Services/
│ ├─ Interfaces/ # интерфейсы (ICourseScanner, ICourseRepository и т.д.)
│ └─ Implementations/ # реализации (CourseScanner, CourseJsonRepository, PlaybackService...)
├─ Data/
│ ├─ Entities/ # CourseEntity, ChapterEntity, PartEntity
│ └─ Dto.cs
├─ ViewModels/
│ ├─ MainViewModel.cs
│ ├─ FolderNodeViewModel.cs
│ └─ VideoNodeViewModel.cs
├─ Views/
│ ├─ MainWindow.xaml
│ └─ MainWindow.xaml.cs
├─ Messages/
│ ├─ PlayVideoMessage.cs
│ ├─ OpenFolderMessage.cs
│ ├─ VideoControlMessage.cs
│ └─ ResumeAvailableMessage.cs
├─ Helpers/
│ └─ RelayCommand.cs
├─ App.xaml
└─ App.xaml.cs
```

---

<h2 id="key-concepts">Ключевые концепции</h2>


- **CourseScanner** — сканирует структуру и возвращает `CourseEntity`.  
  Парсит `Index` части из имени файла, `Order` главы из имени папки, присваивает стабильные индексы и сортирует. Опция `tryGetDurations` использует `ffprobe` для получения длительности.

- **CourseJsonRepository** — репозиторий, который хранит JSON в `<root>/.smotrel/course.json`. Делает atomic-save и backups (`.smotrel/backups/`).

- **CourseMergeService** — объединяет старые пользовательские данные (watched, пользовательские заголовки, позиции) с новым результатом сканирования.

- **PlaybackService** — отвечает за сохранение позиции: debounce (2s по умолчанию), `SavePositionByPartIdAsync` для моментальной записи, `MarkWatchedByPartIdAsync`, `ClearResumeAsync`, `FlushAsync`.

- **Resume flow**:
  - Единственный курс-маркер: `CourseEntity.LastPlayedPartId` + `LastPlayedPositionSeconds`.
  - При выборе части VM проверяет маркер и посылает `ResumeAvailableMessage`, MainWindow показывает баннер «Продолжить / Сбросить».

---
<h2 id="format-coursejson">Формат `course.json` (пример)</h2>

Файл хранится в `<courseRoot>/.smotrel/course.json`.

```json
{
  "id": "b3f9e3a0-...-...",
  "rootPath": "C:/Courses/SuperCourse",
  "platform": "Super.biz",
  "title": "Название курса",
  "chapters": [
    {
      "id": "45ef..",
      "title": "1. Введение",
      "order": 1,
      "relPath": ".",
      "parts": [
        {
          "id": "a1b2...",
          "fileName": "[Super.biz] 1. Введение.mp4",
          "path": "C:/Courses/SuperCourse/[Super.biz] 1. Введение.mp4",
          "index": 1,
          "title": "Введение",
          "durationSeconds": 300,
          "lastPositionSeconds": 120,
          "watched": false,
          "fileSizeBytes": 12345678
        }
      ]
    }
  ],
  "createdAt": "2025-08-12T12:00:00Z",
  "fsHash": "ab12fe34...",
  "lastScannedAt": "2025-08-12T12:01:00Z",
  "meta": {
    "totalDuration": 3600,
    "totalParts": 12,
    "watchedSeconds": 1800
  },
  "lastPlayedPartId": "a1b2...",
  "lastPlayedPositionSeconds": 120,
  "status": "inprogress"
}
```
**Ключевые поля:**

+ fsHash — детерминированный SHA256 по (relPath|size|lastWriteTicks) всех видеофайлов; используется для детекции изменений.

+ lastPlayedPartId + lastPlayedPositionSeconds — единственная точка возобновления.

---
<h2 id="how-to-build-and-run">Как собрать и запустить</h2>

Требуется: .NET SDK (версия проекта), Windows (WPF).

Сборка и запуск:

``` bash
# из корня проекта
dotnet build
dotnet run --project Smotrel
```
Или открой решение в Visual Studio и запуск ```Start``` как обычно.

---
<h2 id="requirements-and-dependencies">Требования и зависимости</h2>

+ .NET SDK (см. csproj)

+ (Опционально) ```ffprobe``` (часть ```ffmpeg```) в PATH — если вы хотите, чтобы сканер собирал длительности видео. Без ```ffprobe``` длительности остаются ```null```.

---
<h2 id="di--register-services-example">DI / Регистрация сервисов (пример)</h2>

Добавьте в ```App.ConfigureServices```:

```
services.AddSingleton<ICourseScanner, CourseScanner>();
services.AddSingleton<ICourseRepository, CourseJsonRepository>();
services.AddSingleton<ICourseMergeService, CourseMergeService>();
services.AddSingleton<IPlaybackService, PlaybackService>();
services.AddSingleton<MainViewModel>();
services.AddSingleton<MainWindow>();
```
```MainWindow``` конструируется как:
```
public MainWindow(MainViewModel vm, IPlaybackService playbackService, ICourseRepository repository) { ... }
```
<h2 id="main-interfaces-and-responsibilities">Основные интерфейсы (коротко)</h2>

+ ```ICourseScanner.ScanAsync(string rootPath, bool tryGetDurations = false)```

+ ```ICourseRepository.LoadAsync(string rootPath), SaveAsync(CourseEntity), BackupAsync(...)```

+ ```ICourseMergeService.Merge(CourseEntity existing, CourseEntity scanned)```

+ ```IPlaybackService.NotifyPositionAsync(string filePath, long seconds), SavePositionByPartIdAsync(...), MarkWatchedByPartIdAsync(...), ClearResumeAsync(...), FlushAsync()```

<h2 id="ui-behavior-and-integration">UI: поведение и интеграция</h2>

+ VM отправляет ```PlayVideoMessage(filePath)``` при выборе ```SelectedVideo```.

+ ```MainWindow``` подписан на ```PlayVideoMessage``` (или на ```CurrentVideoPath```), ставит ```MediaElement.Source``` и запускает воспроизведение (авто).

+ При загрузке части VM проверяет ```CourseEntity.LastPlayedPartId``` и посылает ```ResumeAvailableMessage``` — MainWindow показывает баннер.

+ ```ResumeBanner``` содержит ```BtnResume``` (устанавливает ```Player.Position```) и ```BtnClearResume``` (очищает маркер через ```IPlaybackService.ClearResumeAsync```).

+ ```MediaEnded``` → ```PlaybackService.MarkWatchedByPartIdAsync``` и авто-advance логика (следующая часть / начало следующей главы).

<h2 id="checklist-for-manual-testing-e2e">Чек-лист для ручного тестирования (E2E)</h2>

1) Выбрать папку курса → ```.smotrel/course.json``` создан.

2) Открыть часть A, перемотать на 1:20, закрыть приложение → JSON содержит ```lastPlayedPartId``` и ```lastPlayedPositionSeconds ≈ 80```.

3) Открыть курс снова → выбранная часть соответствует lastPlayedPartId, появляется баннер «Продолжить». Нажать ```Продолжить``` — плеер переходит к позиции.

4) Досмотреть часть до конца → часть помечается ```watched = true```, resume-маркер очищается, авто-переход на следующую главу (при достижении конца главы).

5) Переименовать/удалить файл → повторный скан должен поменять ```ч```, вызвать merge и сохранить бэкап старого JSON.

<h2 id="debugging-and-common-problems">Отладка и распространённые проблемы</h2>

+ Сканирование долгое: если ```tryGetDurations=true```, ```ffprobe``` вызовы замедляют полный scan. Рекомендуется делать fast-scan без длительностей и дополнять длительности в фоне.

+ ffprobe не найден: длительности будут null — поведение приложения сохраняется. Установите ffmpeg/ffprobe и убедитесь, что ```ffprobe``` в PATH.

+ Resume не срабатывает: проверьте, что GUID части (```PartEntity.Id```) совпадает с ```VideoItem.PartId``` (строка GUID).

+ Порядок глав неверный (1, 10, 11): проверьте, правильно ли парсится индекс главы; сканер пытается извлечь ```Order``` из имени папки, иначе использует min(Index) из частей.

<h2 id="roadmap--ideas-for-improvements">Roadmap / Идеи для улучшений</h2>

+ Фоновое получение длительностей (ffprobe) после быстрого сканирования.

+ Инкрементальный scan: обновлять только изменённые части на основе ```fsHash``` и кэша в JSON.

+ Настройки пользователя (```.smotrel/settings.json```) — автоплей, время авто-hide overlay, debounce delay.

+ Unit tests для ```CourseScanner```, ```CourseJsonRepository```, ```PlaybackService```.

+ Экспорт/импорт (migrate from sqlite — one-time helper).

+ Bookmarks, multi-user profiles, cloud sync.

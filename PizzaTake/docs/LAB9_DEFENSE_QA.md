# ЛР №9: Контрольні питання, відповіді та план усного захисту `PizzaTake`

Цей документ містить письмові відповіді на ключові контрольні питання за матеріалами ЛР №5–9 та структурований план усного захисту (5–7 хвилин).

---

## 1. Письмові відповіді на контрольні питання

### 1. Чому бізнес-логіку та ViewModel винесено в окрему бібліотеку `PizzaTake.Core` без залежності від `Microsoft.Maui.*`?
- **Тестованість без емулятора:** `PizzaTake.Core` компілюється під чистий `net11.0`, тому xUnit-тести запускаються за секунди на Linux CI (`ubuntu-latest`) без Android/Windows runtime.
- **Інверсія залежностей (DIP):** усі статичні API MAUI (`Shell.Current`, `Preferences.Default`, `SecureStorage.Default`, `Geolocation.Default`, `MediaPicker.Default`, `Connectivity.Current`, `Accelerometer.Default`, `MainThread`) замінені на інтерфейси `PizzaTake.Core.Services.*` (`INavigationService`, `IDialogService`, `ISettingsService`, `ISecureStoreService`, `ILocationService`, `IMediaService`, `IShakeSensorService`, `IConnectivityService`, `IMainThreadDispatcher` тощо). У тестах вони легко підміняються через `Moq`, а в застосунку реєструються як `Maui*`-адаптери у `MauiProgram.cs`.
- **Чистий MVVM:** `Views/*.xaml.cs` містять лише прив’язку `BindingContext`, життєвий цикл `OnAppearing`/`OnDisappearing`, керування сенсором та візуальний шар карти (`OsmMapRenderer`), без бізнес-логіки й звернень до БД.

### 2. Як організовано логування (Serilog), глобальне перехоплення винятків та інтеграцію Sentry?
- **Serilog (`MauiProgram.cs`):** рівень `Debug` у `#if DEBUG` та `Information` у `Release`; два sink-и — `Debug` і `File` (`AppDataDirectory/logs/app-.log`, щоденна ротація `RollingInterval.Day`, зберігання 7 файлів, `shared: true` для безпечного читання під час роботи застосунку).
- **Структуровані логи:** у всі ViewModel, сервіси й репозиторії інжектується `ILogger<T>`, виклики `Debug.WriteLine` повністю відсутні. Рівні: `Information` (події життєвого циклу/збереження), `Warning` (відмова RBAC/дозволу, LWW-конфлікт, вимкнений GPS), `Error` (винятки БД/мережі/камери), `Fatal` (критичні необроблені винятки).
- **Глобальні обробники (`GlobalExceptionHandler.cs` + `MainApplication.cs`):** підписка на `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` (із `e.SetObserved()`) та `AndroidEnvironment.UnhandledExceptionRaiser` — усі записують помилку в Serilog і відправляють у Sentry.
- **Sentry.Maui:** підключено через `builder.UseSentry(...)` з `SendDefaultPii = false`; бізнес-події (`ProductOpened`, `OrderPlaced`, `LoginSuccess`, `QualityReportCreated`) трекаються через `ICrashReportingService`, а `BugReportService` дозволяє користувачу надіслати ручний звіт з останніми 30 рядками логу та метаданими пристрою.
- **`LogsPage`:** читає активний лог-файл із `FileShare.ReadWrite`, підтримує фільтрацію за рівнями та кнопки діагностики («Тест винятку в Task», «Тест крашу (Sentry)», «Повідомити про помилку»).

### 3. Як реалізовано дворівневий контроль доступу (RBAC) та захист сесії?
- **Модель ролей:** `UserRole { Customer, Administrator }` зберігається в `UserProfile.Role` та керується через Singleton `ISessionService` / `SessionService` (подія `SessionChanged`).
- **Рівень 1 (UI):** властивість `IsAdmin => sessionService.IsInRole(UserRole.Administrator)` у `MenuViewModel`, `OrderDetailViewModel`, `SettingsViewModel` та адмін-ViewModel приховує адмін-елементи через XAML Binding (`IsVisible`).
- **Рівень 2 (Команди):** кожна захищена команда викликає спільний метод `BaseViewModel.RequireAdminAsync(...)` — навіть якщо команду викликати напряму з коду або тесту під роллю `Customer`, виконання блокується, записується `LogWarning`, а репозиторій не викликається (перевірено unit-тестами з `Times.Never`).
- **Захищене зберігання:** токен сесії, роль і телефон зберігаються виключно у `SecureStorage` (`MauiSecureStoreService`), а не у відкритих `Preferences`; при виході повністю очищаються.

### 4. Як працюють біометрія, AES-шифрування в SQLite та SSL Certificate Pinning?
- **Біометрія (`IBiometricAuthService` / `MauiBiometricAuthService`):** коли в `SecureStorage` є збережений токен (сесію заблоковано або користувач повертається) і пристрій підтримує біометрію (`Plugin.Maui.Biometric`), на `AuthPage` з’являється кнопка «Увійти за біометрією», яка відновлює `ISessionService` без повторного OTP. За відсутності апаратної підтримки повертається коректне повідомлення без падіння.
- **AES-шифрування (`AesEncryptionService` + `UserProfileRepository`):** поля `Phone` і `CityName` шифруються **AES-256-CBC** із випадковим 16-байтним IV перед записом у SQLite і розшифровуються при читанні; ключ зберігається у `SecureStorage`. Це підтверджено інтеграційним тестом `SyncQueueAndEncryptionIntegrationTests` (сирі дані в БД не містять відкритого тексту).
- **SSL Certificate Pinning (`HttpApiClient`):** `HttpClientHandler.ServerCertificateCustomValidationCallback` обчислює SHA-256 thumbprint сертифіката сервера (`GetCertHashString(HashAlgorithmName.SHA256)`) і звіряє з білим списком дозволених пінів. Невідповідний або підмінений сертифікат відхиляється з `LogError` (метод `VerifySpoofedCertificateRejected()` покритий unit-тестом).

### 5. Як реалізовано роботу з апаратними можливостями та захист від нештатних ситуацій?
- **Дозволи (`MauiPermissionService`):** перед запитом перевіряється поточний статус (`CheckStatusAsync`), за потреби показується діалог-пояснення (`ShouldShowRationale`), далі викликається `RequestAsync`. Відмова логується як `Warning` і не спричиняє краш.
- **Камера та галерея (`MauiMediaService` + `QualityReportViewModel`):** фото копіюється в `AppDataDirectory/photos/report_yyyyMMdd_HHmmss.jpg`, у SQLite (`QualityReports`) записується лише `PhotoPath`. Оброблено всі кейси `HardwareFailureReason`: `PermissionDenied`, `FeatureNotSupported`, `FeatureNotEnabled`, `Cancelled`, `Error`.
- **Геолокація та OpenStreetMap (`MauiLocationService`, `OsmMapRenderer`, `PizzeriaSelectionPage`, `PizzeriasMapPage`):** координати визначаються з тайм-аутом 10 с та fallback на `GetLastKnownLocationAsync()`; карта відображається через `Mapsui.Maui` + тайли OpenStreetMap без Google API key; для зовнішньої навігації (і як альтернатива на Windows) працює `IMapLauncherService.OpenExternalMapAsync`.
- **Акселерометр (`MauiShakeSensorService`):** перевіряється `IsSupported`, запуск `Start()` виконується в `OnAppearing()`, зупинка `Stop()` — в `OnDisappearing()` (`MenuPage` і `OrderDetailPage`), щоб не витрачати батарею у фоні. Навігація з події виконується через `IMainThreadDispatcher.BeginInvokeOnMainThread`.

### 6. Як працюють офлайн-режим, черга синхронізації `SyncQueue` і міграції SQLite?
- **Міграції (`SqliteDatabase`):** при старті під `SemaphoreSlim` перевіряється `PRAGMA user_version` і послідовно застосовуються кроки `0 → 1` (базові таблиці), `1 → 2` (колонки `Notes`, `Latitude`, `Longitude` у `UserAddresses`), `2 → 3` (`QualityReports` + `UserProfiles.Role`), а також ідемпотентні таблиці `ProductCategories` і `MenuBanners`.
- **Офлайн і LWW (`SyncService` + `ConnectivityBannerViewModel`):** зміни адрес/замовлень/актів якості додаються у `SyncQueueItems`. Якщо мережа відсутня, показується банер «Офлайн-режим (у черзі: N)»; при появі з’єднання `IConnectivityService.ConnectivityChanged` автоматично запускає `SyncPendingAsync()`. Якщо серверний запис має новіший `UpdatedAtUtc`, діє **Last-Write-Wins** — локальний застарілий запис не перезаписує сервер і логується `Warning`.

### 7. Як побудована стратегія тестування та CI/CD?
- **Модульні тести (`PizzaTake.Core.Tests/Unit`):** 13 класів (57 тестових методів із `[Fact]`/`[Theory]`/`[InlineData]`) покривають ViewModel, RBAC-заборону для `Customer`, обробку відмов камери/GPS, LWW у `SyncService`, SSL Pinning, формулу акційної знижки та фільтрацію логів.
- **Інтеграційні тести (`PizzaTake.Core.Tests/Integration`):** 3 класи (9 тестів) на реальній тимчасовій SQLite БД (`IAsyncLifetime`): повний CRUD, міграція `v0 → v3`, `SyncQueue` та AES-шифрування `UserProfile`.
- **Покриття коду:** збирається `coverlet.collector` у форматі Cobertura і конвертується в HTML через `ReportGenerator`; покриття ViewModel становить **70.47%** (при мінімальному порозі **≥ 60%**).
- **UI-тести (`PizzaTake.UiTests`):** 2 E2E-сценарії на Appium (`LoginFlow_ShouldAuthenticateAndOpenMainInterface` та `CreateAddressFlow_ShouldSaveNewAddressAndDisplayInSettings`) за стабільними `AutomationId`.
- **CI (`.github/workflows/ci.yml`):** два залежні джоби — `test-core` (юніт/інтеграційні тести + артефакт покриття) і `build-android` (`needs: test-core`, збірка Release `.apk` і публікація артефакту).

---

## 2. Сценарій усного захисту (5–7 хвилин)

1. **Вступ і архітектура (1 хв):** показати структуру `PizzaTake.slnx` (`Core` / `MAUI` / `Core.Tests` / `UiTests`), підкреслити `0` посилань на `Microsoft.Maui.*` у `Core`, чистий code-behind і єдиний DI у `MauiProgram.cs`.
2. **Демо клієнтського сценарію (1,5 хв):** онбординг міста й піцерії з вбудованою OSM-картою → каталог із банерами, фільтрами та акційною знижкою → картка піци з КБЖВ та добавками → кошик + оформлення («Доставка» / «На виніс») → історія замовлень.
3. **Безпека та RBAC (1,5 хв):** вхід як `Customer` (`+380991112233` / `1234`, адмін-кнопки приховані) → перемикання на `Administrator` (`+380990000001` / `1234`) → показ адмін-інструментів (редагування товару/КБЖВ/добавок, категорій, банерів, статусу замовлення, `LogsPage`) → показати `HttpApiClient` (SSL Pinning) та `UserProfileRepository` (AES-256).
4. **Апаратні фічі, офлайн та Sentry (1 хв):** `QualityReportPage` (камера/галерея + GPS + коректна обробка відмови дозволу), струшування (Shake), офлайн-банер з чергою синхронізації, перегляд логів у `LogsPage` і дашборд Sentry.
5. **Тести, покриття та CI/CD (1 хв):** показати зелений GitHub Actions pipeline (`test-core` + `build-android` APK), HTML-звіт `ReportGenerator` (покриття ViewModel > 70%) та Appium UI-тести.

---

## 3. Чекліст завершення Хвилі 4 (Dev 1 + Dev 2)

### Dev 1 (виконано у гілці `lab9`)
- [x] Фінальний аудит «анти-штрафів» (`0` MAUI API у `Core`, `0` `Shell.Current`/бізнес-логіки у `Views/*.xaml.cs`, `0` `Debug.WriteLine`, `0` секретів/IDE-сміття).
- [x] Усунуто єдине знайдене зауваження аудиту: переведено навігацію при струшуванні в `MenuPage.cs` та `OrderDetailPage.xaml.cs` на `IMainThreadDispatcher` + команду ViewModel.
- [x] Створено повний `README.md` (опис, архітектура, ER-схема, міграції, матриця RBAC, тестові акаунти, таблиця відповідності ЛР №1–8, інструкції).
- [x] Підготовлено відповіді на контрольні питання та сценарій усного захисту (`PizzaTake/docs/LAB9_DEFENSE_QA.md`).

### Dev 2 (паралельно у `lab9` перед фінальним релізом `v1.0`)
- [ ] Наскрізний smoke-тест на **Windows** і **Android** (обидві ролі, офлайн, апаратні фічі, Sentry, OSM, Deep Link).
- [ ] Скріншоти ключових екранів (Win+Android, ролі, офлайн, дозволи/відмова, фото, OSM, `LogsPage`, Sentry, HTML-покриття, зелений CI).
- [ ] Перевірка інсталяційного **Release `.apk`** із CI.
- [ ] Запис демо-відео (≤ 5 хв).
- [ ] Merge `lab9` → `master` + анотований тег `v1.0` (`git tag -a v1.0 -m "Lab 9: PizzaTake v1.0 release"`).

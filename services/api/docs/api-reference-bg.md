# OrionSearch API — подробно ръководство

**Версия на документа:** 31 октомври 2026 г.

**За кого е:** frontend и mobile разработчици, QA, backend екипа и хората, които интегрират OrionSearch.

**Обхват:** публичният ASP.NET Core API в текущия branch `feature/ai-research-report`, както и обяснение на връзката му с Python research worker-а.

Документът описва поведението на кода в този branch. Настройките и внедрената версия в дадена среда могат да се различават. Примерните UUID, дати, имейли, токени и резултати са измислени. Примерите не съдържат истински ключове. Статусът на Render по-долу е последно потвърден при теста на 30 октомври, а не обещание за текуща наличност.

При този тест API и worker-ът бяха внедрени от feature branch-а с промените в [PR #13](https://github.com/BolyarCoders/OrionSearch/pull/13). Проверете дали PR-ът е слят и кой commit работи в средата, преди да приемете, че всяко следващо внедряване съдържа endpoint-а за доклад.

## Съдържание

1. [Какво прави системата](#1-какво-прави-системата)
2. [Адреси и основни правила](#2-адреси-и-основни-правила)
3. [Удостоверяване и роли](#3-удостоверяване-и-роли)
4. [Грешки и ограничения](#4-грешки-и-ограничения)
5. [Всички endpoint-и](#5-всички-endpoint-и)
6. [Health проверки](#6-health-проверки)
7. [Акаунти и сесии](#7-акаунти-и-сесии)
8. [Архиви и настройки](#8-архиви-и-настройки)
9. [Проучвания](#9-проучвания)
10. [Източници, findings и доказателства](#10-източници-findings-и-доказателства)
11. [Версии на документи и raw файлове](#11-версии-на-документи-и-raw-файлове)
12. [Пълен пример за клиент](#12-пълен-пример-за-клиент)
13. [Вътрешна работа на backend-а](#13-вътрешна-работа-на-backend-а)
14. [Python worker и AI](#14-python-worker-и-ai)
15. [Конфигурация и deployment](#15-конфигурация-и-deployment)
16. [Практически проблеми и ограничения](#16-практически-проблеми-и-ограничения)
17. [Карта към изходния код](#17-карта-към-изходния-код)

## 1. Какво прави системата

OrionSearch организира информация в **архиви по теми**. Потребител създава архив и задава предпочитания за източниците. След това подава заявка за проучване. Основният API записва заявката в PostgreSQL и веднага връща идентификатор. Фонова обработка извиква Python worker-а, проверява резултата и го записва. Клиентът проверява статуса и при завършване може да прочете доклада и откритите източници.

**Понятия:**

| Понятие | Значение |
|---|---|
| Archive | Работно пространство по тема и граница за достъп. |
| Membership | Връзка между акаунт и архив с роля `owner`, `editor` или `viewer`. |
| Research run | Едно логическо проучване. Неговият UUID е публичният ID от API-то. |
| Job / attempt | Вътрешна задача и отделен опит за изпълнение. Не използвайте вътрешния job ID като публичен run ID. |
| Source / document | Открит URL с метаданни, свързан с архив. |
| Version / capture | Запазено съдържание на даден източник; един открит URL може да няма записана версия. |
| Finding / evidence | Отделно твърдение и връзката му към запазен материал. Не всеки AI доклад създава findings. |
| Report | Генериран текстов анализ на едно приключило проучване. |

Докладът използва метаданни и кратки feed описания и **не е проверка на фактите**. Не представяйте AI увереността като доказателство. Рендерирайте доклада като текст, а не като доверен HTML.

## 2. Адреси и основни правила

Публичен базов адрес, използван в последния deployment: `https://orionsearch-api.onrender.com`. Например пълният URL за вход е `https://orionsearch-api.onrender.com/auth/login`.

За локална среда адресът се определя от конфигурацията на приложението, например `http://localhost:5190`. Проверявайте реалния порт при стартиране.

Python услугата е отделна: `https://orionsearch-409q.onrender.com`. Frontend-ът обичайно **не я извиква**; той използва .NET API. Сървърният ключ към worker-а никога не трябва да се поставя в браузър, мобилно приложение, Git или примерни заявки.

| Правило | Поведение |
|---|---|
| Формат | Освен raw download, примерите обменят JSON. За JSON тяло изпращайте `Content-Type: application/json`. |
| Имена на полета | JSON използва `camelCase`: `archiveId`, `pageSize`, `refreshToken`. |
| ID | Ресурсите използват UUID. Невалиден UUID в маршрут с `:guid` обикновено не съвпада с маршрута и връща 404. |
| Дати | Дати във филтри: `YYYY-MM-DD`. Timestamps в отговорите са UTC и обикновено завършват с `Z`. |
| Непознати полета | DTO-тата за основните write заявки отхвърлят непознати JSON полета. Не изпращайте `ownerId`, `role` или `userId`. |
| Празно тяло | За endpoint с JSON тяло изпращайте валиден JSON, например `{}`, когато е допустимо. |
| Връзка | Използвайте HTTPS за публичната услуга. |
| Кеш | Акаунти, research и capture отговори използват `Cache-Control: no-store` на съответните маршрути. |

### Странициране

Стандартните параметри са `page=1` и `pageSize=20`. `page` е от 1 до 1 000 000, а `pageSize` — от 1 до 100. Невалидна стойност води до 400. Примерен отговор:

```json
{
  "items": [],
  "pageNumber": 1,
  "pageSize": 20,
  "totalCount": 0
}
```

`totalCount` е броят след филтрите и преди страницирането. Празният масив с HTTP 200 е нормален резултат, когато няма съвпадения.

## 3. Удостоверяване и роли

API използва ASP.NET Core Identity, когато `NativeIdentity__Enabled=true`: регистрация с имейл и парола, последвана от вход. Не се изисква имейл за потвърждение и не се използва външен доставчик за вход. Ако Identity е изключен, account маршрутите не се регистрират и защитените заявки не могат да се използват нормално.

При успешен вход се връщат **opaque bearer tokens** на ASP.NET Identity. Те не трябва да се третират като JWT и frontend-ът не трябва да се опитва да извлича идентичност от тях. Примерна форма:

```json
{
  "tokenType": "Bearer",
  "accessToken": "<access-token>",
  "expiresIn": 900,
  "refreshToken": "<refresh-token>"
}
```

При следващи защитени заявки:

```http
Authorization: Bearer <access-token>
```

В кода access token има срок 15 минути, а refresh token — 7 дни. При refresh съхранете нововърнатите токени. Logout и смяна на парола променят security stamp и обезсилват сесиите на акаунта. Паролите и токените не бива да се записват в логове или споделят в issue/чат.

| Действие | Owner | Editor | Viewer | Нечлен |
|---|---|---|---|---|
| Четене на архив и резултати | Да | Да | Да | 404 |
| Промяна на заглавие, описание, настройки | Да | Да | 403 | 404 |
| Промяна на статус / архивиране | Да | 403 | 403 | 404 |
| Стартиране на проучване | Да | Да | 403 | 404 |

Защитените маршрути изискват и активен акаунт. Нечлен и несъществуващ ресурс обикновено получават еднакъв 404, за да не се разкрива съществуването на чужд архив. Ролите съществуват в модела и се проверяват, но **публичен API за покани и управление на членове не е описан като наличен**.

## 4. Грешки и ограничения

Много грешки използват `application/problem+json` и могат да съдържат `type`, `title`, `status`, `instance`, `traceId` и при валидация `errors`. Формата зависи от мястото на грешката и механизма на ASP.NET; не разчитайте, че всяко поле съществува при всеки 401, 404, 413 или инфраструктурен отговор.

```json
{
  "title": "Title must contain 1–200 non-whitespace characters.",
  "status": 400,
  "instance": "/archives",
  "traceId": "примерен-trace-id",
  "errors": { "title": ["Title must contain 1–200 non-whitespace characters."] }
}
```

| HTTP | Значение и типични причини |
|---|---|
| 200 | Успешно четене или вход; празен списък също може да е 200. |
| 201 | Създаден акаунт или архив; `Location` сочи към `/me` или новия архив. |
| 202 | Проучването е прието и записано, но не непременно изпълнено. `Location` сочи към статуса. |
| 204 | Успех без тяло, например logout, смяна на парола или архивиране. |
| 400 | Невалидна стойност, невалиден JSON, непознато поле, неуспешна регистрация или смяна на парола. |
| 401 | Липсващ/невалиден/изтекъл токен, неактивен акаунт, неуспешен вход или невалиден refresh token. |
| 403 | Има членство, но ролята не разрешава действието. |
| 404 | Няма маршрут/ресурс, няма достъп като член, липсва версия или доклад; не правете извод кое от тях е вярно за чужд ресурс. |
| 409 | Конфликт: например неактивен архив при нова задача, различен body със същия idempotency key или несъгласувано собственичество. |
| 413 | Тялото надвишава приложимия лимит. |
| 429 | Превишена честота или капацитет; проверете `Retry-After`, ако е наличен. |
| 500 | Неочаквана сървърна грешка; публичният отговор не трябва да съдържа stack trace или тайни. |
| 503 | Например readiness не може да достигне базата, или raw съдържание липсва/не може да се провери в хранилището. |

При `/auth` лимитът за body е 32 768 байта, а при `POST /archives/{id}/research-jobs` — 16 384 байта. API не прилага лимити по честота или дневни квоти; запазени са ограниченията за капацитет на research опашката. Хостингът може да налага собствени ограничения. При 429 не правете непрекъснати незабавни повторения.

## 5. Всички endpoint-и

В таблицата „Bearer“ означава access token на потребителя, не worker ключ.

| Метод | Път | Bearer | Успех |
|---|---|---|---|
| GET | `/health` | Не | 200 |
| GET | `/health/ready` | Не | 200 / 503 |
| POST | `/auth/register` | Не | 201 |
| POST | `/auth/login` | Не | 200 |
| POST | `/auth/refresh` | Не | 200 |
| POST | `/auth/logout` | Да | 204 |
| POST | `/auth/change-password` | Да | 204 |
| GET | `/me` | Да | 200 |
| POST | `/archives` | Да | 201 |
| GET | `/archives` | Да | 200 |
| GET | `/archives/{id}` | Да | 200 |
| PATCH | `/archives/{id}` | Да | 200 |
| DELETE | `/archives/{id}` | Да | 204 |
| GET | `/archives/{id}/sources` | Да | 200 |
| POST | `/archives/{archiveId}/research-jobs` | Да | 202 |
| GET | `/research-jobs/{id}` | Да | 200 |
| GET | `/research-jobs/{id}/report` | Да | 200 |
| GET | `/archives/{archiveId}/research-jobs` | Да | 200 |
| GET | `/archives/{archiveId}/findings` | Да | 200 |
| GET | `/archives/{archiveId}/findings/{findingId}` | Да | 200 |
| GET | `/archives/{archiveId}/sources/{documentId}` | Да | 200 |
| GET | `/archives/{archiveId}/sources/{documentId}/versions` | Да | 200 |
| GET | `/archives/{archiveId}/sources/{documentId}/versions/{versionId}` | Да | 200 |
| GET | `/archives/{archiveId}/sources/{documentId}/versions/{versionId}/raw` | Да | 200 |

`GET /openapi/v1.json` се регистрира само в Development. Това не означава, че публичният production адрес има Swagger UI. Python worker-ът има отделен `/docs`; той не е документацията на публичния .NET API.

## 6. Health проверки

### `GET /health`

Без токен. Проверява, че HTTP приложението отговаря, не че всички зависимости работят.

```http
GET /health
```

**200:** `{"status":"healthy"}`. Успехът не доказва наличност на PostgreSQL, Gemini или Python worker-а.

### `GET /health/ready`

Без токен. Readiness включва PostgreSQL health check. Обичайно **200** с текст `Healthy`, ако проверката минава, или **503** с `Unhealthy`, ако базата не е достъпна. Не проверява всички миграции, Gemini, S3 или работата на опашката. Точният отговор при proxy/хостинг отказ може да е различен.

## 7. Акаунти и сесии

При невалиден JSON, непознати полета, 413, външен 429 или вътрешен отказ важат общите правила в раздел 4.

### `POST /auth/register`

Създава акаунт. Имейлът се изрязва от крайни интервали, валидира се и трябва да е уникален; максимум 254 символа. Паролата е 12–128 символа и се проверява и от правилата на Identity, включително сложността. Изберете силна парола с главни/малки букви, цифра и специален символ. `displayName` се получава от частта преди `@`, до 100 символа.

```http
POST /auth/register
Content-Type: application/json

{"email":"person@example.com","password":"Example-Strong-Password-42!"}
```

**201 Created**, `Location: /me`:

```json
{"id":"11111111-1111-4111-8111-111111111111","email":"person@example.com","displayName":"person"}
```

**400:** невалиден имейл/парола, нарушени password правила или невъзможна регистрация, включително дублиращ се акаунт. Регистрацията не връща токени — следва отделен login. Няма имейл за потвърждение.

### `POST /auth/login`

```http
POST /auth/login
Content-Type: application/json

{"email":"person@example.com","password":"Example-Strong-Password-42!"}
```

**200:** token response от раздел 3. **401:** грешни данни, несъществуващ/неактивен акаунт или временна блокировка. След 5 неуспешни опита Identity използва 5-минутен lockout. Публичният отговор не трябва да се използва за установяване дали даден имейл съществува. **429:** възможен отказ от хостинг или външна защита.

### `POST /auth/refresh`

```json
{"refreshToken":"<refresh-token>"}
```

**200:** нов access и refresh token. **401:** липсващ, невалиден, изтекъл, твърде дълъг или отменен refresh token, неактивен/блокиран акаунт. След logout или смяна на парола старият refresh token вече не трябва да се използва.

### `POST /auth/logout`

Изисква bearer access token; няма заявен JSON модел.

**204:** сесиите на акаунта са оттеглени чрез security stamp. **401:** няма валидна активна идентичност. **409:** конфликт при обновяване на акаунта; опитайте отново. Logout засяга всички сесии на този акаунт, а не само текущия браузър.

### `POST /auth/change-password`

Изисква bearer token:

```json
{"currentPassword":"Example-Strong-Password-42!","newPassword":"Another-Strong-Password-73!"}
```

**204:** паролата е променена и старите сесии са обезсилени; влезте отново. **400:** невалиден формат, грешна текуща парола или нова парола, която не минава Identity правилата. **401:** невалидна текуща сесия.

### `GET /me`

Изисква bearer token. **200:** профилът с `id`, `email`, `displayName`, както при регистрация. **401:** невалиден токен или неактивен/липсващ потребител. Данните за роля в конкретен архив идват от архивния ресурс, не от този отговор.

## 8. Архиви и настройки

### Модел на архив

Примерен `ArchiveDetails`:

```json
{
  "archive": {
    "id": "22222222-2222-4222-8222-222222222222",
    "title": "Artificial intelligence",
    "description": "AI developments",
    "status": "active",
    "role": "owner",
    "createdAt": "2026-10-30T08:00:00Z",
    "updatedAt": "2026-10-30T08:00:00Z",
    "sourceCount": 0
  },
  "settings": {
    "dateFrom": null,
    "dateTo": null,
    "languages": [],
    "preferredDomains": [],
    "excludedDomains": [],
    "config": {"maxArticles": 10},
    "updatedAt": "2026-10-30T08:00:00Z"
  }
}
```

Статуси: `active`, `paused`, `archived`. `sourceCount` брои връзките към източници за този архив, не задължително запазени версии. `role` е ролята на текущия потребител.

**Валидация на настройки:**

- `title`: задължително, след trim 1–200 символа; `description`: `null` или до 10 000 символа.
- `dateFrom`, `dateTo`: `null` или ISO дата; ако и двете са подадени, началото не може да е след края.
- `languages`: ISO 639-1 кодове, напр. `en`, `bg`; нормализират се до малки букви.
- При research заявките worker-ът запазва съвместимост с полетата за дата и език, но не ги използва за филтриране. AI избира английски заглавия и налични резюмета; неясният език се изключва, а езикът на пълния текст не се проверява. Адаптерите търсят в ограничен прозорец от последните 90 дни; резултати не са гарантирани.
- `preferredDomains`, `excludedDomains`: DNS имена без схема, порт и път; един домейн не може да е едновременно в двата списъка.
- Всеки масив допуска до 100 елемента; дубликати след нормализация и `null` вместо масив са невалидни.
- `config`: JSON обект до 16 384 UTF-8 байта. Worker-ът разпознава `maxArticles` — цяло число от 1 до 50, по подразбиране 20. Не поставяйте ключове, URL на worker-а или чувствителни данни в него.
- Съхраняването на настройки не изпълнява проучване. Някои настройки могат да са валидни при записа, но да не се поддържат от worker-а при изпълнение — например прозорец извън последните 90 дни.

### `POST /archives`

Изисква bearer token. Създава архив, настройки и owner membership в една транзакция.

```json
{
  "title": "Artificial intelligence",
  "description": "AI developments",
  "settings": {
    "dateFrom": null,
    "dateTo": null,
    "languages": [],
    "preferredDomains": [],
    "excludedDomains": [],
    "config": {"maxArticles": 10}
  }
}
```

`settings` може да се пропусне и тогава се използват празни настройки; изрично `settings: null` е невалидно. **201:** `ArchiveDetails`, с `Location: /archives/{id}`. **400:** невалидни полета, `null` където е забранен или непознато поле. **401:** невалидна идентичност. Сървърът определя собственика.

### `GET /archives`

Връща страница от достъпните архиви. **200:** `Page<ArchiveSummary>`. Параметри:

| Query | Значение |
|---|---|
| `page`, `pageSize` | Общите правила за странициране. |
| `status` | По избор: `active`, `paused`, `archived`. Невалиден статус → 400. |
| `includeArchived` | `true` включва архивирани, когато няма изричен филтър `status`; по подразбиране са скрити. |
| `topic` | Буквално търсене без значение на главни/малки букви в заглавие или описание; след trim 1–200 символа, без control символи. Това не е semantic search. |

Подреждане: `createdAt` низходящо, после ID. **401:** липсва активна идентичност. Няма достъп до архиви извън membership-а.

### `GET /archives/{id}`

**200:** `ArchiveDetails` с настройките. **401:** невалидна сесия. **404:** няма достъпен архив или маршрутът не съвпада с UUID. **409:** липсват настройки или собственикът и membership-ът са несъгласувани.

### `PATCH /archives/{id}`

Owner/editor могат да променят метаданни и настройки; само owner може да задава `status`. Пример:

```json
{
  "description": null,
  "settings": {
    "dateTo": null,
    "languages": ["en"],
    "config": {"maxArticles": 5}
  }
}
```

Пропуснатите полета се запазват. `description`, `dateFrom` и `dateTo` могат да се изчистят с `null`; `title`, `status`, `settings`, масивите и `config` не могат. Подаден масив или `config` **замества целия съответен масив/обект**, не се слива частично. След промените се валидират пълните настройки. **200:** обновен `ArchiveDetails`; **400:** невалидно съдържание; **401:** липсва идентичност; **403:** недостатъчна роля; **404:** няма достъп; **409:** вътрешна несъгласуваност на архива.

### `DELETE /archives/{id}`

Само owner. **204:** задава `status=archived`; това е soft archive и не изтрива историята или глобалните документи. Повторно извикване е допустимо. **401/403/404/409:** както при достъпа по-горе. За възстановяване owner може да изпрати `PATCH {"status":"active"}`. Архивиран архив не приема нови research заявки.

## 9. Проучвания

### `POST /archives/{archiveId}/research-jobs`

Owner/editor на активен архив. Използва се за асинхронна задача, без клиентът да чака AI отговора в същата HTTP заявка.

```http
POST /archives/22222222-2222-4222-8222-222222222222/research-jobs
Authorization: Bearer <access-token>
Content-Type: application/json
Idempotency-Key: ai-research-20261030-001

{"objective":"artificial intelligence"}
```

`objective` е опционален: `{}` или `{"objective":null}` използва заглавието на архива. Ако е зададен, не може да е празен/само интервали и е до 2000 символа. Настройките се snapshot-ват при приемането, така че последващ PATCH не променя вече записаната заявка.

`Idempotency-Key` е опционален, един header с 1–200 видими ASCII символа. За един архив същият ключ със същата заявка връща същия run, включително ако той вече е приключил. Същият ключ с различен `objective` връща **409**. Без ключ всяко подаване създава нов run. Когато objective е пропуснат, fingerprint остава за пропуснат objective дори заглавието да се промени по-късно.

**202 Accepted**, `Location: /research-jobs/{id}`, `Cache-Control: no-store`. Пример:

```json
{
  "id":"33333333-3333-4333-8333-333333333333",
  "archiveId":"22222222-2222-4222-8222-222222222222",
  "status":"queued",
  "createdAt":"2026-10-30T08:00:00Z",
  "startedAt":null,
  "finishedAt":null,
  "statistics":{"queries":0,"sources":0,"versions":0,"findings":0},
  "attempts":0,
  "maxAttempts":3,
  "nextAttemptAt":"2026-10-30T08:00:00Z",
  "retrying":false,
  "error":null
}
```

Значения на полетата: `id` е публичният run UUID; `statistics` брои приетите резултати; `attempts`/`maxAttempts` показват отделните опити; `nextAttemptAt` е време за следващ pending опит или `null`; `retrying` отличава повторен опит от първоначално чакане; `error` е безопасно обобщение или `null`. Часове и числови настройки в примера са илюстративни.

**400:** невалиден objective/key/body или повече от един key header. **401:** невалидна сесия. **403:** viewer. **404:** архивът не е достъпен. **409:** архивът не е active или key е използван с различно съдържание. **413:** body над лимита. **429:** изчерпан капацитет на опашката или отказ от хостинга; `Retry-After` може да подсказва кога да опитате. При успешно записан job не създавайте нова задача на всеки polling цикъл.

### `GET /research-jobs/{id}`

Owner/editor/viewer на архива. **200:** същият `ResearchStatus` модел. При `queued` и `running` отговорът включва `Retry-After: 2`; използвайте го като ориентир за polling. **401:** сесията е невалидна. **404:** run липсва или не е достъпен. **409:** несъгласуван архив.

| Статус | Какво означава |
|---|---|
| `queued` | Записан и чака изпълнение или следващ опит. |
| `running` | Worker е взел текущ опит. |
| `completed` | Резултатът е приет и записан. |
| `failed` | Опитите са изчерпани или има непоправима грешка. |
| `cancelled` | Задачата е отменена от lifecycle логиката, например при архивиране. |

Възможни безопасни `error` текстове включват timeout, недостъпна услуга, грешна service authentication, невалиден резултат, paused/archived архив или проблем при съхранението. Не очаквайте оригиналния exception или тайни в отговора.

### `GET /research-jobs/{id}/report`

Owner/editor/viewer. **200:**

```json
{"researchRunId":"33333333-3333-4333-8333-333333333333","text":"Примерен AI анализ [1]..."}
```

**404:** няма достъп, run не съществува, още не е `completed` или приключилото проучване няма доклад. **401:** невалидна сесия; при несъгласуван архив е възможен **409**. Използвайте статуса, за да различите изчакваща задача от приключила без доклад. Докладът е наличен само при съвместим worker и включено `ResearchWorker__IncludeReport`.

### `GET /archives/{archiveId}/research-jobs`

История за членове на архива. `page`, `pageSize`, опционален `status` (`queued`, `running`, `completed`, `failed`, `cancelled`). **200:** `Page<ResearchStatus>`, сортирана по `createdAt` низходящо и ID възходящо. **400:** невалидна страница/статус; **401/404/409:** идентичност, достъп или несъгласуваност. Историята не стартира повторно задачи.

## 10. Източници, findings и доказателства

### `GET /archives/{id}/sources`

Достъп за членове. Връща `Page<SourceSummary>` и включва открити източници без запазена версия.

| Query | Смисъл |
|---|---|
| `page`, `pageSize` | Странициране. |
| `publishedFrom`, `publishedTo` | Включителни UTC дати `YYYY-MM-DD`; неизвестна дата не отговаря на филтър по дата. Началото не може да е след края. |
| `language` | Валиден ISO 639-1 код; сравнява запазения език. |
| `publisherDomain` | Нормализиран домейн на издателя, без схема/порт/път. |
| `topic` | Буквален case-insensitive фрагмент от заглавието; 1–200 символа след trim. |
| `sort` | `added_desc` по подразбиране, `added_asc`, `published_desc`, `published_asc`, `relevance_desc`, `title_asc`. |

При nullable стойности за подредба липсващите стойности са накрая; `documentId` е допълнителен tie-breaker. **200:** страница; **400:** невалиден филтър/диапазон/подредба; **401/404/409:** сесия, достъп, консистентност.

Примерен елемент:

```json
{
  "documentId":"44444444-4444-4444-8444-444444444444",
  "canonicalUrl":"https://example.com/article",
  "title":"Example article",
  "publisherName":null,
  "publisherDomain":null,
  "language":null,
  "publishedAt":null,
  "firstSeenAt":"2026-10-30T08:00:00Z",
  "lastSeenAt":"2026-10-30T08:00:00Z",
  "relevanceScore":null,
  "addedAt":"2026-10-30T08:00:00Z"
}
```

`publishedAt: null` означава неизвестна дата, не непременно липса на статия. Metadata филтрите не са embeddings или пълнотекстово търсене.

### `GET /archives/{archiveId}/sources/{documentId}`

**200:** `{"source": <SourceSummary>, "versionCount": 0}`. Изисква членство и връзка между документа и този архив; глобално съществуване на документа не дава достъп. `versionCount` брои версиите, разрешени за конкретния архив. **401/404/409:** невалидна сесия, липсващ достъп/линк или несъгласуваност.

### `GET /archives/{archiveId}/findings`

Параметри: `page`, `pageSize`, `status` (`unverified`, `supported`, `disputed`, `superseded`), `type` (`claim`, `observation`, `estimate`) и опционален `runId`. Ако подаденият run не принадлежи на архива → **404**. Невалиден филтър → **400**.

**200:** страница от findings на завършили runs, подредена по `firstObservedAt` низходящо и ID възходящо. Примерен елемент:

```json
{
  "id":"55555555-5555-4555-8555-555555555555",
  "researchRunId":"33333333-3333-4333-8333-333333333333",
  "statement":"Примерно твърдение",
  "statementTruncated":false,
  "type":"observation",
  "status":"unverified",
  "confidence":null,
  "firstObservedAt":"2026-10-30T08:00:00Z",
  "evidenceCount":0
}
```

`statement` може да се отреже до 10 000 символа; `statementTruncated` сигнализира това. `confidence`, ако присъства, не гарантира истинност. Старите findings без run provenance не се показват. Празен списък е валиден дори при наличен AI доклад: текущият worker не превръща автоматично анализа във findings.

### `GET /archives/{archiveId}/findings/{findingId}`

**200:** `{"finding": <FindingSummary>, "evidence": <Page<EvidenceReference>>}`. `page` и `pageSize` се отнасят за evidence, не за самия finding. Evidence съдържа `id`, `documentId`, `versionId`, nullable `chunkId` и `chunkIndex`, `stance`, nullable `quote`, `quoteTruncated`, `canonicalUrl`, nullable `sourceTitle`. Достъпни са само evidence записи с разрешена за архива версия; цитатът се ограничава до 10 000 символа, заглавието — до 2000. **400:** невалидна страница; **401/404/409:** идентичност, липса на достъп/ресурс или несъгласуваност.

## 11. Версии на документи и raw файлове

Всички маршрути изискват активен потребител и членство. Версията трябва да е изрично разрешена за архива; същият глобален URL в друг архив не дава автоматичен достъп до всички версии.

### `GET /archives/{archiveId}/sources/{documentId}/versions`

`page`, `pageSize` са стандартни. **200:** `Page<CapturedVersionSummary>` с елементи `id`, `fetchedAt`, `hasText`, `hasRaw`, подредени по `fetchedAt` низходящо и ID възходящо. Открит източник без captures връща празна страница. **400:** невалидна страница; **401/404/409:** идентичност, недостъпен архив/източник или несъгласуваност.

### `GET /archives/{archiveId}/sources/{documentId}/versions/{versionId}`

**200:** `CapturedVersion` с `id`, `documentId`, `fetchedAt`, `contentHash`, nullable `extractedText`, `hasRaw` и `chunks`. Всеки chunk има `index`, `text`, nullable `startOffset`, `endOffset`. Offset-ите броят Unicode scalar values в нормализирания текст, не UTF-16 code units или байтове. Вътрешният storage URI и ключовете не се връщат. **401/404/409:** невалидна сесия, липсващ grant/ресурс или несъгласуваност.

### `GET /archives/{archiveId}/sources/{documentId}/versions/{versionId}/raw`

**200:** файлови байтове като `application/octet-stream` attachment с име `capture-{versionId}.bin`, `Cache-Control: no-store`, `X-Content-Type-Options: nosniff` и sandbox CSP. Сървърът проверява immutable версията и SHA-256 преди да върне байтове. Не рендерирайте source HTML като доверено съдържание. **404:** няма достъп, версия или raw запис; **503:** хранилището не е достъпно или integrity проверката не може да мине; **401/409:** сесия/консистентност. Наличието на тези endpoint-и не означава, че всеки източник има raw capture или че upload в live среда е потвърден.

## 12. Пълен пример за клиент

Примерът е за Bash-подобен shell с `curl`; в PowerShell синтаксисът за променливи и пренасяне на ред е различен. Използвайте собствен тестов акаунт и подменете стойностите; не оставяйте реални пароли и токени в shell history, source code или frontend build. Регистрацията и създаването на архив записват данни.

```bash
export API='https://orionsearch-api.onrender.com'

# При вече създаден акаунт: предайте данните по защитен начин.
curl -sS "$API/auth/login" \
  -H 'Content-Type: application/json' \
  -d '{"email":"person@example.com","password":"Example-Strong-Password-42!"}'

# Поставете accessToken от отговора в променлива чрез защитен механизъм.
export ACCESS_TOKEN='<access-token>'

curl -sS "$API/archives" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"title":"Artificial intelligence","settings":{"config":{"maxArticles":3}}}'

# Използвайте archive.id от отговора.
export ARCHIVE_ID='<archive-uuid>'
curl -i "$API/archives/$ARCHIVE_ID/research-jobs" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: client-generated-unique-key' \
  -d '{"objective":"artificial intelligence"}'

# Използвайте id от 202 отговора и poll-вайте с интервал.
export RUN_ID='<research-run-uuid>'
curl -sS "$API/research-jobs/$RUN_ID" -H "Authorization: Bearer $ACCESS_TOKEN"
curl -sS "$API/research-jobs/$RUN_ID/report" -H "Authorization: Bearer $ACCESS_TOKEN"
curl -sS "$API/archives/$ARCHIVE_ID/sources?page=1&pageSize=20" -H "Authorization: Bearer $ACCESS_TOKEN"
```

Клиентът следва да изчака статус `completed`, преди да очаква доклад. При `queued`/`running` използвайте `Retry-After`, backoff и ограничен брой заявки; при `failed` покажете безопасния `error`. При 401 обновете сесията чрез refresh или поискайте нов вход. При 429 спазете `Retry-After`. При timeout на `POST` повторете със **същия** idempotency key и body, за да не създадете дубликат.

## 13. Вътрешна работа на backend-а

1. API удостоверява потребителя и проверява ролята в PostgreSQL.
2. При заявка за research в една транзакция се записват run, queue job и snapshot на настройките. Връщането на 202 следва успешен commit.
3. Включеният .NET processor взема задача с lease; няколко изпълнителя използват заключвания и не трябва да обработват един и същ опит като собственик едновременно.
4. Processor изпраща versioned server-to-server заявка към Python с отделен service credential. Потребителският access token не се изпраща към worker-а.
5. Worker търси в конфигурирани източници, филтрира резултатите и при заявен report извиква Gemini. Той няма credentials за PostgreSQL.
6. .NET валидира идентичността на run-а, формата, размерите и изхода. При capture може да провери storage bytes и hash. Приета информация се записва транзакционно с финализирането на текущия lease.
7. При временни откази се използват ограничени повторни опити с backoff. Изтекъл lease се възстановява; остарял изпълнител не трябва да записва късен резултат.

Paused архив не стартира ново чакащо изпълнение, но вече изпратен опит може да приключи. Archived архив не приема нови заявки и не допуска неподходящо късно записване. Публичният API връща опростено състояние, а не queue payload, вътрешни worker ID или provider exception.

**Източник на истината:** PostgreSQL пази акаунти, членства, архиви, runs, опити, метаданни и приетия report. S3-съвместимото object storage е отделно за raw bytes, когато е конфигурирано. Backend пази права на ниво архив и проверява version grants. Откриването на URL не доказва, че пълният материал е запазен.

## 14. Python worker и AI

Интегрираният вътрешен маршрут е `POST /research` на worker-а с `Authorization: Bearer <service-key>` и JSON договор `orion.research.v1`. Заявката съдържа `contractVersion`, `researchRunId`, `archiveId`, `title`, `description`, `objective`, snapshot на `settings` и опционално `includeReport`. Отговорът връща същите версия и run ID, `queries`, `sources`, `findings` и при поискване `report`. .NET отказва несъвпадащи ID/версия или невалиден отговор.

Това е **вътрешен договор**, не endpoint за потребителския frontend. Worker `GET /health` е публична проверка на процеса; `/docs` е неговият Swagger UI. По-старите `/prototype/research` и `/prototype/research/jobs` изискват отделен opt-in и `X-API-Key`; тяхното in-memory job състояние не е трайна продуктова опашка.

### Интегрираните worker маршрути

| Метод и път | Достъп | Предназначение и резултат |
|---|---|---|
| `GET /health` | Без ключ | **200** с `{"status":"ok","service":"research-worker"}`; не проверява Gemini, източници или квоти. |
| `GET /docs`, `GET /openapi.json` | Без worker ключ | Swagger UI и машинната спецификация на FastAPI. Те описват Python услугата, не .NET API. |
| `POST /research` | `Authorization: Bearer <worker-service-key>` | Синхронна вътрешна операция; при успех **200** с versioned резултат. Крайният потребител продължава да използва асинхронното .NET API. |

Илюстративен body за сървърния `POST /research`:

```json
{
  "contractVersion":"orion.research.v1",
  "researchRunId":"33333333-3333-4333-8333-333333333333",
  "archiveId":"22222222-2222-4222-8222-222222222222",
  "title":"Artificial intelligence",
  "description":null,
  "objective":"artificial intelligence",
  "settings":{
    "dateFrom":null,
    "dateTo":null,
    "languages":[],
    "preferredDomains":[],
    "excludedDomains":[],
    "config":{"maxArticles":3},
    "updatedAt":"2026-10-30T08:00:00Z"
  },
  "includeReport":true
}
```

Примерна съкратена форма на успешен резултат без captures/findings:

```json
{
  "contractVersion":"orion.research.v1",
  "researchRunId":"33333333-3333-4333-8333-333333333333",
  "queries":[{"query":"artificial intelligence","provider":"hacker_news"}],
  "sources":[{"key":"source_example","canonicalUrl":"https://example.com/article","title":"Example"}],
  "findings":[],
  "report":"Примерен анализ [1]."
}
```

`report` присъства само когато е поискан от съвместима версия. `sources` може да съдържа publisher, дата и други допустими полета от схемата; ключът е идентификатор за препратки вътре в резултата, а не content hash. Пълният договор е в [схемите за v1](../../../packages/contracts/research/v1/README.md). Успешен резултат не винаги има findings или версии.

| Worker отговор | Защо |
|---|---|
| 400 | Невалиден JSON, дублирани полета, непозната версия/полета или невалиден request според схемата и семантичните проверки. |
| 401 | Липсващ, неправилен или невалиден Bearer service credential. |
| 413 | Заявката надвишава 256 KiB. |
| 415 | Content-Type не е `application/json`. |
| 422 | Неподдържани настройки или резултат, който не минава проверките на worker-а. |
| 503 | Липсваща service конфигурация, зает worker или временен отказ при източник/AI изпълнение; възможен е `Retry-After`. |

Мрежа, proxy или неочаквана грешка могат да доведат и до други отговори. .NET преобразува грешките в ограничени категории и не препраща частните provider съобщения към потребителя.

### Опционалните prototype маршрути

Когато операторът изрично зададе `ORION_ENABLE_PROTOTYPE_API=true`, старите маршрути използват `X-API-Key`, а не интегрирания Bearer договор:

| Метод и път | Поведение |
|---|---|
| `POST /prototype/research` | Body `{"topic":"Artificial intelligence","days":7,"max_articles":10}`; **200** с пълния prototype резултат, обичайно `topic`, `days`, `queries`, `sources`, `total_articles`, `articles`, `report`, `errors`, `generated_at`. Невалидни полета обичайно → **422**; вътрешен отказ → **502**. |
| `POST /prototype/research/jobs` | Същият body; **202** с `job_id`, `status` и относителен `status_url`; заета опашка → **503**. |
| `GET /prototype/research/jobs/{job_id}` | **200** с `queued`, `running`, `completed` или `failed`, резултат/грешка; липсващ job → **404**. |

`topic` е 2–180 символа, `days` 1–90 с default 7, а `max_articles` 1–50 с default 20. Изключен prototype → **404**, невалиден ключ → **401**, липсващ worker ключ → **503**. Имената на тези JSON полета са `snake_case`. Prototype задачите са в паметта на процеса и могат да изчезнат при рестарт; не ги използвайте за трайност, гарантирана доставка или директно като публичния job API на приложението.

Описаната конфигурация на 30 октомври използваше само `hacker_news`, защото Google News върна 503. Основният модел беше `gemini-3.5-flash-lite`; достъпността на източниците и модела може да се промени. Вградената логика поддържа прозорец до 90 дни и по подразбиране последните 7; при явни ограничения неизвестни дати/езици може да бъдат изключени. Необработените feed обобщения не са гарантиран пълен текст, а анализът може да съдържа неточности. Генерираните доклади не означават, че имате автоматично проверени findings или доказателства.

## 15. Конфигурация и deployment

Следните настройки са сървърни и се предоставят чрез средата/secret manager-а, не чрез frontend. Таблицата дава имена и ролята им, без стойности на тайни.

| Настройка | Предназначение |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | Например `Development` или `Production`; влияе и на OpenAPI. |
| `DATABASE_URL` или `ConnectionStrings__OrionSearch` | Връзка към PostgreSQL; данните за достъп са тайна. |
| `NativeIdentity__Enabled` | Регистрира account маршрутите и активира Identity. |
| `NativeIdentity__KeyEncryptionCertificateBase64` / `...Path` и `...Password` | Защитават persisted Data Protection ключове извън Development. Не настройвайте едновременно base64 и path. |
| `NativeIdentity__RequestsPerMinute` | Остаряла настройка, която вече не се прилага. |
| `Cors__AllowedOrigins__0`, `__1`, … | Изрично разрешени frontend origins. CORS не заменя authentication. |
| `ReverseProxy__KnownProxies`, `ReverseProxy__TrustRenderEdge` | Настройки за доверени проксита; Render режимът вече не се използва за лимитиране на заявки. |
| `ResearchWorker__Enabled` | Включва обработката; записването на job и изпълнението са отделни стъпки. |
| `ResearchWorker__AgentBaseAddress`, `ResearchWorker__AgentApiKey` | Адрес и сървърен ключ към Python worker-а. |
| `ResearchWorker__IncludeReport` | Изрично иска report от съвместим worker. |
| `ResearchWorker__TimeoutSeconds`, `LeaseSeconds`, `HeartbeatSeconds`, `PollSeconds` | Таймаути, lease и polling на опашката. |
| `ResearchWorker__MaxAttempts`, `RetryBaseSeconds`, `RetryMaxSeconds`, `MaxResponseBytes` | Лимити за опити, backoff и размер на отговор. |
| `ResearchLimits__...` | Капацитет на чакащите заявки и едновременните опити; дневните и минутните настройки вече не се прилагат. |
| `CaptureStore__...` | Endpoint, bucket, регион и credentials за versioned S3-съвместимо хранилище, когато е активирано. |
| Worker: `ORION_WORKER_API_KEY`, `GEMINI_API_KEY`, `GEMINI_MODEL`, `GEMINI_FALLBACK_MODEL`, `RESEARCH_SOURCES` | Конфигурация на отделния Python процес. Gemini ключът не се дава на .NET клиента. |

Примерни **кодови defaults**, не гарантирани live стойности: lease 120 s, heartbeat 30 s, processor poll 5 s, общ timeout 300 s, до 3 опита, backoff база 10 s, максимум 300 s и отговор до 2 MiB. Research admission допуска по подразбиране глобално 100 чакащи или изпълнявани задачи, 10 на owner, 5 на архив и 1 едновременен опит; конфигурацията може да ги промени.

Startup **не прилага EF миграции автоматично**. Миграциите се планират, архивират и прилагат изрично от оператор. Последният описан deployment използваше Render Free; свободните web услуги могат да заспиват, което забавя или прекъсва фонова обработка до следващо събуждане. Базата е имала срок до 26 ноември 2026 г. в 08:35 ч. българско време и няма управляван backup на Free плана — проверете актуалното състояние в Render. Локален криптиран backup не заменя независим, редовно проверяван backup.

## 16. Практически проблеми и ограничения

| Симптом | Какво да проверите |
|---|---|
| `/health` е 200, но `/health/ready` е 503 | Връзка/състояние на PostgreSQL; liveness не проверява базата. |
| `/auth/login` връща 404 | Дали Identity е включен и дали е внедрена версията с account endpoint-и. |
| Login или защитен маршрут връща 401 | Парола, lockout, токен, expiry, security stamp и активен акаунт. |
| Архив връща 404 | Невалидно UUID, липсващ архив или липса на membership; не разчитайте да различите чужд ресурс. |
| PATCH връща 403 | Viewer няма write права; editor не може да променя status. |
| Research POST връща 409 | Архивът не е active, има конфликтен idempotency key или ownership inconsistency. |
| Research POST връща 429 | Капацитет на опашката или външна защита; проверете отговора и `Retry-After`. |
| Run стои `queued` | Processor е изключен/спи, архивът е paused, капацитетът е изчерпан или предстои retry. 202 не гарантира завършване. |
| Run е `failed` | Вижте безопасното `error`; операторът проверява worker/provider логовете без да разкрива тайни. |
| Report е 404 при `completed` | По-стар run, report функцията е изключена или отчет не е приет; проверете конфигурацията/версията. |
| Празни sources/findings/versions | Възможно е няма съвпадения; findings и captures не се създават задължително. |
| Raw download е 503 | Обектът не може да се изчете или да се провери; не заобикаляйте hash проверката. |
| Браузърът блокира заявка, а curl работи | Проверете правилния frontend origin в CORS настройките и схемата/порта. |
| Нов код липсва след deploy | Проверете конкретния commit и конфигурация на услугата. PR branch и внедрен commit не означават автоматично, че промените са в `main`. |

Няма гаранция за непрекъснато събиране на нови материали: периодичен scheduler, пълно цифрово списание, потребителски cancel endpoint и semantic search не са описани като завършени функции на този API. Това ръководство не твърди, че всички тестове или всички live интеграции са преминали при всяка следваща промяна.

## 17. Карта към изходния код

| Тема | Файлове |
|---|---|
| Маршрути | [Endpoints](../src/OrionSearch.Api/Endpoints/) |
| Архивни request модели | [ArchiveRequests.cs](../src/OrionSearch.Api/Archives/ArchiveRequests.cs) |
| Валидация и архиви | [ArchiveValidation.cs](../src/OrionSearch.Application/Archives/ArchiveValidation.cs), [ArchiveService.cs](../src/OrionSearch.Application/Archives/ArchiveService.cs) |
| Identity | [NativeIdentityExtensions.cs](../src/OrionSearch.Api/Authentication/NativeIdentityExtensions.cs), [native-identity.md](native-identity.md) |
| Research API и модели | [ResearchEndpoints.cs](../src/OrionSearch.Api/Endpoints/ResearchEndpoints.cs), [ResearchContracts.cs](../src/OrionSearch.Application/Research/ResearchContracts.cs) |
| Опашка и запис | [EfResearchQueue.cs](../src/OrionSearch.Infrastructure/Research/EfResearchQueue.cs), [ResearchProcessor.cs](../src/OrionSearch.Infrastructure/Research/ResearchProcessor.cs) |
| Python договор и rollout | [research-integration.md](research-integration.md), [contract README](../../../packages/contracts/research/v1/README.md) |
| Capture storage | [capture-storage.md](capture-storage.md) |
| Предишно историческо ръководство | [api-and-backend-guide.md](api-and-backend-guide.md) — написано за по-ранен snapshot; при противоречие сверявайте с текущия код. |

При промяна на API актуализирайте маршрутите, примерите, ограниченията и статуса на внедряване в този документ заедно с кода.

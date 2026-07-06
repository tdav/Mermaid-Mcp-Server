# MermaidMcpServer — дизайн

**Дата:** 2026-07-06
**Статус:** утверждён (ожидает финального ревью спецификации)

## 1. Обзор

Одно приложение **ASP.NET Core (.NET 10)**, которое совмещает две роли:

1. **Веб-UI** — рендер Mermaid-диаграмм: слева `<textarea>` с кодом, справа визуализация.
2. **MCP-сервер по SSE** — инструменты для ИИ-клиентов: построить, отрендерить и провалидировать Mermaid-диаграммы.

Ключевой технический факт: `FoggyBalrog.MermaidDotNet` **генерирует Mermaid-текст** (`Mermaid.Flowchart()...Build()` → `string`), но **не рисует** его. Визуальный рендер выполняет `mermaid.js`. Отсюда — гибридная схема рендеринга:

- **client-side** (`mermaid.js` в браузере) — для веб-UI;
- **server-side** (headless-браузер **PuppeteerSharp**, Chromium скачивается по требованию) — для отдачи SVG/PNG из MCP. Вынесено в **опциональный** сервис, включается флагом конфигурации; по умолчанию выключен → лёгкий старт без Chromium.

### Поддержка типов диаграмм (~18)

MCP-сервер поддерживает **все ~18 типов диаграмм**, которые умеет FoggyBalrog:

Flowchart, Sequence diagram, Class diagram, State diagram, Entity relationship diagram, User journey diagram, Gantt diagram, Git graph, Mind map, Kanban diagram, Packet diagram, Pie chart, Quadrant chart, Requirement diagram, Timeline diagram, Sankey diagram, XY chart, Block diagram.

Покрытие идёт по двум путям:

- `render_mermaid` / `validate_mermaid` принимают **сырой Mermaid-код** → работают со **всеми 18 типами сразу** (в т.ч. длинный хвост);
- **структурные билдеры** (`create_*`) на FoggyBalrog добавляются **по фазам** (см. §7) для удобной генерации без ручного написания синтаксиса.

## 2. Архитектура

```
                    ┌─────────────────────────────────────────┐
   Браузер  ──────► │  ASP.NET Core (.NET 10)                  │
   (веб-UI)         │                                          │
                    │  ── Web ──                               │
                    │   GET  /              index.html (шелл)  │
                    │   POST /render        HTML-фрагмент      │
                    │   GET  /examples/{t}  Mermaid-код        │
                    │                                          │
   ИИ-клиент ─SSE─► │  ── MCP ──  MapMcp (legacy SSE)          │
   (Claude и др.)   │   GET  /sse           поток событий      │
                    │   POST /message       вызовы             │
                    │   tools: render_mermaid, validate_...,   │
                    │          create_* (FoggyBalrog)          │
                    │                                          │
                    │  ── Сервисы ──                           │
                    │   HeadlessRenderer (PuppeteerSharp,      │
                    │     lazy, opt-in) — SVG/PNG/parse        │
                    │   ExampleCatalog (FoggyBalrog)           │
                    └─────────────────────────────────────────┘
```

Единицы и их границы:

- **Web-эндпоинты** — отдают шелл и HTML-фрагменты для HTMX. Зависят от `ExampleCatalog`. Не знают про MCP.
- **MCP-инструменты** — тонкие обёртки: билдеры → `FoggyBalrog`; `render/validate` → `HeadlessRenderer`. Не знают про Web.
- **HeadlessRenderer** — единственный владелец Chromium. Ленивая инициализация при первом обращении; общий для `render(svg/png)` и `validate`. Опционален (флаг `Rendering:EnableServerRender`).
- **ExampleCatalog** — генерирует примеры кода на каждый тип через FoggyBalrog. Используется веб-UI (`/examples/{type}`).

## 3. Веб-UI

- **Шелл**: статический `wwwroot/index.html`. Сетка Tailwind `grid-cols-12`: слева `<textarea>` `col-span-2`, справа канва `#canvas` `col-span-10` (соотношение 12-2 / 12-10, как задано).
- **HTMX-поток рендера**:
  1. `<textarea>` с `hx-post="/render"`, `hx-trigger="keyup changed delay:400ms, load"`, `hx-target="#canvas"`.
  2. Сервер санитизирует ввод, возвращает фрагмент `<pre class="mermaid">…экранированный код…</pre>`.
  3. Хук `htmx:afterSwap` вызывает `mermaid.run({ nodes: [#canvas pre] })`.
  4. Ошибку парсинга `mermaid` ловим на клиенте → панель ошибки в стиле `sem-danger` (текст + иконка, не только цвет).
- **Дизайн-система «The Precision Instrument»** (импорт из `DESIGN.json` / `DESIGN.md`):
  - токены → CSS-переменные: `:root` (тёмная тема) + `[data-theme="light"]` (светлая);
  - плоская elevation, hairline-границы 1px, **Ember Orange только для действий** (кнопка Render, активная вкладка, focus-кольца);
  - шрифты Inter (UI) + JetBrains Mono (код/технические значения);
  - переключатель тёмная/светлая темы (обе полноценны, семантика идентична);
  - тема `mermaid.js` подстроена под палитру (нейтральные поверхности, ember-акцент дозированно);
  - без neumorphism / glassmorphism / декоративных теней; `prefers-reduced-motion` отключает анимации.
- **Дополнительно**: выпадающий список примеров (`/examples/{type}` → код в `<textarea>`), экспорт SVG и PNG (client-side), копирование кода.
- **Вендоринг (без CDN в рантайме для JS)**: `wwwroot/lib/htmx.min.js`, `wwwroot/lib/mermaid.min.js`.
- **Tailwind**: по умолчанию **Play-CDN + inline `tailwind.config`** с маппингом токенов дизайн-системы (нулевая сборка — это локальный dev-инструмент). Офлайн-путь: собрать `wwwroot/css/app.css` standalone-бинарём Tailwind (без npm) и закоммитить; команда сборки — в README.

> ⚠️ Замечание по эргономике: `col-span-2` ≈ 17% ширины — узко для редактора кода. Оставлено как задано; изменение соотношения (напр. 3/9 или 4/8) — правка одной CSS-строки.

## 4. MCP-сервер (SSE)

Регистрация: `AddMcpServer().WithHttpTransport(...).WithTools<...>()`; `app.MapMcp()` с включённым **legacy SSE** → эндпоинты `/sse` (поток) и `/message` (вызовы). Современный Streamable-HTTP остаётся на корне для новых клиентов; в README акцент на SSE.

Метаданные сервера явно сообщают: **поддержка ~18 типов диаграмм Mermaid**.

### Инструменты

| Инструмент | Параметры | Результат |
| --- | --- | --- |
| `render_mermaid` | `code`, `format = code \| svg \| png` | `code`: Mermaid-текст + ссылка предпросмотра `/?code=<base64url>` (stateless, без хранилища). `svg`: SVG-текст. `png`: изображение (чтобы модель «видела» диаграмму). |
| `validate_mermaid` | `code` | ok / список синтаксических ошибок (через `mermaid.parse` в headless). |
| `create_flowchart`, `create_sequence_diagram`, `create_class_diagram`, `create_state_diagram`, `create_er_diagram`, `create_gantt`, … | структурные параметры типа | Валидный Mermaid-код (FoggyBalrog). По одному инструменту на тип, добавляются по фазам. |

`render(svg/png)` и `validate` требуют `HeadlessRenderer`. Если серверный рендер выключен — инструменты возвращают понятный ответ «server rendering disabled» (не падают); `render(code)` и билдеры работают всегда.

## 5. Потоки данных

**Веб-рендер:** ввод в `<textarea>` → HTMX `POST /render` → фрагмент `<pre class="mermaid">` → `mermaid.run()` → SVG в `#canvas`.

**MCP build+preview:** ИИ вызывает `create_flowchart(...)` → FoggyBalrog собирает код → (опц.) `render_mermaid(code, format=code)` → код + ссылка предпросмотра, которую можно открыть в браузере.

**MCP render-image:** ИИ вызывает `render_mermaid(code, format=png)` → `HeadlessRenderer` (Chromium + `mermaid.js`) → PNG в ответе.

## 6. Обработка ошибок

- Некорректный код: UI — панель `sem-danger` с текстом ошибки; MCP — структурный ответ об ошибке, без исключений наружу.
- Серверный рендер выключен, но запрошен svg/png/validate → ответ «server rendering disabled».
- Пустой ввод → пустая канва / no-op, без ошибок.

## 7. Фазность реализации

1. **Фаза 1 (каркас):** проект, DI, веб-шелл + `/render` + `/examples`, дизайн-система, MCP по SSE с `render_mermaid`(code) + `validate_mermaid`, `HeadlessRenderer` (opt-in). Все 18 типов доступны через сырой код. README.
2. **Фаза 2 (частые билдеры):** `create_*` для 6 типов — flowchart, sequence, class, state, ER, gantt.
3. **Фаза 3 (полный набор):** билдеры для остальных типов до полного покрытия ~18.

## 8. Структура файлов

```
MermaidMcpServer.csproj        FoggyBalrog.MermaidDotNet + ModelContextProtocol.AspNetCore + PuppeteerSharp
Program.cs                     host, DI, /render, /examples, MapMcp (SSE)
Mcp/MermaidTools.cs            render_mermaid, validate_mermaid
Mcp/DiagramBuilderTools.cs     create_* (FoggyBalrog)
Rendering/HeadlessRenderer.cs  PuppeteerSharp, lazy, opt-in
Examples/ExampleCatalog.cs     примеры по типам (FoggyBalrog)
wwwroot/index.html             двухпанельный шелл
wwwroot/js/app.js              тема, экспорт, инициализация mermaid, HTMX-хуки
wwwroot/lib/htmx.min.js
wwwroot/lib/mermaid.min.js
appsettings.json               Rendering:EnableServerRender и т.п.
README.md                      описание проекта + установка MCP по SSE
```

Соглашения кода: приватные поля без префикса `_`, обращение через `this.`; идентификаторы и комментарии — на английском.

## 9. README (состав)

- Что за проект, скриншот двухпанельного UI.
- Запуск: `dotnet run`, адрес веб-UI.
- **Установка MCP по SSE**: URL `http://localhost:<port>/sse`; пример конфигурации клиента:
  ```json
  { "mcpServers": { "mermaid": { "type": "sse", "url": "http://localhost:5000/sse" } } }
  ```
- Список инструментов и **явное указание: поддержка ~18 типов диаграмм**.
- Включение серверного рендера (`Rendering:EnableServerRender`) и загрузка Chromium.
- Офлайн-сборка Tailwind (опц.).

## 10. Тестирование

По правилу проекта — **перед написанием любых тестов остановиться и спросить**. Точка решения: смок-проверка генерации кода FoggyBalrog и маппинга параметров билдеров. Ничего тестового не создаётся без явного подтверждения.

## Открытые вопросы

Нет — три развилки (соотношение 2/10, доставка Tailwind, фазность) согласованы.

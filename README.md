# MermaidMcpServer

Сервер рендеринга Mermaid-диаграмм с двухпанельным веб-UI (слева код, справа диаграмма)
и встроенным MCP-сервером по протоколу SSE. Рендер в браузере — `mermaid.js`;
генерация кода — `FoggyBalrog.MermaidDotNet`; опциональный серверный рендер в SVG/PNG — PuppeteerSharp.

## Возможности
- Двухпанельный редактор: `<textarea>` (col-span-2) + канва (col-span-10), HTMX + Tailwind.
- Тёмная/светлая темы (дизайн-система «The Precision Instrument», импорт из DESIGN.json/DESIGN.md).
- MCP-сервер по SSE с поддержкой ~18 типов диаграмм Mermaid.

## Запуск
```bash
dotnet run --project src/MermaidMcpServer
```
Веб-UI: http://localhost:5000

## MCP-сервер (SSE)
Эндпоинт SSE: `http://localhost:5000/sse` (плюс `/message` для POST-сообщений).
`GET /` отдаёт веб-UI (страницу с редактором диаграмм), а не MCP-протокол. Клиенты с
поддержкой Streamable HTTP используют `POST /` на корне; рекомендуемый и документированный
здесь транспорт — SSE (`/sse`).

Важно: SSE как транспорт — устаревающий вариант без встроенного backpressure (клиент не
может притормозить сервер при перегрузке очереди сообщений); это осознанный выбор для
доверенных клиентов и локального использования, а не для публичного продакшена.

Пример конфигурации клиента (Claude Code / совместимые):
```json
{ "mcpServers": { "mermaid": { "type": "sse", "url": "http://localhost:5000/sse" } } }
```

### Инструменты
- `render_mermaid(code, format=code|svg|png)` — предпросмотр/рендер. Поддержка ~18 типов диаграмм.
- `validate_mermaid(code)` — проверка синтаксиса (требует серверного рендера).
- `create_flowchart(...)` и другие билдеры (добавляются по фазам).

Поддерживаемые типы (~18): flowchart, sequence, class, state, ER, user journey, gantt,
git graph, mind map, kanban, packet, pie, quadrant, requirement, timeline, sankey, xy, block.

## Серверный рендер (SVG/PNG)
Выключен по умолчанию. Включить: в `appsettings.json` → `"Rendering": { "EnableServerRender": true }`.
При первом запросе svg/png/validate PuppeteerSharp скачает Chromium (~150 МБ).

## Офлайн Tailwind (опционально)
По умолчанию используется Tailwind Play-CDN. Для офлайна соберите статический `app.css`
standalone-бинарём Tailwind и подключите вместо CDN-скрипта в `wwwroot/index.html`.

# MermaidMcpServer

Сервер рендеринга Mermaid-диаграмм с двухпанельным веб-UI (слева код, справа диаграмма)
и встроенным MCP-сервером (Streamable HTTP). Рендер в браузере — `mermaid.js`;
генерация кода — `FoggyBalrog.MermaidDotNet`; опциональный серверный рендер в SVG/PNG — `Mermaider` (чистый .NET, без Chromium).

## Возможности
- Двухпанельный редактор: `<textarea>` (col-span-2) + канва (col-span-10), HTMX + Tailwind.
- Тёмная/светлая темы (дизайн-система «The Precision Instrument», импорт из DESIGN.json/DESIGN.md).
- MCP-сервер (Streamable HTTP, `/mcp`) с поддержкой ~18 типов диаграмм Mermaid.

## Запуск
```bash
dotnet run --project src/MermaidMcpServer
```
Веб-UI: http://localhost:5000

## MCP-сервер (Streamable HTTP)
Эндпоинт MCP: `http://localhost:5000/mcp`. Транспорт — Streamable HTTP без сессий
(ревизия протокола `2026-07-28`, SDK `ModelContextProtocol.AspNetCore` 2.x); клиенты
старых ревизий подключаются через `initialize` к тому же адресу. Legacy-SSE (`/sse`,
`/message`) отключён. `GET /` отдаёт веб-UI.

### Подключение клиентов

**Claude Code (CLI):**
```bash
claude mcp add --transport http mermaid http://localhost:5000/mcp
```
Эквивалент в JSON-конфиге Claude Code: `{ "type": "http", "url": "http://localhost:5000/mcp" }`.

**Claude Desktop** — `claude_desktop_config.json` поддерживает только stdio-серверы, поэтому
подключение через мост `mcp-remote` (нужен Node.js / `npx`):
```json
{
  "mcpServers": {
    "mermaid": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "http://localhost:5000/mcp", "--transport", "http-only"]
    }
  }
}
```
После правки конфига полностью перезапустите Claude Desktop (выход из трея, не просто
закрытие окна). Сервер (`dotnet run`) должен быть запущен до старта клиента.

### Инструменты
- `render_mermaid(code, format=code|svg|png)` — предпросмотр/рендер. Поддержка ~18 типов диаграмм.
- `generate_mermaid_file(code, format=png|svg)` — рендерит файл на сервере и возвращает
  постоянную ссылку `{PublicBaseUrl}/files/{GUID}.png|svg` для вставки в документы.
- `validate_mermaid(code)` — проверка синтаксиса (требует серверного рендера).
- `create_flowchart(...)` и другие билдеры (добавляются по фазам).

### Файлы генерации (`GET /files/{name}`)
`generate_mermaid_file` пишет файл в `Rendering:OutputDirectory` (по умолчанию `.`) и кладёт его
в память на 1 час; после часа (или перезапуска) endpoint читает файл с диска. Файлы не
удаляются. Ответ кэшируется браузером (`Cache-Control: immutable`); SVG отдаётся с CSP,
запрещающим скрипты. Автодеплой хранит файлы в `/var/lib/mermaid-mcp` — вне каталога
приложения, который подменяется при каждом обновлении. Реверс-прокси должен пропускать
`/mcp` и `/files/`.

Поддерживаемые типы (~18): flowchart, sequence, class, state, ER, user journey, gantt,
git graph, mind map, kanban, packet, pie, quadrant, requirement, timeline, sankey, xy, block.

## Серверный рендер (SVG/PNG)
Выключен по умолчанию. Включить: в `appsettings.json` → `"Rendering": { "EnableServerRender": true }`.
Рендер чисто управляемый (`Mermaider` → SVG, `Svg.Skia`/SkiaSharp → PNG): ни браузера, ни
загрузки Chromium, ни дочернего процесса. Веб-preview по-прежнему рисует `mermaid.js` в браузере
(все ~18 типов); серверный рендер покрывает поднабор типов Mermaider (flowchart, sequence,
class, state, ER, pie, quadrant, timeline, git graph, mind map и др.).

## Офлайн Tailwind (опционально)
По умолчанию используется Tailwind Play-CDN. Для офлайна соберите статический `app.css`
standalone-бинарём Tailwind и подключите вместо CDN-скрипта в `wwwroot/index.html`.


## Автодеплой на Linux-сервер
Один запуск от root ставит сервис и автообновление (таймер раз в 5 минут подтягивает
`main`, пересобирает и перезапускает сервис; при неудаче откатывается на прежнюю версию).
Нужны `git`, `curl`, systemd и .NET SDK 10.
```bash
curl -fsSL https://raw.githubusercontent.com/tdav/Mermaid-Mcp-Server/main/deploy/deploy.sh \
  | sudo PUBLIC_BASE_URL=https://mermaid.example.com bash
```
Сервис слушает `127.0.0.1:40100` — наружу его отдаёт реверс-прокси. Окружение — в
`/etc/mermaid-mcp/mermaid.env`. Ручные команды: `deploy.sh --force | --status | --no-timer`
(см. шапку `deploy/deploy.sh`).

Реверс-прокси — готовый конфиг nginx в `deploy/nginx.conf` (открывает `/mcp`, `/files/` и
веб-UI; инструкция по установке — в шапке файла).

### Переход с SSE на `/mcp`
Legacy-эндпоинт `/sse` удалён. После обновления сервера:
1. Поставить/обновить `deploy/nginx.conf`: пути `/mcp` и `/files/` должны проходить через прокси.
2. Переподключить MCP-клиентов на новый адрес:
   ```bash
   claude mcp remove mermaid
   claude mcp add --transport http mermaid https://mermaid.example.com/mcp
   ```
   В Claude Desktop — заменить в `mcp-remote` адрес на `.../mcp` и флаг на `--transport http-only`
   (см. «Подключение клиентов»), затем полностью перезапустить приложение.

## Deploy SystemD (вручную)
```bash
[Unit]
Description=Mermaid Server

[Service]
WorkingDirectory=/var/www/{folder}
ExecStart=/var/www/{folder}/MermaidMcpServer --urls=http://localhost:40100/
Restart=always

RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=MermaidMcpServer

User=root
Environment=ASPNETCORE_ENVIRONMENT=Development
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```


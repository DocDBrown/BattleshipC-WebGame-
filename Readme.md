# Battleship — C# Blazor WebAssembly Web Game

A complete browser Battleship game written in C# 14 on .NET 10. The UI is a Blazor
WebAssembly application hosted as static files by an ASP.NET Core 10 server. The same
server creates private two-player rooms and relays versioned JSON messages over
WebSockets. A single-player mode runs entirely in the browser against a local AI.

![Battleship](battleship1.webp)

## Features

- **Single player** against a browser-local AI at easy, medium, or hard difficulty.
- **Two-player multiplayer** with a six-character room code and a shareable join link.
- **Manual or automatic fleet placement** for the human player.
- **Mouse, touch, drag, and keyboard** input for placement and targeting.
- **Private fleets**: unhit ship coordinates never leave the browser. Only shots and
  shot results are sent over the network.
- **Versioned JSON protocol** (`v1`) with one message per WebSocket text frame.
- **Server-side validation and rate limiting** for room creation, connections, and messages.
- **Room lifecycle management**, including idle and maximum-lifetime expiry.
- **No persistent storage**: game state lives in browser memory and room state in server memory.

## Project Layout

```text
.
├── Battleship.sln
├── Dockerfile
├── src
│   ├── Battleship.Shared          # Protocol records, validation helpers, board coordinates
│   ├── Battleship.Server          # ASP.NET Core host, room API, WebSocket relay, reaper
│   └── Battleship.Web             # Blazor WebAssembly UI, game engine, AI, WebSocket client
└── Readme.md
```

The server project references the web project so that a local build can serve the
Blazor framework files directly. The Docker build publishes the web project separately
and copies its generated `wwwroot` into the server image.

## Quick Start (Docker)

```bash
docker build -t battleship .
docker run --rm -p 8080:8080 battleship
```

Open <http://localhost:8080>.

The container listens on port `8080` (HTTP). To use HTTPS or a reverse proxy, terminate
TLS in front of the container and forward both HTTP and WebSocket upgrade requests.

## Quick Start (Local .NET 10 SDK)

```bash
dotnet run --project src/Battleship.Server
```

Open <http://localhost:8080>. The default launch profile runs the server in
Development mode so it can serve the referenced Blazor project's static web assets.
The server hosts both the API and the Blazor application. The application is
browser-only after the initial load; single-player games do not make network requests.

## How to Play

1. Open **Single player**, choose a difficulty on `/single`, and place your fleet.
2. Click or tap a cell to place the first unplaced ship, drag a ship card onto the
   board, or use automatic placement.
3. Select **Start battle** and click or tap enemy cells to fire.
4. The AI fires only at legal, untried cells and only uses its own shot history and
   reported outcomes.
5. Multiplayer: open **Host**, share the room code or join link, and have your opponent
   open the link or enter the code on the menu.

### Controls

| Input | Action |
| --- | --- |
| Mouse / touch | Activate a cell or ship card |
| Drag | Drag a ship card onto the placement board |
| Arrow keys | Move the board cursor |
| `R` | Rotate the ship being placed |
| `Enter` / `Space` | Activate the focused cell |

## AI Difficulties

- **Easy** chooses uniformly at random among untried cells.
- **Medium** hunts at random, then targets untried cells orthogonally adjacent to
  unresolved hits, extending along a line when two or more hits align. Hits are cleared
  when the ship is reported sunk.
- **Hard** counts weighted legal placements for every remaining ship, avoiding known
  misses and sunk cells, gives extra weight to placements covering unresolved hits,
  prioritises cells adjacent to unresolved hits, and breaks ties randomly.

The AI fleet is placed automatically at every difficulty. The AI never receives a
reference to the player's board or unresolved ship positions.

## Server Configuration

Server settings are read from the `Battleship` configuration section
(`appsettings.json`) and can be overridden with environment variables using `__`
separators, for example `Battleship__MaxRooms=500`.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Battleship__MaxRooms` | `500` | Maximum concurrent live rooms |
| `Battleship__RoomCodeAttempts` | `64` | Attempts to find a unique room code |
| `Battleship__CreateRoomPermitsPerMinute` | `30` | Per-IP room creation limit |
| `Battleship__ConnectPermitsPerMinute` | `120` | Per-IP connection limit |
| `Battleship__KeepAliveSeconds` | `30` | WebSocket keep-alive interval |
| `Battleship__SendTimeoutSeconds` | `5` | Per-send timeout |
| `Battleship__MessagesPerSecond` | `10` | Per-participant message rate |
| `Battleship__MessageBurst` | `20` | Per-participant message burst |
| `Battleship__ReaperIntervalSeconds` | `30` | Room reaper tick interval |
| `Battleship__RoomIdleTimeoutSeconds` | `120` | Idle lifetime for an empty room |
| `Battleship__RoomMaxLifetimeMinutes` | `60` | Maximum room lifetime |

## Client Configuration

Client settings are served from `src/Battleship.Web/wwwroot/appsettings.json`.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Battleship:AiSeed` | unset | Optional seed for deterministic AI decisions |
| `Battleship:AiMoveDelayMs` | `600` | Delay before the AI takes its turn |
| `Battleship:AiDecisionBudgetMs` | `250` | Soft budget for one AI decision |
| `Battleship:HttpTimeoutSeconds` | `10` | Room API and socket connect timeout |
| `Battleship:JoinTimeoutSeconds` | `600` | Host wait for an opponent |
| `Battleship:HandshakeTimeoutSeconds` | `15` | HELLO/READY handshake and final QUIT wait |
| `Battleship:ReadTimeoutSeconds` | `60` | Timeout waiting for a pending result or the opponent's next shot |
| `Battleship:JoinRetryCount` | `20` | Room lookup retries while the host connects |
| `Battleship:JoinRetryDelayMs` | `500` | Delay between room lookup retries |

## HTTP API

| Method | Path | Response |
| --- | --- | --- |
| `POST` | `/api/rooms` | `201` with `{ "roomCode": "ABC234" }` |
| `GET` | `/api/rooms/{roomCode}` | `200` with `{ "participants": 1 }` |
| `GET` (upgrade) | `/ws/rooms/{roomCode}?role=host\|client` | WebSocket |

Room codes are six characters from the alphabet `A-HJ-NP-Z2-9`.

## Protocol (v1)

One UTF-8 JSON object per text frame. Property names are camelCase, unknown properties
are rejected, and every message carries `v` (`1`) and `type`.

| Type | Direction | Fields |
| --- | --- | --- |
| `HELLO` | both | `v`, `type` |
| `READY` | both | `v`, `type` |
| `SHOT` | active player | `v`, `type`, `seq`, `coord` (`A1`..`J10`) |
| `RESULT` | receiving player | `v`, `type`, `seq`, `outcome` (`HIT`, `MISS`, `SUNK`, `GAME_OVER`), `ship` for `SUNK`/`GAME_OVER` |
| `CHAT` | both | `v`, `type`, `text` (1..200 characters, no control/bidi characters) |
| `QUIT` | either | `v`, `type` |
| `ERROR` | either/server | `v`, `type`, `code`, optional `detail` |

`SHOT.seq` is strictly increasing per sender and every `RESULT` must echo the pending
`seq`. The receiving browser resolves the shot locally and sends only the outcome and
sunk ship type. Chat never changes the turn.

### Close Codes

| Code | Meaning |
| --- | --- |
| `1000` | Normal closure after `QUIT` or game over |
| `1001` | Server shutdown or room expired |
| `1002` | Protocol error or version mismatch |
| `1003` | Binary frame received |
| `1009` | Frame exceeded 4096 bytes |
| `1011` | Internal error |
| `4001` | Peer disconnected |
| `4004` | Room not found, or client connected before host |
| `4009` | Room full, or role slot already taken |
| `4029` | Connection rate limit exceeded |
| `4400` | Invalid room code or role |

## Privacy and Security

- Unhit ship coordinates are never serialized into protocol messages; `RESULT`
  contains only the sequence, outcome, and sunk ship type.
- The server validates frame type, size, rate, JSON schema, and protocol version, then
  forwards only the re-serialized validated message.
- Chat is rendered as an HTML-encoded Razor text node. Control and bidirectional
  override characters are removed.
- Responses include a Content Security Policy allowing only same-origin scripts and
  `wasm-unsafe-eval`, plus `nosniff`, `DENY` framing, and a no-referrer policy.
- Rooms and games are in-memory only; restarting the server clears all rooms.

## Development Notes

- .NET 10 SDK is required for local builds.
- Tests are intentionally not included in this project.
- The solution can be opened in Visual Studio, Rider, or VS Code with the C# Dev Kit.
- `docker compose down` stops the container; no volumes or databases are used.

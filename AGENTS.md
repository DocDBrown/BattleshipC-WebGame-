<!-- AGENTS.md -->
# AGENTS.md

## Project

Battleship is a browser Battleship game. It has a single-player mode against an AI (easy, medium, hard) and two-player online games relayed over WebSockets.

- `src/Battleship.Shared`: game rules, board, fleet, shot resolution, AI strategies, protocol types.
- `src/Battleship.Web`: Blazor WebAssembly client (pages, components, `wwwroot` static files including `robots.txt`, `sitemap.xml`, `llms.txt`).
- `src/Battleship.Server`: ASP.NET Core host that serves the published client from `wwwroot` and runs the room API and WebSocket relay.
- `Directory.Build.props`: shared build settings for all projects.

## Commands

- Restore: `dotnet restore Battleship.sln`
- Format check: `dotnet format Battleship.sln --verify-no-changes`
- Build: `dotnet build Battleship.sln -c Release -warnaserror`
- Test: `dotnet test Battleship.sln -c Release`
- Run in a container: `docker compose up -d --build`, then open http://localhost:8080

All of the above must pass before a change is complete; CI runs the same steps.

## Code rules

- No code comments, except a single filename comment on the first line of each file.
- Never remove or reduce existing `AppLogger.Log` calls; add logging for new events.
- Builds treat warnings as errors; fix warnings rather than suppressing them.
- When changing a file, return the complete file, not a fragment.

## Game invariants

- Board is 10×10; fleet is Carrier 5, Battleship 4, Cruiser 3, Submarine 3, Patrol Boat 2.
- Turns always alternate; repeated or invalid shots are rejected without using a turn.
- In multiplayer the host fires first; chat never changes the turn.
- Unhit ship positions must never leave the player's browser. Only shot results are exchanged.
- The AI must only use `AiView` knowledge, never the human `Board`.
- Room codes are six characters from `ProtocolInfo.RoomCodeAlphabet` (A–H, J–N, P–Z, 2–9).

## SEO and AEO

- Every routable page renders `<SeoMeta>` with a unique title, description and canonical path.
- Pages that create state on visit (`/host`, `/join/{code}`) set `NoIndex="true"` and are disallowed in `robots.txt`.
- New public pages must be added to `wwwroot/sitemap.xml` and `wwwroot/llms.txt`.
- FAQ structured data on `/help` is generated from the same list as the visible FAQ; keep them as one source.
- The footer (`SiteFooter.razor`) links to https://github.com/DocDBrown/BattleshipC-WebGame-.

## Deployment

Pushing to `main` runs `.github/workflows/ci.yml`: gitleaks, format/build/test, then the image is built from `Dockerfile`, pushed to Artifact Registry repo `battleship` in `australia-southeast1`, and deployed to Cloud Run service `battleship`. Never commit secrets; GCP settings live in GitHub Actions secrets.
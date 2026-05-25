# Gold Saucer Casino

Prototype Dalamud plugin for private opt-in casino tables in FFXIV. The first game is Texas Hold'em poker.

## Current Shape

- `GoldSaucerCasino.Plugin` is the Dalamud plugin shell. Use `/casino` to open the popup.
- `GoldSaucerCasino.Core` contains game rules that can be tested outside FFXIV.
- `GoldSaucerCasino.Core.Tests` is a small dependency-free test runner for the poker hand evaluator.
- Poker hands now advance through phases: pre-flop, flop, turn, river, and showdown.
- The plugin renders a simulated table with opponent seats, your hand, and the shared board.
- The Open tab starts in a not-joined lobby with Host Table and Join Table actions.
- Settings persist a last-known gil snapshot, last room code, and default buy-in.
- Blackjack is now the primary table prototype, with host-entered bets, ready checks, visible player cards, a hidden dealer hole card, split/double/insurance eligibility, automatic busts, natural blackjack payout, and five-hit wins.
- In hosted blackjack, the host is the dealer/banker. Players trade bets to the host; the host keeps losing bets and manually pays winning returns.
- Blackjack supports up to five player seats plus the dealer, shows the local character name with `(you)`, and includes a solo Bot Game mode for no-gil testing.
- Bot Game starts with no bet, does not affect profile stats, and uses the same dealer rule as real blackjack: hit 16 or lower, stand on 17 or higher.
- The Dalamud Open button opens a table-only game window; the Settings button opens a separate settings/profile window.
- Host, join, and bot game start from Settings, while ready checks and hand actions appear in the live table window.
- Player Profile tracks total blackjack earnings, losses, and net result for host-confirmed real rounds.
- Settings includes a Rules tab with Blackjack rules, betting notes, action requirements, No Push 22, and dealer behavior.

## Multiplayer Plan

Dalamud plugins do not get a magic peer-to-peer channel through FFXIV. Multiplayer should be built as an external opt-in relay:

1. A small server hosts private table rooms. `GoldSaucerCasino.Relay` is the first local relay prototype.
2. The server reserves room codes, so duplicates cannot happen globally.
3. Each plugin connects to that server over HTTPS/WebSocket.
4. The server owns the deck seed, turn order, betting state, and showdown resolution.
5. The plugin renders the UI and sends player actions.

Room codes in the current plugin are generated locally and are only placeholders until the relay exists.

Run the local relay:

```powershell
dotnet run --project src/GoldSaucerCasino.Relay/GoldSaucerCasino.Relay.csproj --urls http://127.0.0.1:5217
```

Smoke test it with two fake clients:

```powershell
dotnet run --project tests/GoldSaucerCasino.Relay.SmokeTests/GoldSaucerCasino.Relay.SmokeTests.csproj -- http://127.0.0.1:5217
```

The plugin Table Setup screen now has a Relay URL field. With the relay running, Host Game calls `POST /rooms`, Join Game connects to that room over WebSocket, and connected player names are synced into the blackjack seats before a round starts.

## Custom Plugin Repository

This GitHub repo can serve both the plugin source and the Dalamud custom repository files.

Build/update the custom Dalamud repository files:

```powershell
.\tools\BuildDalamudRepo.ps1 -GitHubOwner YOUR_GITHUB_USERNAME -GitHubRepo YOUR_REPO_NAME -Branch main
```

Copy the generated files to the repo root before pushing:

```powershell
Copy-Item dist\dalamud-repo\pluginmaster.json pluginmaster.json -Force
New-Item -ItemType Directory -Path plugins\GoldSaucerCasino -Force
Copy-Item dist\dalamud-repo\plugins\GoldSaucerCasino\latest.zip plugins\GoldSaucerCasino\latest.zip -Force
```

The URL to paste into Dalamud's Experimental custom repositories box is:

```text
https://raw.githubusercontent.com/YOUR_GITHUB_USERNAME/YOUR_REPO_NAME/main/pluginmaster.json
```

For this project, the repository URL is:

```text
https://raw.githubusercontent.com/TentacleBukakke/GoldSaucerCasinoDalamud/main/pluginmaster.json
```

The generated repository files live in:

```text
dist\dalamud-repo
```

## Render Relay Deployment

The relay can be deployed to Render as a Docker web service. This repo includes:

- `Dockerfile.relay`
- `render.yaml`

Render setup:

1. Push this source repo to GitHub.
2. In Render, choose **New** -> **Blueprint**.
3. Connect the GitHub repo containing this `render.yaml`.
4. Create the `gold-saucer-casino-relay` service.
5. After deploy, copy the public Render URL, such as:

```text
https://gold-saucer-casino-relay.onrender.com
```

Use that URL in the plugin's **Settings** -> **Table Setup** -> **Relay URL** field.

Current hosted relay:

```text
https://gold-saucer-casino-relay.onrender.com
```

## Gil Settlement

This prototype intentionally does not automate gil transfers. In hosted blackjack, the host acts as dealer/banker: players trade bets to the host before the round, and the host manually pays calculated winnings after settlement. Automating trades or gil movement is likely to create Terms of Service and plugin-review problems.

For blackjack, the host should enter the amount each player traded during the ready check. Split, double-down, and insurance actions also require host-confirmed trade amounts before the plugin applies the action.

## Build Notes

Install XIVLauncher/Dalamud dev files first so these references exist:

- `%AppData%\XIVLauncher\addon\Hooks\dev\Dalamud.dll`
- `%AppData%\XIVLauncher\addon\Hooks\dev\Dalamud.Bindings.ImGui.dll`

Dalamud API 15 targets .NET 10, so this repo pins the .NET 10 SDK.

Then run:

```powershell
dotnet run --project tests/GoldSaucerCasino.Core.Tests/GoldSaucerCasino.Core.Tests.csproj
dotnet build src/GoldSaucerCasino.Plugin/GoldSaucerCasino.Plugin.csproj
```

For custom repository distribution, Dalamud expects a raw JSON repository URL in `/xlsettings` -> Experimental -> Custom Plugin Repositories.

# Gold Saucer Casino

Dalamud plugin for private blackjack tables with friends in FFXIV.

## Install

Add this custom repository URL in Dalamud:

```text
https://raw.githubusercontent.com/TentacleBukakke/GoldSaucerCasinoDalamud/main/pluginmaster.json
```

In game:

1. Open `/xlsettings`.
2. Go to **Experimental**.
3. Add the custom repository URL above.
4. Save.
5. Open `/xlplugins`.
6. Install **Gold Saucer Casino**.

## Playing

- Use `/casino` or the plugin installer **Open** button for the table window.
- Use the plugin installer **Settings** button for setup, player profile, ledger, and rules.
- Click **Host Game** to create a room code.
- Friends click **Join Game** and enter that room code.
- The hosted relay is already configured:

```text
https://gold-saucer-casino-relay.onrender.com
```

No one needs to run a local relay server for normal play.

## Blackjack

- Hosted blackjack supports up to five player seats plus the dealer.
- The host is the dealer/banker. On the table, the host is shown as `Name (Dealer)`.
- Players trade bets to the host before readying.
- The host enters the amount actually received for each player.
- If players lose, the host keeps the bet. If players win, the host manually pays the calculated return.
- Bot Game is a no-bet practice round against an AI dealer and does not affect player stats.

## Gil Settlement

The plugin does not automate gil transfer. It tracks blackjack state, bets, and payout math, but trades and payouts are done manually between players.

## Development

This repo contains:

- `GoldSaucerCasino.Plugin`: Dalamud plugin.
- `GoldSaucerCasino.Core`: blackjack and poker rules.
- `GoldSaucerCasino.Relay`: hosted WebSocket relay.
- `tests`: core tests and relay smoke tests.

Build and test:

```powershell
dotnet build FF14Casino.sln
dotnet run --project tests/GoldSaucerCasino.Core.Tests/GoldSaucerCasino.Core.Tests.csproj
dotnet run --project tests/GoldSaucerCasino.Relay.SmokeTests/GoldSaucerCasino.Relay.SmokeTests.csproj -- https://gold-saucer-casino-relay.onrender.com
```

Rebuild the Dalamud custom repo package:

```powershell
.\tools\BuildDalamudRepo.ps1 -GitHubOwner TentacleBukakke -GitHubRepo GoldSaucerCasinoDalamud -Branch main
Copy-Item dist\dalamud-repo\pluginmaster.json pluginmaster.json -Force
New-Item -ItemType Directory -Path plugins\GoldSaucerCasino -Force
Copy-Item dist\dalamud-repo\plugins\GoldSaucerCasino\latest.zip plugins\GoldSaucerCasino\latest.zip -Force
```

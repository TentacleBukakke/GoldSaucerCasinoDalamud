using GoldSaucerCasino.Core.Blackjack;
using GoldSaucerCasino.Core.Poker;
using Dalamud.Bindings.ImGui;
using System.Numerics;
using GoldSaucerCasino.Plugin;
using GoldSaucerCasino.Plugin.Relay;

namespace GoldSaucerCasino.Plugin.Windows;

public sealed class MainWindow
{
    private readonly Configuration configuration;
    private readonly Action saveConfiguration;
    private readonly Func<long> getCurrentGil;
    private readonly Func<string> getLocalPlayerName;
    private readonly RelayClient relayClient = new();
    private readonly PokerTable demoTable = new();
    private readonly BlackjackTable blackjackTable = new();
    private readonly Dictionary<string, int> blackjackBetInputs = [];
    private readonly Dictionary<string, int> blackjackInsuranceInputs = [];
    private IReadOnlyList<BlackjackPayout> blackjackPayouts = [];
    private string playerName = "Player";
    private int buyIn = 10000;
    private int defaultBuyIn = 10000;
    private int splitMatchedBet = 10000;
    private int doubleDownBet = 10000;
    private string settlementNote = "Manual settlement only. Confirm gil trades in-game yourself.";
    private string tableMessage = "Start a hand, then advance through each poker phase.";
    private string blackjackMessage = "Host or join a blackjack table.";
    private string relayStatus = "Relay disconnected.";
    private string relayUrl = "http://127.0.0.1:5217";
    private string joinRoomCode = string.Empty;
    private string activeRoomCode = string.Empty;
    private string hostPlayerName = string.Empty;
    private TableConnectionState connectionState = TableConnectionState.NotJoined;
    private bool isBotGame;
    private bool profileRecordedForRound;

    public bool IsOpen { get; set; }

    public bool IsSettingsOpen { get; set; }

    public MainWindow(Configuration configuration, Action saveConfiguration, Func<long> getCurrentGil, Func<string> getLocalPlayerName)
    {
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        this.getCurrentGil = getCurrentGil;
        this.getLocalPlayerName = getLocalPlayerName;
        this.buyIn = Math.Max(1, configuration.DefaultBuyIn);
        this.defaultBuyIn = this.buyIn;
        if (string.IsNullOrWhiteSpace(configuration.RelayUrl) || configuration.RelayUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) || configuration.RelayUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase))
        {
            configuration.RelayUrl = Configuration.DefaultRelayUrl;
            this.saveConfiguration();
        }

        this.activeRoomCode = configuration.LastRoomCode;
        this.relayUrl = configuration.RelayUrl;
        this.demoTable.SeatPlayer("You", 10000);
        this.demoTable.SeatPlayer("Lyse", 10000);
        this.demoTable.SeatPlayer("Thancred", 10000);
        this.demoTable.SeatPlayer("Y'shtola", 10000);

        this.ResetBlackjackSeats(includeLocalPlayer: true);
        this.relayClient.PlayersChanged += this.ApplyRelayPlayers;
        this.relayClient.StatusChanged += status => this.relayStatus = status;
    }

    private string LocalPlayerName => string.IsNullOrWhiteSpace(this.getLocalPlayerName()) ? "You" : this.getLocalPlayerName();

    private string DealerDisplayName => this.connectionState switch
    {
        TableConnectionState.Hosting => $"{this.LocalPlayerName} (Dealer)",
        TableConnectionState.Bot => "AI Dealer",
        _ => "Dealer",
    };

    public void Dispose() => this.relayClient.Dispose();

    public void Draw()
    {
        this.DrawGameWindow();
        this.DrawSettingsWindow();
    }

    private void DrawGameWindow()
    {
        if (!this.IsOpen)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(820, 680), ImGuiCond.FirstUseEver);
        var isOpen = this.IsOpen;
        if (!ImGui.Begin("Gold Saucer Casino", ref isOpen))
        {
            this.IsOpen = isOpen;
            ImGui.End();
            return;
        }

        this.IsOpen = isOpen;
        this.DrawOpenTab();

        ImGui.End();
    }

    private void DrawSettingsWindow()
    {
        if (!this.IsSettingsOpen)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(620, 520), ImGuiCond.FirstUseEver);
        var isOpen = this.IsSettingsOpen;
        if (!ImGui.Begin("Gold Saucer Casino Settings", ref isOpen))
        {
            this.IsSettingsOpen = isOpen;
            ImGui.End();
            return;
        }

        this.IsSettingsOpen = isOpen;
        this.DrawSettingsTab();

        ImGui.End();
    }

    private void DrawOpenTab()
    {
        this.RefreshGilSnapshot();
        this.DrawLobbyBar();

        if (this.connectionState == TableConnectionState.NotJoined)
        {
            ImGui.Dummy(new Vector2(0, 120));
            this.CenterText("Havent joined a game");
            this.CenterText("Open Settings to host, join, or start a bot game.");
            return;
        }

        ImGui.TextWrapped(this.blackjackMessage);
        this.DrawBlackjackTable();
        ImGui.Spacing();
        this.DrawBlackjackControls();
    }

    private void DrawBlackjackTab()
    {
        this.RefreshGilSnapshot();
        this.DrawLobbyBar();

        if (this.connectionState == TableConnectionState.NotJoined)
        {
            this.DrawBlackjackNotJoinedPanel();
            return;
        }

        ImGui.TextUnformatted($"Blackjack - {this.blackjackTable.Phase}");
        ImGui.SameLine();
        ImGui.TextUnformatted($"Room {this.activeRoomCode}");
        ImGui.SameLine();
        if (ImGui.Button("Leave Blackjack"))
        {
            this.connectionState = TableConnectionState.NotJoined;
            this.blackjackMessage = "Havent joined a game";
            return;
        }

        ImGui.Spacing();
        ImGui.TextWrapped(this.blackjackMessage);
        this.DrawBlackjackControls();
        this.DrawBlackjackTable();
    }

    private void DrawBlackjackNotJoinedPanel()
    {
        ImGui.Dummy(new Vector2(0, 80));
        this.CenterText("Havent joined a game");
        this.CenterText("Host a blackjack table or join with an invite room number.");
        ImGui.Dummy(new Vector2(0, 24));

        var buttonWidth = 170f;
        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetContentRegionAvail().X - ((buttonWidth * 2) + 12)) / 2));
        if (ImGui.Button("Host Table", new Vector2(buttonWidth, 36)))
        {
            _ = this.HostBlackjackTableAsync();
        }

        ImGui.SameLine();
        if (ImGui.Button("Join Table", new Vector2(buttonWidth, 36)))
        {
            _ = this.JoinBlackjackTableAsync();
        }

        ImGui.SetNextItemWidth(180);
        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetContentRegionAvail().X - 180) / 2));
        ImGui.InputText("##blackjack-room-code", ref this.joinRoomCode, 16);
        this.CenterText("Trades are verified by the host entering the amount received.");
    }

    private void DrawBlackjackControls()
    {
        if (this.blackjackTable.Phase == BlackjackPhase.Lobby)
        {
            if (ImGui.Button("Open Ready Check"))
            {
                this.OpenBlackjackReadyCheck();
            }

            ImGui.TextWrapped("Seats update automatically as players join the room.");

            return;
        }

        if (this.blackjackTable.Phase == BlackjackPhase.ReadyCheck)
        {
            this.DrawBlackjackReadyCheck();
            return;
        }

        if (this.blackjackTable.Phase == BlackjackPhase.PlayerTurns)
        {
            this.DrawBlackjackPlayerActions();
            return;
        }

        if (this.blackjackTable.Phase == BlackjackPhase.DealerTurn)
        {
            if (ImGui.Button("Dealer Play + Settle"))
            {
                this.blackjackPayouts = this.blackjackTable.FinishDealerAndSettle();
                this.RecordLocalProfileResult();
                this.blackjackMessage = "Dealer finished. Payouts are calculated below.";
            }

            return;
        }

        if (this.blackjackTable.Phase == BlackjackPhase.Settled)
        {
            if (ImGui.Button("Next Ready Check"))
            {
                this.OpenBlackjackReadyCheck();
            }

            this.DrawPayouts();
        }
    }

    private void DrawBlackjackReadyCheck()
    {
        if (this.blackjackTable.Seats.Count == 0)
        {
            ImGui.TextWrapped("Waiting for players to join. Share the room code, then enter bets after players appear.");
            return;
        }

        ImGui.TextUnformatted("Host enters the amount traded by each player, then marks them ready.");
        ImGui.Columns(4, "blackjack-ready", true);
        ImGui.TextUnformatted("Player");
        ImGui.NextColumn();
        ImGui.TextUnformatted("Bet");
        ImGui.NextColumn();
        ImGui.TextUnformatted("Ready");
        ImGui.NextColumn();
        ImGui.TextUnformatted("State");
        ImGui.NextColumn();
        ImGui.Separator();

        foreach (var seat in this.blackjackTable.Seats)
        {
            ImGui.TextUnformatted(this.DisplaySeatName(seat.Name));
            ImGui.NextColumn();

            var bet = this.GetBlackjackBetInput(seat);
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt($"##bet-{seat.Name}", ref bet, 1000, 10000))
            {
                bet = Math.Max(0, bet);
                this.blackjackBetInputs[seat.Name] = bet;
                try
                {
                    this.blackjackTable.SetBet(seat.Name, bet);
                }
                catch (Exception ex)
                {
                    this.blackjackMessage = ex.Message;
                }
            }

            ImGui.NextColumn();

            var ready = seat.IsReady;
            if (ImGui.Checkbox($"##ready-{seat.Name}", ref ready))
            {
                try
                {
                    this.blackjackTable.SetReady(seat.Name, ready);
                }
                catch (Exception ex)
                {
                    this.blackjackMessage = ex.Message;
                }
            }

            ImGui.NextColumn();
            ImGui.TextUnformatted(seat.IsReady ? "Ready" : "Waiting");
            ImGui.NextColumn();
        }

        ImGui.Columns(1);

        var canStart = this.blackjackTable.CanStartRound;
        if (this.ActionButton("Start Game", canStart))
        {
            try
            {
                this.blackjackTable.StartRound();
                this.blackjackPayouts = [];
                this.profileRecordedForRound = this.isBotGame;
                this.blackjackMessage = this.blackjackTable.CanOfferInsurance
                    ? "Dealer shows an Ace. Insurance is available before player actions."
                    : "Cards dealt. Player turns go right to left by seat order.";
            }
            catch (Exception ex)
            {
                this.blackjackMessage = ex.Message;
            }
        }
    }

    private void DrawBlackjackPlayerActions()
    {
        var seat = this.blackjackTable.ActiveSeat;
        var hand = seat?.ActiveHand;
        if (seat is null || hand is null)
        {
            this.blackjackMessage = "No active player. Dealer can play.";
            return;
        }

        ImGui.TextUnformatted($"Turn: {seat.Name} - Hand {seat.ActiveHandIndex + 1}");
        ImGui.SameLine();
        ImGui.TextUnformatted($"Total {hand.Total}");

        if (this.blackjackTable.CanOfferInsurance)
        {
            var insurance = this.GetInsuranceInput(seat);
            ImGui.SetNextItemWidth(110);
            ImGui.InputInt("Insurance", ref insurance, 1000, 10000);
            insurance = Math.Max(0, insurance);
            this.blackjackInsuranceInputs[seat.Name] = insurance;
            if (this.ActionButton("Confirm Insurance Trade", insurance <= seat.InitialBet / 2))
            {
                try
                {
                    this.blackjackTable.SetInsuranceBet(seat.Name, insurance);
                    this.blackjackMessage = $"{seat.Name} insurance recorded.";
                }
                catch (Exception ex)
                {
                    this.blackjackMessage = ex.Message;
                }
            }
        }

        if (this.ActionButton("Hit Me", hand.CanHit))
        {
            this.blackjackTable.Hit();
            this.blackjackMessage = hand.IsBust ? $"{seat.Name} bust." : $"{seat.Name} hit.";
        }

        ImGui.SameLine();
        if (this.ActionButton("Stand", !hand.IsStanding))
        {
            this.blackjackTable.Stand();
            this.blackjackMessage = $"{seat.Name} stands.";
        }

        ImGui.SameLine();
        if (this.ActionButton("Split Pair", hand.CanSplit))
        {
            this.splitMatchedBet = (int)hand.Bet;
            this.blackjackMessage = this.isBotGame
                ? "Free split available in bot mode. Confirm below."
                : "Host must receive a matching split bet, then confirm below.";
        }

        ImGui.SameLine();
        if (this.ActionButton("Double Down", hand.CanDoubleDown))
        {
            this.doubleDownBet = this.isBotGame ? 0 : (int)Math.Min(hand.Bet, Math.Max(1, this.doubleDownBet));
            this.blackjackMessage = this.isBotGame
                ? "Free double down available in bot mode. Confirm below."
                : "Host must receive the double-down trade, then confirm below.";
        }

        if (hand.CanSplit)
        {
            if (!this.isBotGame)
            {
                ImGui.SetNextItemWidth(120);
                ImGui.InputInt("Split matched trade", ref this.splitMatchedBet, 1000, 10000);
            }

            if (this.ActionButton("Confirm Split", this.splitMatchedBet == hand.Bet))
            {
                try
                {
                    this.blackjackTable.Split(this.splitMatchedBet);
                    this.blackjackMessage = hand.IsSplitAces
                        ? "Split aces: one card dealt to each hand and turn advanced."
                        : "Split complete. Play each hand in order.";
                }
                catch (Exception ex)
                {
                    this.blackjackMessage = ex.Message;
                }
            }
        }

        if (hand.CanDoubleDown)
        {
            if (!this.isBotGame)
            {
                ImGui.SetNextItemWidth(120);
                ImGui.InputInt("Double trade", ref this.doubleDownBet, 1000, 10000);
            }

            var canDouble = this.isBotGame ? this.doubleDownBet == 0 : this.doubleDownBet > 0 && this.doubleDownBet <= hand.Bet;
            if (this.ActionButton("Confirm Double Down", canDouble))
            {
                try
                {
                    this.blackjackTable.DoubleDown(this.doubleDownBet);
                    this.blackjackMessage = "Double down complete: one card dealt and turn advanced.";
                }
                catch (Exception ex)
                {
                    this.blackjackMessage = ex.Message;
                }
            }
        }
    }

    private void DrawPayouts()
    {
        if (this.blackjackPayouts.Count == 0)
        {
            return;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Payouts due from host");
        foreach (var payout in this.blackjackPayouts)
        {
            var total = payout.ReturnAmount + payout.InsuranceReturn;
            var betText = this.isBotGame ? "no bet" : $"bet {payout.Bet:N0}, return {total:N0}";
            ImGui.TextUnformatted($"{payout.PlayerName} hand {payout.HandNumber}: {payout.Outcome}, {betText}");
        }
    }

    private void RecordLocalProfileResult()
    {
        if (this.isBotGame || this.profileRecordedForRound)
        {
            return;
        }

        var localName = this.LocalPlayerName;
        foreach (var payout in this.blackjackPayouts.Where(payout => string.Equals(payout.PlayerName, localName, StringComparison.OrdinalIgnoreCase)))
        {
            var totalReturn = payout.ReturnAmount + payout.InsuranceReturn;
            var net = totalReturn - payout.Bet;
            if (net >= 0)
            {
                this.configuration.TotalEarnings += net;
            }
            else
            {
                this.configuration.TotalLosses += Math.Abs(net);
            }
        }

        this.profileRecordedForRound = true;
        this.saveConfiguration();
    }

    private void DrawBlackjackTable()
    {
        var tableSize = new Vector2(-1, 430);
        ImGui.BeginChild("blackjack-table", tableSize, true);

        var tableMin = ImGui.GetCursorScreenPos();
        var tableMax = tableMin + ImGui.GetContentRegionAvail();
        tableMax.Y = tableMin.Y + 410;
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(tableMin + new Vector2(18, 30), tableMax - new Vector2(18, 18), ImGui.GetColorU32(new Vector4(0.02f, 0.09f, 0.05f, 1f)), 120);
        drawList.AddRectFilled(tableMin + new Vector2(36, 48), tableMax - new Vector2(36, 36), ImGui.GetColorU32(new Vector4(0.01f, 0.36f, 0.15f, 1f)), 96);
        drawList.AddRect(tableMin + new Vector2(36, 48), tableMax - new Vector2(36, 36), ImGui.GetColorU32(new Vector4(0.08f, 0.7f, 0.27f, 1f)), 96, ImDrawFlags.None, 3f);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 22);
        this.CenterText(this.DealerDisplayName);
        this.DrawDealerCards();

        ImGui.Dummy(new Vector2(0, 68));
        this.DrawBlackjackSeats();

        ImGui.EndChild();
    }

    private void DrawDealerCards()
    {
        var labels = new List<string>();
        for (var i = 0; i < this.blackjackTable.DealerCards.Count; i++)
        {
            labels.Add(i == 0 && this.blackjackTable.DealerHoleCardHidden ? "???" : this.blackjackTable.DealerCards[i].ToString());
        }

        while (labels.Count < 2)
        {
            labels.Add("--");
        }

        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetContentRegionAvail().X - 122) / 2));
        this.DrawCardRow(labels, false);
    }

    private void DrawBlackjackSeats()
    {
        var seats = this.blackjackTable.Seats;
        var columns = Math.Max(1, Math.Min(seats.Count, 4));
        ImGui.Columns(columns, "blackjack-seats", false);
        foreach (var seat in seats)
        {
            var active = this.blackjackTable.ActiveSeat == seat && this.blackjackTable.Phase == BlackjackPhase.PlayerTurns;
            var displayName = this.DisplaySeatName(seat.Name);
            ImGui.TextUnformatted(active ? $"> {displayName}" : displayName);
            ImGui.TextUnformatted(this.isBotGame ? "No bet" : $"Bet {seat.InitialBet:N0}");
            foreach (var hand in seat.Hands)
            {
                ImGui.TextUnformatted($"Total {hand.Total} {hand.Outcome}");
                this.DrawCardRow(hand.Cards.Select(card => card.ToString()).ToArray(), false);
            }

            if (seat.Hands.Count == 0)
            {
                ImGui.TextUnformatted(seat.IsReady ? "Ready" : "Not ready");
            }

            ImGui.NextColumn();
        }

        ImGui.Columns(1);
    }

    private void DrawLobbyBar()
    {
        ImGui.TextUnformatted($"Gil detected: {this.configuration.LastKnownGil:N0}");
        ImGui.SameLine();
        ImGui.TextUnformatted(this.connectionState == TableConnectionState.NotJoined
            ? "No table joined"
            : $"{this.connectionState} room {this.activeRoomCode}");
        ImGui.Separator();
    }

    private void DrawNotJoinedPanel()
    {
        ImGui.Dummy(new Vector2(0, 80));
        this.CenterText("Havent joined a game");
        this.CenterText("Host a private table or join with an invite room number.");
        ImGui.Dummy(new Vector2(0, 24));

        var buttonWidth = 160f;
        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetContentRegionAvail().X - ((buttonWidth * 2) + 12)) / 2));
        if (ImGui.Button("Host Table", new Vector2(buttonWidth, 36)))
        {
            this.HostTable();
        }

        ImGui.SameLine();
        if (ImGui.Button("Join Table", new Vector2(buttonWidth, 36)))
        {
            this.JoinTable();
        }

        ImGui.SetNextItemWidth(180);
        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetContentRegionAvail().X - 180) / 2));
        ImGui.InputText("##room-code", ref this.joinRoomCode, 16);
        this.CenterText("Room networking is local-only until a relay server is added.");
    }

    private void DrawPokerTableControls()
    {
        ImGui.TextUnformatted($"Texas Hold'em - {this.demoTable.Phase}");
        ImGui.SameLine();
        ImGui.TextUnformatted($"Pot: {this.demoTable.Pot:N0}");
        ImGui.SameLine();
        if (ImGui.Button("Leave Table"))
        {
            this.connectionState = TableConnectionState.NotJoined;
            this.tableMessage = "Havent joined a game";
            return;
        }

        ImGui.InputText("Name", ref this.playerName, 32);
        ImGui.InputInt("Buy-in", ref this.buyIn, 1000, 10000);
        if (ImGui.Button("Seat Demo Player"))
        {
            try
            {
                this.demoTable.SeatPlayer(this.playerName.Trim(), Math.Max(1, this.buyIn));
            }
            catch
            {
                this.tableMessage = "That player could not be seated.";
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Start Hand"))
        {
            this.StartHand();
        }

        ImGui.SameLine();
        if (ImGui.Button("Next Phase"))
        {
            this.AdvancePhase();
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset Hand"))
        {
            this.demoTable.StartHand();
            this.tableMessage = "New hand started.";
        }
    }

    private void StartHand()
    {
        try
        {
            this.demoTable.StartHand();
            this.tableMessage = "Pre-flop: everyone has two private cards. Betting would happen here.";
        }
        catch (Exception ex)
        {
            this.tableMessage = ex.Message;
        }
    }

    private void AdvancePhase()
    {
        try
        {
            this.demoTable.AdvancePhase();
            this.tableMessage = this.demoTable.Phase switch
            {
                PokerPhase.Flop => "Flop: three community cards are now visible.",
                PokerPhase.Turn => "Turn: the fourth community card is now visible.",
                PokerPhase.River => "River: the final community card is now visible.",
                PokerPhase.Showdown => $"Showdown winner: {string.Join(", ", this.demoTable.GetShowdownWinners().Select(winner => winner.Name))}",
                PokerPhase.Waiting => "Hand complete. Start another hand when ready.",
                _ => "Phase advanced.",
            };
        }
        catch (Exception ex)
        {
            this.tableMessage = ex.Message;
        }
    }

    private void DrawTable()
    {
        var tableSize = new Vector2(-1, 430);
        ImGui.BeginChild("poker-table", tableSize, true);

        var tableMin = ImGui.GetCursorScreenPos();
        var tableMax = tableMin + ImGui.GetContentRegionAvail();
        tableMax.Y = tableMin.Y + 410;
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(tableMin + new Vector2(18, 30), tableMax - new Vector2(18, 18), ImGui.GetColorU32(new Vector4(0.02f, 0.09f, 0.05f, 1f)), 120);
        drawList.AddRectFilled(tableMin + new Vector2(36, 48), tableMax - new Vector2(36, 36), ImGui.GetColorU32(new Vector4(0.02f, 0.42f, 0.16f, 1f)), 96);
        drawList.AddRect(tableMin + new Vector2(36, 48), tableMax - new Vector2(36, 36), ImGui.GetColorU32(new Vector4(0.08f, 0.72f, 0.26f, 1f)), 96, ImDrawFlags.None, 3f);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 22);
        var opponents = this.demoTable.Players.Skip(1).ToArray();
        this.DrawSeatRow(opponents);

        ImGui.Dummy(new Vector2(0, 30));
        this.DrawCommunityCards();
        ImGui.Dummy(new Vector2(0, 34));

        var localPlayer = this.demoTable.Players.FirstOrDefault();
        if (localPlayer is not null)
        {
            ImGui.TextUnformatted(localPlayer.Name);
            ImGui.SameLine();
            ImGui.TextUnformatted($"Stack {localPlayer.Stack:N0}");
            this.DrawCardRow(localPlayer.HoleCards.Select(card => card.ToString()).ToArray(), false);
        }

        ImGui.EndChild();
    }

    private void DrawSeatRow(IReadOnlyList<PokerPlayer> players)
    {
        if (players.Count == 0)
        {
            ImGui.TextUnformatted("Waiting for other players...");
            return;
        }

        var columns = Math.Min(players.Count, 4);
        ImGui.Columns(columns, "opponent-seats", false);
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            ImGui.TextUnformatted(player.Name);
            ImGui.TextUnformatted($"Stack {player.Stack:N0}");
            var showCards = this.demoTable.Phase == PokerPhase.Showdown;
            var cardLabels = showCards && player.HoleCards.Count == 2
                ? player.HoleCards.Select(card => card.ToString()).ToArray()
                : new[] { "??", "??" };
            this.DrawCardRow(cardLabels, !showCards);
            ImGui.NextColumn();
        }

        ImGui.Columns(1);
    }

    private void DrawCommunityCards()
    {
        this.CenterText("Board");
        var visibleCards = this.demoTable.VisibleCommunityCards.Select(card => card.ToString()).ToList();
        while (visibleCards.Count < 5)
        {
            visibleCards.Add("--");
        }

        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetContentRegionAvail().X - 305) / 2));
        this.DrawCardRow(visibleCards.ToArray(), false);
    }

    private void DrawCardRow(IReadOnlyList<string> labels, bool hidden)
    {
        for (var i = 0; i < labels.Count; i++)
        {
            ImGui.PushID($"{labels[i]}-{i}-{hidden}");
            this.DrawCard(labels[i], hidden || labels[i] == "--" || labels[i] == "???");
            ImGui.PopID();

            if (i < labels.Count - 1)
            {
                ImGui.SameLine();
            }
        }
    }

    private void DrawCard(string label, bool muted)
    {
        var cardSize = new Vector2(56, 78);
        var isRed = label.Contains('H') || label.Contains('D');
        var color = muted ? new Vector4(0.12f, 0.14f, 0.16f, 1f) : new Vector4(0.94f, 0.94f, 0.88f, 1f);
        var textColor = muted ? new Vector4(0.7f, 0.75f, 0.82f, 1f) : new Vector4(0.08f, 0.09f, 0.11f, 1f);
        if (!muted && isRed)
        {
            textColor = new Vector4(0.82f, 0.04f, 0.04f, 1f);
        }

        ImGui.PushStyleColor(ImGuiCol.Button, color);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, color);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, color);
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.Button(label, cardSize);
        ImGui.PopStyleColor(4);
    }

    private void DrawSettingsTab()
    {
        this.RefreshGilSnapshot();
        if (ImGui.BeginTabBar("settings-tabs"))
        {
            if (ImGui.BeginTabItem("Table Setup"))
            {
                this.DrawTableSetupSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Player Profile"))
            {
                this.DrawPlayerProfileSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Ledger"))
            {
                this.DrawLedgerTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Rules"))
            {
                this.DrawRulesSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Plugin"))
            {
                this.DrawPluginSettings();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawTableSetupSettings()
    {
        ImGui.TextUnformatted($"Detected character: {this.LocalPlayerName} (you)");
        ImGui.TextUnformatted($"Gil detected: {this.configuration.LastKnownGil:N0}");
        ImGui.TextWrapped(this.relayStatus);
        ImGui.Separator();

        if (this.connectionState == TableConnectionState.NotJoined)
        {
            var buttonWidth = 130f;
            if (ImGui.Button("Host Game", new Vector2(buttonWidth, 32)))
            {
                _ = this.HostBlackjackTableAsync();
            }

            ImGui.SameLine();
            if (ImGui.Button("Join Game", new Vector2(buttonWidth, 32)))
            {
                _ = this.JoinBlackjackTableAsync();
            }

            ImGui.SameLine();
            if (ImGui.Button("Bot Game", new Vector2(buttonWidth, 32)))
            {
                this.StartBotBlackjackGame();
            }

            ImGui.SetNextItemWidth(180);
            ImGui.InputText("Invite room number", ref this.joinRoomCode, 16);
        }
        else
        {
            ImGui.TextUnformatted(this.isBotGame ? "Mode: Bot game" : $"{this.connectionState} room {this.activeRoomCode}");
            ImGui.SameLine();
            if (ImGui.Button("Open Table"))
            {
                this.IsOpen = true;
            }

            ImGui.SameLine();
            if (ImGui.Button("Leave Game"))
            {
                _ = this.relayClient.DisconnectAsync();
                this.connectionState = TableConnectionState.NotJoined;
                this.isBotGame = false;
                this.hostPlayerName = string.Empty;
                this.blackjackMessage = "Havent joined a game";
            }
        }

        ImGui.Spacing();
        this.DrawRelayPlayerList();
        ImGui.Spacing();
        ImGui.TextWrapped("Gameplay controls appear in the Open table window. Settings only manages setup, profile, and plugin options.");
    }

    private void DrawRelayPlayerList()
    {
        if (this.connectionState == TableConnectionState.NotJoined || this.isBotGame)
        {
            return;
        }

        ImGui.TextUnformatted("Connected players");
        foreach (var seat in this.blackjackTable.Seats)
        {
            ImGui.BulletText(this.DisplaySeatName(seat.Name));
        }
    }

    private void DrawPlayerProfileSettings()
    {
        ImGui.TextUnformatted(this.LocalPlayerName);
        ImGui.Separator();
        ImGui.TextUnformatted($"Total earnings: {this.configuration.TotalEarnings:N0}");
        ImGui.TextUnformatted($"Total losses: {this.configuration.TotalLosses:N0}");
        ImGui.TextUnformatted($"Overall: {this.configuration.NetProfit:N0}");

        if (ImGui.Button("Reset Profile Stats"))
        {
            this.configuration.TotalEarnings = 0;
            this.configuration.TotalLosses = 0;
            this.saveConfiguration();
        }
    }

    private void DrawRulesSettings()
    {
        if (ImGui.BeginTabBar("rules-tabs"))
        {
            if (ImGui.BeginTabItem("Blackjack"))
            {
                this.DrawBlackjackRules();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Poker"))
            {
                ImGui.TextWrapped("Poker is benched for now. Rules will be added when poker becomes playable.");
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawBlackjackRules()
    {
        ImGui.BeginChild("blackjack-rules", new Vector2(0, 0), false);

        ImGui.TextUnformatted("Goal");
        ImGui.BulletText("Beat the dealer by getting closer to 21 without going over.");
        ImGui.BulletText("Number cards count as printed. J, Q, and K count as 10. Aces count as 1 or 11.");
        ImGui.BulletText("No Push 22 rule: if the dealer busts on 22, it is still a dealer bust.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Betting");
        ImGui.BulletText("In hosted games, the host is the dealer and banker.");
        ImGui.BulletText("Players trade their original bet to the host before readying.");
        ImGui.BulletText("The host enters the amount they actually received from each player.");
        ImGui.BulletText("If players lose, the host keeps the bet. If players win, the host manually pays the calculated return.");
        ImGui.BulletText("Bot games are no bet practice rounds and do not affect profile stats.");
        ImGui.BulletText("Winning normal hands return 2x the bet, including the original bet.");
        ImGui.BulletText("Push returns the original bet.");
        ImGui.BulletText("Losing or busting returns 0.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Blackjack");
        ImGui.BulletText("A 21 from the first two cards is blackjack.");
        ImGui.BulletText("Blackjack pays 3:2 plus the original bet. A 10 gil bet returns 25 gil.");
        ImGui.BulletText("A split ace 21 is treated as 21, not natural blackjack.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Player Actions");
        ImGui.BulletText("Hit Me: draw one card. If the hand goes over 21, it busts and the turn advances.");
        ImGui.BulletText("Stand: keep the current total and end that hand's action.");
        ImGui.BulletText("Five successful hits without busting automatically wins that hand.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Double Down");
        ImGui.BulletText("Available only after the first two cards of a hand.");
        ImGui.BulletText("The extra bet may not exceed the original bet.");
        ImGui.BulletText("After doubling down, exactly one card is dealt and the hand automatically stands.");
        ImGui.BulletText("Hosted games require the host to confirm the extra trade amount first.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Splitting");
        ImGui.BulletText("Available only when the first two cards have the same numerical value.");
        ImGui.BulletText("The second hand's bet must equal the original bet.");
        ImGui.BulletText("Hosted games require the host to confirm the matching split trade first.");
        ImGui.BulletText("Split aces receive exactly one card on each hand, then the turn advances.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Insurance");
        ImGui.BulletText("Available only when the dealer's visible card is an Ace.");
        ImGui.BulletText("Insurance may be up to half of the original bet.");
        ImGui.BulletText("Insurance pays 2:1 if the dealer has blackjack.");
        ImGui.BulletText("Insurance loses in all other cases.");

        ImGui.Spacing();
        ImGui.TextUnformatted(this.DealerDisplayName);
        ImGui.BulletText("The dealer has no choices.");
        ImGui.BulletText("The dealer must hit on 16 or lower.");
        ImGui.BulletText("The dealer must stand on 17 or higher, including soft 17.");

        ImGui.EndChild();
    }

    private void DrawPluginSettings()
    {
        ImGui.InputInt("Default buy-in", ref this.defaultBuyIn, 1000, 10000);
        if (this.defaultBuyIn < 1)
        {
            this.defaultBuyIn = 1;
        }

        if (ImGui.Button("Save Settings"))
        {
            this.configuration.DefaultBuyIn = this.defaultBuyIn;
            this.buyIn = this.configuration.DefaultBuyIn;
            this.saveConfiguration();
        }

        ImGui.Spacing();
        ImGui.TextWrapped("Room codes are generated locally in this prototype. A relay server will be required to reserve room numbers globally and synchronize table actions between players.");
    }

    private void HostTable()
    {
        this.connectionState = TableConnectionState.Hosting;
        this.activeRoomCode = Random.Shared.Next(100000, 1000000).ToString();
        this.configuration.LastRoomCode = this.activeRoomCode;
        this.saveConfiguration();
        this.StartHand();
        this.tableMessage = $"Hosting room {this.activeRoomCode}. Share this code once the relay server exists.";
    }

    private async Task HostBlackjackTableAsync()
    {
        try
        {
            this.blackjackMessage = "Connecting to relay...";
            this.configuration.RelayUrl = this.relayUrl.Trim();
            this.saveConfiguration();
            var roomCode = await this.relayClient.HostAsync(this.configuration.RelayUrl, this.LocalPlayerName);
            this.hostPlayerName = this.LocalPlayerName;
            this.ResetBlackjackSeats(includeLocalPlayer: false);
            this.connectionState = TableConnectionState.Hosting;
            this.isBotGame = false;
            this.profileRecordedForRound = false;
            this.activeRoomCode = roomCode;
            this.configuration.LastRoomCode = this.activeRoomCode;
            this.saveConfiguration();
            this.OpenBlackjackReadyCheck();
            this.IsOpen = true;
            this.blackjackMessage = $"Hosting blackjack room {this.activeRoomCode}. You are the dealer/banker; players trade bets to you before readying.";
        }
        catch (Exception ex)
        {
            this.connectionState = TableConnectionState.NotJoined;
            this.blackjackMessage = $"Could not host via relay: {ex.Message}";
            this.relayStatus = this.blackjackMessage;
        }
    }

    private async Task JoinBlackjackTableAsync()
    {
        var roomCode = this.joinRoomCode.Trim();
        if (roomCode.Length == 0)
        {
            this.blackjackMessage = "Enter a room code first.";
            return;
        }

        try
        {
            this.blackjackMessage = "Connecting to relay...";
            this.configuration.RelayUrl = this.relayUrl.Trim();
            this.saveConfiguration();
            await this.relayClient.JoinAsync(this.configuration.RelayUrl, roomCode, this.LocalPlayerName);
            this.hostPlayerName = string.Empty;
            this.ResetBlackjackSeats(includeLocalPlayer: true);
            this.connectionState = TableConnectionState.Joined;
            this.isBotGame = false;
            this.profileRecordedForRound = false;
            this.activeRoomCode = roomCode;
            this.configuration.LastRoomCode = roomCode;
            this.saveConfiguration();
            this.OpenBlackjackReadyCheck();
            this.IsOpen = true;
            this.blackjackMessage = $"Joined blackjack room {roomCode}. Waiting for table updates.";
        }
        catch (Exception ex)
        {
            this.connectionState = TableConnectionState.NotJoined;
            this.blackjackMessage = $"Could not join relay room: {ex.Message}";
            this.relayStatus = this.blackjackMessage;
        }
    }

    private void StartBotBlackjackGame()
    {
        this.hostPlayerName = "AI Dealer";
        this.ResetBlackjackSeats(includeLocalPlayer: true);
        this.connectionState = TableConnectionState.Bot;
        this.isBotGame = true;
        this.profileRecordedForRound = true;
        this.activeRoomCode = "BOT";
        this.blackjackTable.StartFreeRound();
        this.blackjackPayouts = [];
        this.IsOpen = true;
        this.blackjackMessage = "Bot game started. No bet, no gil risk, no profile stats. Dealer hits 16 or lower and stands on 17+.";
    }

    private void ResetBlackjackSeats(bool includeLocalPlayer)
    {
        this.blackjackTable.ClearSeats();
        if (includeLocalPlayer)
        {
            this.blackjackTable.SeatPlayer(this.LocalPlayerName);
        }

        this.blackjackBetInputs.Clear();
        this.blackjackInsuranceInputs.Clear();
        foreach (var seat in this.blackjackTable.Seats)
        {
            this.blackjackBetInputs[seat.Name] = this.defaultBuyIn;
        }
    }

    private void ApplyRelayPlayers(IReadOnlyList<string> playerNames)
    {
        if (this.isBotGame || this.connectionState == TableConnectionState.NotJoined)
        {
            return;
        }

        if (this.blackjackTable.Phase is BlackjackPhase.PlayerTurns or BlackjackPhase.DealerTurn or BlackjackPhase.Settled)
        {
            this.relayStatus = "Relay player list changed; seats will refresh next round.";
            return;
        }

        var existingBets = this.blackjackTable.Seats.ToDictionary(seat => seat.Name, seat => seat.InitialBet, StringComparer.OrdinalIgnoreCase);
        var existingReady = this.blackjackTable.Seats.ToDictionary(seat => seat.Name, seat => seat.IsReady, StringComparer.OrdinalIgnoreCase);
        var wasReadyCheck = this.blackjackTable.Phase == BlackjackPhase.ReadyCheck;
        var seats = playerNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => !string.Equals(name, this.hostPlayerName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        if (seats.Length == 0 && this.connectionState == TableConnectionState.Joined)
        {
            seats = new[] { this.LocalPlayerName };
        }

        this.blackjackTable.ClearSeats();
        foreach (var name in seats)
        {
            this.blackjackTable.SeatPlayer(name);
            if (!this.blackjackBetInputs.ContainsKey(name))
            {
                this.blackjackBetInputs[name] = this.defaultBuyIn;
            }
        }

        if (wasReadyCheck)
        {
            this.blackjackTable.OpenReadyCheck();
            foreach (var seat in this.blackjackTable.Seats)
            {
                if (existingBets.TryGetValue(seat.Name, out var bet))
                {
                    this.blackjackTable.SetBet(seat.Name, bet);
                    this.blackjackBetInputs[seat.Name] = (int)Math.Max(0, bet);
                }

                if (existingReady.TryGetValue(seat.Name, out var ready) && ready && seat.InitialBet > 0)
                {
                    this.blackjackTable.SetReady(seat.Name, true);
                }
            }
        }

        this.relayStatus = $"Relay synced {this.blackjackTable.Seats.Count} player(s).";
    }

    private void OpenBlackjackReadyCheck()
    {
        try
        {
            this.blackjackTable.OpenReadyCheck();
            foreach (var seat in this.blackjackTable.Seats)
            {
                this.blackjackBetInputs[seat.Name] = Math.Max(this.defaultBuyIn, (int)seat.InitialBet);
            }

            this.blackjackPayouts = [];
            this.profileRecordedForRound = this.isBotGame;
        }
        catch (Exception ex)
        {
            this.blackjackMessage = ex.Message;
        }
    }

    private void JoinTable()
    {
        var roomCode = this.joinRoomCode.Trim();
        if (roomCode.Length == 0)
        {
            this.tableMessage = "Enter a room code first.";
            return;
        }

        this.connectionState = TableConnectionState.Joined;
        this.activeRoomCode = roomCode;
        this.configuration.LastRoomCode = roomCode;
        this.saveConfiguration();
        this.StartHand();
        this.tableMessage = $"Joined room {roomCode} locally. Real sync needs the relay server.";
    }

    private void RefreshGilSnapshot()
    {
        var gil = this.getCurrentGil();
        if (gil > 0 && gil != this.configuration.LastKnownGil)
        {
            this.configuration.LastKnownGil = gil;
            this.saveConfiguration();
        }
    }

    private int GetBlackjackBetInput(BlackjackSeat seat)
    {
        if (!this.blackjackBetInputs.TryGetValue(seat.Name, out var bet))
        {
            bet = seat.InitialBet > 0 ? (int)seat.InitialBet : this.defaultBuyIn;
            this.blackjackBetInputs[seat.Name] = bet;
        }

        return bet;
    }

    private int GetInsuranceInput(BlackjackSeat seat)
    {
        if (!this.blackjackInsuranceInputs.TryGetValue(seat.Name, out var bet))
        {
            bet = 0;
            this.blackjackInsuranceInputs[seat.Name] = bet;
        }

        return bet;
    }

    private string DisplaySeatName(string name) =>
        string.Equals(name, this.LocalPlayerName, StringComparison.OrdinalIgnoreCase) ? $"{name} (you)" : name;

    private bool ActionButton(string label, bool enabled)
    {
        if (!enabled)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.45f);
        }

        var clicked = ImGui.Button(label) && enabled;
        if (!enabled)
        {
            ImGui.PopStyleVar();
        }

        return clicked;
    }

    private void CenterText(string text)
    {
        var width = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetContentRegionAvail().X - width) / 2));
        ImGui.TextUnformatted(text);
    }

    private enum TableConnectionState
    {
        NotJoined,
        Hosting,
        Joined,
        Bot,
    }

    private void DrawLedgerTab()
    {
        ImGui.TextWrapped(this.settlementNote);
        ImGui.InputTextMultiline("Settlement notes", ref this.settlementNote, 512, new Vector2(-1, 180));
    }
}

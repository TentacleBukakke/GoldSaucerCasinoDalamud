using GoldSaucerCasino.Core.Cards;

namespace GoldSaucerCasino.Core.Blackjack;

public sealed class BlackjackTable
{
    private readonly List<BlackjackSeat> seats = [];
    private readonly List<Card> dealerCards = [];
    private Deck? deck;

    public IReadOnlyList<BlackjackSeat> Seats => this.seats;

    public IReadOnlyList<Card> DealerCards => this.dealerCards;

    public BlackjackPhase Phase { get; private set; } = BlackjackPhase.Lobby;

    public int ActiveSeatIndex { get; private set; }

    public BlackjackSeat? ActiveSeat => this.ActiveSeatIndex < this.seats.Count ? this.seats[this.ActiveSeatIndex] : null;

    public bool DealerHoleCardHidden => this.Phase is BlackjackPhase.PlayerTurns or BlackjackPhase.ReadyCheck;

    public void SeatPlayer(string name)
    {
        if (this.seats.Count >= 5)
        {
            throw new InvalidOperationException("The blackjack table is full.");
        }

        if (this.seats.Any(seat => string.Equals(seat.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("That player is already seated.");
        }

        this.seats.Add(new BlackjackSeat(name));
    }

    public void ClearSeats()
    {
        this.seats.Clear();
        this.dealerCards.Clear();
        this.ActiveSeatIndex = 0;
        this.Phase = BlackjackPhase.Lobby;
    }

    public void OpenReadyCheck()
    {
        if (this.seats.Count == 0)
        {
            throw new InvalidOperationException("Seat at least one player first.");
        }

        foreach (var seat in this.seats)
        {
            seat.ResetForRound();
        }

        this.dealerCards.Clear();
        this.Phase = BlackjackPhase.ReadyCheck;
    }

    public void SetBet(string playerName, long amount) => this.FindSeat(playerName).SetBet(amount);

    public void SetReady(string playerName, bool ready) => this.FindSeat(playerName).SetReady(ready);

    public bool CanStartRound => this.Phase == BlackjackPhase.ReadyCheck && this.seats.Count > 0 && this.seats.All(seat => seat.IsReady && seat.InitialBet > 0);

    public void StartRound(Random? random = null)
    {
        if (!this.CanStartRound)
        {
            throw new InvalidOperationException("All players need a host-entered bet and ready state.");
        }

        this.DealRound(random);
    }

    public void StartFreeRound(Random? random = null)
    {
        if (this.seats.Count == 0)
        {
            throw new InvalidOperationException("Seat at least one player first.");
        }

        foreach (var seat in this.seats)
        {
            seat.SetBet(0);
        }

        this.DealRound(random);
    }

    private void DealRound(Random? random)
    {
        this.deck = Deck.Shuffled(random);
        this.dealerCards.Clear();
        foreach (var seat in this.seats)
        {
            seat.StartHand();
        }

        for (var i = 0; i < 2; i++)
        {
            foreach (var seat in this.seats)
            {
                seat.Hands[0].AddInitialCard(this.Draw());
            }

            this.dealerCards.Add(this.Draw());
        }

        foreach (var seat in this.seats)
        {
            if (seat.Hands[0].IsNaturalBlackjack)
            {
                seat.Hands[0].Stand();
            }
        }

        this.ActiveSeatIndex = 0;
        this.AdvanceToNextPlayableSeat();
        this.Phase = this.ActiveSeat is null ? BlackjackPhase.DealerTurn : BlackjackPhase.PlayerTurns;
    }

    public bool CanOfferInsurance => this.Phase == BlackjackPhase.PlayerTurns && this.dealerCards.Count >= 2 && this.dealerCards[1].Rank == Rank.Ace;

    public void SetInsuranceBet(string playerName, long amount)
    {
        if (!this.CanOfferInsurance)
        {
            throw new InvalidOperationException("Insurance is only available when the dealer shows an ace.");
        }

        this.FindSeat(playerName).SetInsuranceBet(amount);
    }

    public void Hit()
    {
        var hand = this.RequireActiveHand();
        hand.Hit(this.Draw());
        this.AdvanceAfterAction();
    }

    public void Stand()
    {
        this.RequireActiveHand().Stand();
        this.AdvanceAfterAction();
    }

    public void DoubleDown(long extraBet)
    {
        this.RequireActiveHand().DoubleDown(this.Draw(), extraBet);
        this.AdvanceAfterAction();
    }

    public void Split(long matchedBet)
    {
        var seat = this.ActiveSeat ?? throw new InvalidOperationException("No active player.");
        var original = seat.ActiveHand ?? throw new InvalidOperationException("No active hand.");
        var secondHand = seat.SplitActiveHand(matchedBet);
        var splitAces = original.Cards[0].Rank == Rank.Ace && secondHand.Cards[0].Rank == Rank.Ace;
        original.IsSplitAces = splitAces;
        secondHand.IsSplitAces = splitAces;
        original.AddInitialCard(this.Draw());
        secondHand.AddInitialCard(this.Draw());

        if (splitAces)
        {
            original.Stand();
            secondHand.Stand();
            this.AdvanceAfterAction();
        }
    }

    public IReadOnlyList<BlackjackPayout> FinishDealerAndSettle()
    {
        this.Phase = BlackjackPhase.DealerTurn;
        while (BlackjackRules.DealerShouldHit(this.dealerCards))
        {
            this.dealerCards.Add(this.Draw());
        }

        var dealerTotal = BlackjackRules.BestTotal(this.dealerCards);
        var dealerBlackjack = BlackjackRules.IsBlackjack(this.dealerCards);
        var dealerBust = dealerTotal > 21;
        var payouts = new List<BlackjackPayout>();

        foreach (var seat in this.seats)
        {
            for (var i = 0; i < seat.Hands.Count; i++)
            {
                var hand = seat.Hands[i];
                var outcome = hand.Outcome;
                if (outcome == BlackjackOutcome.Pending)
                {
                    outcome = ResolveOutcome(hand, dealerTotal, dealerBust, dealerBlackjack);
                    hand.SetOutcome(outcome);
                }

                var returnAmount = CalculateReturn(hand.Bet, outcome);
                var insuranceReturn = dealerBlackjack && seat.InsuranceBet > 0 ? seat.InsuranceBet * 3 : 0;
                payouts.Add(new BlackjackPayout(seat.Name, i + 1, hand.Bet, outcome, returnAmount, insuranceReturn));
            }
        }

        this.Phase = BlackjackPhase.Settled;
        return payouts;
    }

    private static BlackjackOutcome ResolveOutcome(BlackjackHand hand, int dealerTotal, bool dealerBust, bool dealerBlackjack)
    {
        if (hand.IsBust)
        {
            return BlackjackOutcome.Bust;
        }

        if (hand.IsFiveHitWin)
        {
            return BlackjackOutcome.FiveHitWin;
        }

        if (hand.IsNaturalBlackjack)
        {
            return dealerBlackjack ? BlackjackOutcome.Push : BlackjackOutcome.Blackjack;
        }

        if (dealerBlackjack)
        {
            return BlackjackOutcome.Lose;
        }

        if (dealerBust || hand.Total > dealerTotal)
        {
            return BlackjackOutcome.Win;
        }

        if (hand.Total == dealerTotal)
        {
            return BlackjackOutcome.Push;
        }

        return BlackjackOutcome.Lose;
    }

    public static long CalculateReturn(long bet, BlackjackOutcome outcome) => outcome switch
    {
        BlackjackOutcome.Blackjack => bet + (bet * 3 / 2),
        BlackjackOutcome.Win or BlackjackOutcome.FiveHitWin => bet * 2,
        BlackjackOutcome.Push => bet,
        _ => 0,
    };

    private BlackjackSeat FindSeat(string playerName) =>
        this.seats.FirstOrDefault(seat => string.Equals(seat.Name, playerName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("Player is not seated.");

    private BlackjackHand RequireActiveHand() =>
        this.ActiveSeat?.ActiveHand ?? throw new InvalidOperationException("No active hand.");

    private Card Draw() => (this.deck ?? throw new InvalidOperationException("No deck has been created.")).Draw();

    private void AdvanceAfterAction()
    {
        this.ActiveSeat?.AdvanceHand();
        this.AdvanceToNextPlayableSeat();
        if (this.ActiveSeat is null)
        {
            this.Phase = BlackjackPhase.DealerTurn;
        }
    }

    private void AdvanceToNextPlayableSeat()
    {
        while (this.ActiveSeatIndex < this.seats.Count)
        {
            var seat = this.seats[this.ActiveSeatIndex];
            seat.AdvanceHand();
            if (!seat.IsDone)
            {
                return;
            }

            this.ActiveSeatIndex++;
        }
    }
}

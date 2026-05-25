using GoldSaucerCasino.Core.Cards;

namespace GoldSaucerCasino.Core.Blackjack;

public sealed class BlackjackHand
{
    private readonly List<Card> cards = [];

    public BlackjackHand(long bet)
    {
        this.Bet = bet;
    }

    public long Bet { get; private set; }

    public IReadOnlyList<Card> Cards => this.cards;

    public bool IsStanding { get; private set; }

    public bool IsDoubled { get; private set; }

    public bool IsSplitAces { get; set; }

    public int HitCount { get; private set; }

    public BlackjackOutcome Outcome { get; private set; } = BlackjackOutcome.Pending;

    public int Total => BlackjackRules.BestTotal(this.cards);

    public bool IsBust => this.Total > 21;

    public bool IsNaturalBlackjack => BlackjackRules.IsBlackjack(this.cards) && !this.IsSplitAces;

    public bool IsFiveHitWin => this.HitCount >= 5 && !this.IsBust;

    public bool CanHit => !this.IsStanding && !this.IsBust && !this.IsFiveHitWin && !this.IsSplitAces;

    public bool CanSplit => BlackjackRules.CanSplit(this.cards);

    public bool CanDoubleDown => this.cards.Count == 2 && !this.IsDoubled && !this.IsSplitAces;

    public void AddInitialCard(Card card) => this.cards.Add(card);

    public void Hit(Card card)
    {
        if (!this.CanHit)
        {
            throw new InvalidOperationException("This hand cannot hit.");
        }

        this.cards.Add(card);
        this.HitCount++;
        this.ApplyAutomaticOutcome();
    }

    public void Stand() => this.IsStanding = true;

    public void DoubleDown(Card card, long extraBet)
    {
        if (!this.CanDoubleDown)
        {
            throw new InvalidOperationException("This hand cannot double down.");
        }

        var freePracticeDouble = this.Bet == 0 && extraBet == 0;
        if (!freePracticeDouble && (extraBet < 1 || extraBet > this.Bet))
        {
            throw new ArgumentOutOfRangeException(nameof(extraBet), "Double-down bet must be between 1 and the original bet.");
        }

        this.Bet += extraBet;
        this.cards.Add(card);
        this.HitCount++;
        this.IsDoubled = true;
        this.IsStanding = true;
        this.ApplyAutomaticOutcome();
    }

    public Card RemoveSplitCard()
    {
        if (!this.CanSplit)
        {
            throw new InvalidOperationException("This hand cannot split.");
        }

        var card = this.cards[1];
        this.cards.RemoveAt(1);
        return card;
    }

    public void SetOutcome(BlackjackOutcome outcome) => this.Outcome = outcome;

    private void ApplyAutomaticOutcome()
    {
        if (this.IsBust)
        {
            this.Outcome = BlackjackOutcome.Bust;
            this.IsStanding = true;
        }
        else if (this.IsFiveHitWin)
        {
            this.Outcome = BlackjackOutcome.FiveHitWin;
            this.IsStanding = true;
        }
    }
}

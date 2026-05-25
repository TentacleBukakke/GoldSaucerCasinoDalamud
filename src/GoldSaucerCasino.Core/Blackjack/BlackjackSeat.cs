namespace GoldSaucerCasino.Core.Blackjack;

public sealed class BlackjackSeat
{
    private readonly List<BlackjackHand> hands = [];

    public BlackjackSeat(string name)
    {
        this.Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Name is required.", nameof(name)) : name;
    }

    public string Name { get; }

    public long InitialBet { get; private set; }

    public long InsuranceBet { get; private set; }

    public bool IsReady { get; private set; }

    public IReadOnlyList<BlackjackHand> Hands => this.hands;

    public int ActiveHandIndex { get; private set; }

    public BlackjackHand? ActiveHand => this.ActiveHandIndex < this.hands.Count ? this.hands[this.ActiveHandIndex] : null;

    public bool IsDone => this.hands.Count > 0 && this.hands.All(hand => hand.Outcome != BlackjackOutcome.Pending || hand.IsStanding);

    public void SetBet(long amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Bet cannot be negative.");
        }

        this.InitialBet = amount;
    }

    public void SetInsuranceBet(long amount)
    {
        if (amount < 0 || amount > this.InitialBet / 2)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Insurance cannot exceed half the original bet.");
        }

        this.InsuranceBet = amount;
    }

    public void SetReady(bool ready)
    {
        if (ready && this.InitialBet <= 0)
        {
            throw new InvalidOperationException("A ready player needs a host-entered bet.");
        }

        this.IsReady = ready;
    }

    public void ResetForRound()
    {
        this.hands.Clear();
        this.ActiveHandIndex = 0;
        this.InsuranceBet = 0;
        this.IsReady = false;
    }

    public void StartHand()
    {
        this.hands.Clear();
        this.hands.Add(new BlackjackHand(this.InitialBet));
        this.ActiveHandIndex = 0;
    }

    public void AdvanceHand()
    {
        while (this.ActiveHandIndex < this.hands.Count && this.ActiveHand is { } hand && (hand.IsStanding || hand.Outcome != BlackjackOutcome.Pending))
        {
            this.ActiveHandIndex++;
        }
    }

    public BlackjackHand SplitActiveHand(long matchedBet)
    {
        var hand = this.ActiveHand ?? throw new InvalidOperationException("No active hand.");
        if (matchedBet != hand.Bet)
        {
            throw new InvalidOperationException("Split bet must equal the original bet.");
        }

        var splitCard = hand.RemoveSplitCard();
        var newHand = new BlackjackHand(matchedBet);
        newHand.AddInitialCard(splitCard);
        this.hands.Insert(this.ActiveHandIndex + 1, newHand);
        return newHand;
    }
}

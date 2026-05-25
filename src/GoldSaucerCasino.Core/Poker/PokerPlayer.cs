using GoldSaucerCasino.Core.Cards;

namespace GoldSaucerCasino.Core.Poker;

public sealed class PokerPlayer
{
    public PokerPlayer(string name, long buyIn)
    {
        this.Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Name is required.", nameof(name)) : name;
        this.Stack = buyIn > 0 ? buyIn : throw new ArgumentOutOfRangeException(nameof(buyIn), "Buy-in must be positive.");
    }

    public string Name { get; }

    public long Stack { get; private set; }

    public IReadOnlyList<Card> HoleCards => this.holeCards;

    private readonly List<Card> holeCards = new(2);

    public void Receive(Card card)
    {
        if (this.holeCards.Count == 2)
        {
            throw new InvalidOperationException("A poker player cannot receive more than two hole cards.");
        }

        this.holeCards.Add(card);
    }

    public void ClearHand() => this.holeCards.Clear();

    public void Credit(long amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
        }

        this.Stack += amount;
    }

    public void Debit(long amount)
    {
        if (amount < 0 || amount > this.Stack)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive and no larger than stack.");
        }

        this.Stack -= amount;
    }
}

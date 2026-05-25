namespace GoldSaucerCasino.Core.Cards;

public sealed class Deck
{
    private readonly List<Card> cards;
    private int position;

    private Deck(IEnumerable<Card> cards)
    {
        this.cards = cards.ToList();
    }

    public int Remaining => this.cards.Count - this.position;

    public static Deck Shuffled(Random? random = null)
    {
        random ??= Random.Shared;
        var cards = Enum.GetValues<Suit>()
            .SelectMany(suit => Enum.GetValues<Rank>().Select(rank => new Card(rank, suit)))
            .ToList();

        for (var i = cards.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }

        return new Deck(cards);
    }

    public Card Draw()
    {
        if (this.position >= this.cards.Count)
        {
            throw new InvalidOperationException("The deck is empty.");
        }

        return this.cards[this.position++];
    }
}

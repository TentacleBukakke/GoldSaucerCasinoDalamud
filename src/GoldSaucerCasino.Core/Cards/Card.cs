namespace GoldSaucerCasino.Core.Cards;

public readonly record struct Card(Rank Rank, Suit Suit)
{
    public override string ToString() => $"{Rank.ToShortName()}{Suit.ToSymbol()}";
}

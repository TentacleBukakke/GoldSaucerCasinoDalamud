namespace GoldSaucerCasino.Core.Cards;

public enum Suit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades,
}

public static class SuitExtensions
{
    public static string ToSymbol(this Suit suit) => suit switch
    {
        Suit.Clubs => "C",
        Suit.Diamonds => "D",
        Suit.Hearts => "H",
        Suit.Spades => "S",
        _ => "?",
    };
}

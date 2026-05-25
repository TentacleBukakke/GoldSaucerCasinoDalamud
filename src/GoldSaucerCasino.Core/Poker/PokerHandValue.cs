using GoldSaucerCasino.Core.Cards;

namespace GoldSaucerCasino.Core.Poker;

public sealed record PokerHandValue(PokerHandCategory Category, IReadOnlyList<Rank> TieBreakers)
    : IComparable<PokerHandValue>
{
    public int CompareTo(PokerHandValue? other)
    {
        if (other is null)
        {
            return 1;
        }

        var categoryCompare = this.Category.CompareTo(other.Category);
        if (categoryCompare != 0)
        {
            return categoryCompare;
        }

        for (var i = 0; i < Math.Min(this.TieBreakers.Count, other.TieBreakers.Count); i++)
        {
            var rankCompare = this.TieBreakers[i].CompareTo(other.TieBreakers[i]);
            if (rankCompare != 0)
            {
                return rankCompare;
            }
        }

        return this.TieBreakers.Count.CompareTo(other.TieBreakers.Count);
    }
}

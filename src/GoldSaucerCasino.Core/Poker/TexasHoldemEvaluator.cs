using GoldSaucerCasino.Core.Cards;

namespace GoldSaucerCasino.Core.Poker;

public static class TexasHoldemEvaluator
{
    public static PokerHandValue EvaluateBest(IEnumerable<Card> cards)
    {
        var allCards = cards.Distinct().ToArray();
        if (allCards.Length < 5)
        {
            throw new ArgumentException("At least five cards are required.", nameof(cards));
        }

        PokerHandValue? best = null;

        for (var a = 0; a < allCards.Length - 4; a++)
        for (var b = a + 1; b < allCards.Length - 3; b++)
        for (var c = b + 1; c < allCards.Length - 2; c++)
        for (var d = c + 1; d < allCards.Length - 1; d++)
        for (var e = d + 1; e < allCards.Length; e++)
        {
            var value = EvaluateFive(new[] { allCards[a], allCards[b], allCards[c], allCards[d], allCards[e] });
            if (best is null || value.CompareTo(best) > 0)
            {
                best = value;
            }
        }

        return best!;
    }

    private static PokerHandValue EvaluateFive(IReadOnlyCollection<Card> cards)
    {
        var ranksDescending = cards
            .Select(card => card.Rank)
            .OrderByDescending(rank => rank)
            .ToArray();
        var groups = ranksDescending
            .GroupBy(rank => rank)
            .Select(group => new { Rank = group.Key, Count = group.Count() })
            .OrderByDescending(group => group.Count)
            .ThenByDescending(group => group.Rank)
            .ToArray();
        var isFlush = cards.Select(card => card.Suit).Distinct().Count() == 1;
        var straightHigh = GetStraightHigh(ranksDescending);

        if (isFlush && straightHigh is not null)
        {
            return new PokerHandValue(PokerHandCategory.StraightFlush, new[] { straightHigh.Value });
        }

        if (groups[0].Count == 4)
        {
            return new PokerHandValue(PokerHandCategory.FourOfAKind, new[] { groups[0].Rank, groups[1].Rank });
        }

        if (groups[0].Count == 3 && groups[1].Count == 2)
        {
            return new PokerHandValue(PokerHandCategory.FullHouse, new[] { groups[0].Rank, groups[1].Rank });
        }

        if (isFlush)
        {
            return new PokerHandValue(PokerHandCategory.Flush, ranksDescending);
        }

        if (straightHigh is not null)
        {
            return new PokerHandValue(PokerHandCategory.Straight, new[] { straightHigh.Value });
        }

        if (groups[0].Count == 3)
        {
            return new PokerHandValue(
                PokerHandCategory.ThreeOfAKind,
                new[] { groups[0].Rank }.Concat(groups.Skip(1).Select(group => group.Rank)).ToArray());
        }

        if (groups[0].Count == 2 && groups[1].Count == 2)
        {
            var pairRanks = groups.Take(2).Select(group => group.Rank).OrderByDescending(rank => rank).ToArray();
            return new PokerHandValue(PokerHandCategory.TwoPair, pairRanks.Concat(new[] { groups[2].Rank }).ToArray());
        }

        if (groups[0].Count == 2)
        {
            return new PokerHandValue(
                PokerHandCategory.Pair,
                new[] { groups[0].Rank }.Concat(groups.Skip(1).Select(group => group.Rank)).ToArray());
        }

        return new PokerHandValue(PokerHandCategory.HighCard, ranksDescending);
    }

    private static Rank? GetStraightHigh(IEnumerable<Rank> ranks)
    {
        var values = ranks.Select(rank => (int)rank).Distinct().OrderByDescending(value => value).ToArray();
        if (values.Contains((int)Rank.Ace))
        {
            values = values.Append(1).ToArray();
        }

        for (var i = 0; i <= values.Length - 5; i++)
        {
            var window = values.Skip(i).Take(5).ToArray();
            if (window[0] - window[4] == 4)
            {
                return (Rank)(window[0] == 1 ? (int)Rank.Five : window[0]);
            }
        }

        return null;
    }
}

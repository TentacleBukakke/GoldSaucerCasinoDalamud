using GoldSaucerCasino.Core.Cards;

namespace GoldSaucerCasino.Core.Blackjack;

public static class BlackjackRules
{
    public static int CardValue(Card card) => card.Rank switch
    {
        Rank.Ace => 11,
        Rank.King or Rank.Queen or Rank.Jack or Rank.Ten => 10,
        _ => (int)card.Rank,
    };

    public static int BestTotal(IEnumerable<Card> cards)
    {
        var total = 0;
        var aces = 0;
        foreach (var card in cards)
        {
            total += CardValue(card);
            if (card.Rank == Rank.Ace)
            {
                aces++;
            }
        }

        while (total > 21 && aces > 0)
        {
            total -= 10;
            aces--;
        }

        return total;
    }

    public static bool IsBlackjack(IReadOnlyCollection<Card> cards) => cards.Count == 2 && BestTotal(cards) == 21;

    public static bool DealerShouldHit(IEnumerable<Card> cards) => BestTotal(cards) < 17;

    public static bool CanSplit(IReadOnlyList<Card> cards) =>
        cards.Count == 2 && CardValue(cards[0]) == CardValue(cards[1]);
}

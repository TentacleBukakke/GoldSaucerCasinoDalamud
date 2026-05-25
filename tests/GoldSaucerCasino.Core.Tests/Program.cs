using GoldSaucerCasino.Core.Cards;
using GoldSaucerCasino.Core.Blackjack;
using GoldSaucerCasino.Core.Poker;

var tests = new (string Name, Action Body)[]
{
    ("straight flush beats four of a kind", StraightFlushBeatsFourOfAKind),
    ("ace can play low in a wheel straight", AceLowStraight),
    ("pair kicker decides equal pairs", PairKicker),
    ("table reveals community cards by phase", TableRevealsCardsByPhase),
    ("blackjack pays three to two", BlackjackPaysThreeToTwo),
    ("five hits without bust wins automatically", FiveHitsWinsAutomatically),
    ("blackjack table caps at five players", BlackjackTableCapsAtFivePlayers),
    ("bot blackjack starts with no bet", BotBlackjackStartsWithNoBet),
    ("dealer hits below seventeen and stands on seventeen", DealerRuleIsFixed),
};

foreach (var test in tests)
{
    test.Body();
    Console.WriteLine($"pass: {test.Name}");
}

static void StraightFlushBeatsFourOfAKind()
{
    var straightFlush = TexasHoldemEvaluator.EvaluateBest(new[]
    {
        C(Rank.Nine, Suit.Hearts), C(Rank.Ten, Suit.Hearts), C(Rank.Jack, Suit.Hearts),
        C(Rank.Queen, Suit.Hearts), C(Rank.King, Suit.Hearts), C(Rank.Two, Suit.Clubs), C(Rank.Three, Suit.Spades),
    });
    var quads = TexasHoldemEvaluator.EvaluateBest(new[]
    {
        C(Rank.Ace, Suit.Clubs), C(Rank.Ace, Suit.Diamonds), C(Rank.Ace, Suit.Hearts),
        C(Rank.Ace, Suit.Spades), C(Rank.King, Suit.Clubs), C(Rank.Two, Suit.Clubs), C(Rank.Three, Suit.Spades),
    });

    Assert(straightFlush.CompareTo(quads) > 0, "straight flush should beat quads");
}

static void AceLowStraight()
{
    var value = TexasHoldemEvaluator.EvaluateBest(new[]
    {
        C(Rank.Ace, Suit.Clubs), C(Rank.Two, Suit.Hearts), C(Rank.Three, Suit.Diamonds),
        C(Rank.Four, Suit.Spades), C(Rank.Five, Suit.Clubs), C(Rank.King, Suit.Clubs), C(Rank.Nine, Suit.Spades),
    });

    Assert(value.Category == PokerHandCategory.Straight, "hand should be a straight");
    Assert(value.TieBreakers[0] == Rank.Five, "wheel straight should be five-high");
}

static void PairKicker()
{
    var aceKicker = TexasHoldemEvaluator.EvaluateBest(new[]
    {
        C(Rank.Queen, Suit.Clubs), C(Rank.Queen, Suit.Diamonds), C(Rank.Ace, Suit.Hearts),
        C(Rank.Jack, Suit.Spades), C(Rank.Nine, Suit.Clubs),
    });
    var kingKicker = TexasHoldemEvaluator.EvaluateBest(new[]
    {
        C(Rank.Queen, Suit.Hearts), C(Rank.Queen, Suit.Spades), C(Rank.King, Suit.Hearts),
        C(Rank.Jack, Suit.Clubs), C(Rank.Nine, Suit.Diamonds),
    });

    Assert(aceKicker.CompareTo(kingKicker) > 0, "ace kicker should win");
}

static void TableRevealsCardsByPhase()
{
    var table = new PokerTable();
    table.SeatPlayer("You", 1000);
    table.SeatPlayer("Friend", 1000);

    table.StartHand(new Random(7));
    Assert(table.Phase == PokerPhase.PreFlop, "hand should start pre-flop");
    Assert(table.VisibleCommunityCards.Count == 0, "pre-flop should show no community cards");

    table.AdvancePhase();
    Assert(table.Phase == PokerPhase.Flop, "phase should advance to flop");
    Assert(table.VisibleCommunityCards.Count == 3, "flop should show three cards");

    table.AdvancePhase();
    Assert(table.VisibleCommunityCards.Count == 4, "turn should show four cards");

    table.AdvancePhase();
    Assert(table.VisibleCommunityCards.Count == 5, "river should show five cards");
}

static void BlackjackPaysThreeToTwo()
{
    Assert(BlackjackTable.CalculateReturn(10, BlackjackOutcome.Blackjack) == 25, "10 gil blackjack should return 25");
}

static void FiveHitsWinsAutomatically()
{
    var hand = new BlackjackHand(10);
    hand.AddInitialCard(C(Rank.Two, Suit.Clubs));
    hand.AddInitialCard(C(Rank.Two, Suit.Diamonds));

    hand.Hit(C(Rank.Two, Suit.Hearts));
    hand.Hit(C(Rank.Two, Suit.Spades));
    hand.Hit(C(Rank.Three, Suit.Clubs));
    hand.Hit(C(Rank.Three, Suit.Diamonds));
    hand.Hit(C(Rank.Three, Suit.Hearts));

    Assert(hand.Outcome == BlackjackOutcome.FiveHitWin, "five hits without bust should win");
    Assert(hand.Total == 17, "hand should stay below 21");
}

static void BlackjackTableCapsAtFivePlayers()
{
    var table = new BlackjackTable();
    table.SeatPlayer("A");
    table.SeatPlayer("B");
    table.SeatPlayer("C");
    table.SeatPlayer("D");
    table.SeatPlayer("E");

    try
    {
        table.SeatPlayer("F");
        throw new InvalidOperationException("sixth player should not be seated");
    }
    catch (InvalidOperationException)
    {
    }
}

static void BotBlackjackStartsWithNoBet()
{
    var table = new BlackjackTable();
    table.SeatPlayer("You");
    table.StartFreeRound(new Random(2));

    Assert(table.Seats[0].InitialBet == 0, "free round should not set an initial bet");
    Assert(table.Seats[0].Hands[0].Bet == 0, "free round hand should have no bet");
}

static void DealerRuleIsFixed()
{
    Assert(BlackjackRules.DealerShouldHit(new[] { C(Rank.Ten, Suit.Clubs), C(Rank.Six, Suit.Hearts) }), "dealer should hit on 16");
    Assert(!BlackjackRules.DealerShouldHit(new[] { C(Rank.Ten, Suit.Clubs), C(Rank.Seven, Suit.Hearts) }), "dealer should stand on 17");
    Assert(!BlackjackRules.DealerShouldHit(new[] { C(Rank.Ace, Suit.Clubs), C(Rank.Six, Suit.Hearts) }), "dealer should stand on soft 17");
}

static Card C(Rank rank, Suit suit) => new(rank, suit);

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

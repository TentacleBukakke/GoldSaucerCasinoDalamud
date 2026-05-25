using GoldSaucerCasino.Core.Cards;

namespace GoldSaucerCasino.Core.Poker;

public sealed class PokerTable
{
    private readonly List<PokerPlayer> players = [];
    private readonly List<Card> communityCards = [];
    private Deck? deck;

    public IReadOnlyList<PokerPlayer> Players => this.players;

    public IReadOnlyList<Card> CommunityCards => this.communityCards;

    public PokerPhase Phase { get; private set; } = PokerPhase.Waiting;

    public int VisibleCommunityCardCount => this.Phase switch
    {
        PokerPhase.Flop => 3,
        PokerPhase.Turn => 4,
        PokerPhase.River or PokerPhase.Showdown => 5,
        _ => 0,
    };

    public IReadOnlyList<Card> VisibleCommunityCards => this.communityCards
        .Take(this.VisibleCommunityCardCount)
        .ToArray();

    public long Pot { get; private set; }

    public void SeatPlayer(string name, long buyIn)
    {
        if (this.players.Count >= 9)
        {
            throw new InvalidOperationException("The table is full.");
        }

        if (this.players.Any(player => string.Equals(player.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("That player is already seated.");
        }

        this.players.Add(new PokerPlayer(name, buyIn));
    }

    public void StartHand(Random? random = null)
    {
        if (this.players.Count < 2)
        {
            throw new InvalidOperationException("At least two players are required.");
        }

        this.communityCards.Clear();
        this.Pot = 0;
        foreach (var player in this.players)
        {
            player.ClearHand();
        }

        this.deck = Deck.Shuffled(random);
        foreach (var player in this.players)
        {
            player.Receive(this.deck.Draw());
            player.Receive(this.deck.Draw());
        }

        for (var i = 0; i < 5; i++)
        {
            this.communityCards.Add(this.deck.Draw());
        }

        this.Phase = PokerPhase.PreFlop;
    }

    public void AdvancePhase()
    {
        this.Phase = this.Phase switch
        {
            PokerPhase.Waiting => throw new InvalidOperationException("Start a hand before advancing phases."),
            PokerPhase.PreFlop => PokerPhase.Flop,
            PokerPhase.Flop => PokerPhase.Turn,
            PokerPhase.Turn => PokerPhase.River,
            PokerPhase.River => PokerPhase.Showdown,
            PokerPhase.Showdown => PokerPhase.Waiting,
            _ => throw new InvalidOperationException("Unknown poker phase."),
        };
    }

    public void StartDemoHand(Random? random = null)
    {
        this.StartHand(random);
        while (this.Phase != PokerPhase.Showdown)
        {
            this.AdvancePhase();
        }
    }

    public IReadOnlyList<PokerPlayer> GetShowdownWinners()
    {
        if (this.Phase != PokerPhase.Showdown || this.communityCards.Count != 5)
        {
            throw new InvalidOperationException("Showdown requires five community cards.");
        }

        return this.players
            .Select(player => new
            {
                Player = player,
                Value = TexasHoldemEvaluator.EvaluateBest(player.HoleCards.Concat(this.communityCards)),
            })
            .GroupBy(result => result.Value, PokerHandValueComparer.Instance)
            .OrderByDescending(group => group.Key, PokerHandValueComparer.Instance)
            .First()
            .Select(result => result.Player)
            .ToArray();
    }

    private sealed class PokerHandValueComparer : IEqualityComparer<PokerHandValue>, IComparer<PokerHandValue>
    {
        public static readonly PokerHandValueComparer Instance = new();

        public int Compare(PokerHandValue? x, PokerHandValue? y) => x?.CompareTo(y) ?? (y is null ? 0 : -1);

        public bool Equals(PokerHandValue? x, PokerHandValue? y) => this.Compare(x, y) == 0;

        public int GetHashCode(PokerHandValue obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Category);
            foreach (var rank in obj.TieBreakers)
            {
                hash.Add(rank);
            }

            return hash.ToHashCode();
        }
    }
}

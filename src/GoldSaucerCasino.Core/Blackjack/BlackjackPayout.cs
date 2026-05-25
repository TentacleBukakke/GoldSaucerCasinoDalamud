namespace GoldSaucerCasino.Core.Blackjack;

public sealed record BlackjackPayout(string PlayerName, int HandNumber, long Bet, BlackjackOutcome Outcome, long ReturnAmount, long InsuranceReturn);

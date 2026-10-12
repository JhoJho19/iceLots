using System;
using System.Linq;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;

namespace IceP.Application
{
    // Pure snapshot calculations: never retains balance or writes partial economic state.
    public sealed class BalanceService
    {
        private readonly EconomyConfig economy;
        public BalanceService(SlotRuntimeConfig config)
        {
            ConfigValidator.ValidateAndThrow(config);
            economy = config.Economy;
        }
        public bool IsValidBet(long bet) => economy.AvailableBets.Contains(bet);
        public SaveData Credit(SaveData state, long coins)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Balance < 0 || coins < 0 || state.PendingSpin != null)
                throw new ArgumentException("Invalid credit state or amount.");
            var proposed = state.Copy();
            proposed.Balance = checked(state.Balance + coins);
            return proposed;
        }
        public bool CanAfford(SaveData state, long bet) => state != null && state.Balance >= 0 &&
            IsValidBet(bet) && state.Balance >= bet;
        public SaveData Reserve(SaveData state, PendingSpin pending)
        {
            if (state == null || pending == null) throw new ArgumentNullException();
            if (state.Balance < 0 || state.PendingSpin != null || !IsValidBet(pending.EffectiveBet))
                throw new ArgumentException("Invalid reservation state or bet.");
            var proposed = state.Copy();
            if (pending.Type == PendingSpinType.Paid)
            {
                if (state.FreeSpinsRemaining != 0 || state.BonusBet != 0 || !CanAfford(state, pending.EffectiveBet))
                    throw new ArgumentException("Paid bet cannot be reserved.");
                proposed.Balance = checked(state.Balance - pending.EffectiveBet);
            }
            else if (pending.Type == PendingSpinType.Free)
            {
                if (state.FreeSpinsRemaining <= 0 || state.BonusBet != pending.EffectiveBet)
                    throw new ArgumentException("No compatible Free Spin to reserve.");
                proposed.FreeSpinsRemaining = checked(state.FreeSpinsRemaining - 1);
            }
            else throw new ArgumentException("Invalid Spin type.");
            proposed.PendingSpin = pending.Copy();
            return proposed;
        }
        public SaveData Settle(SaveData state, Guid spinId, long payout, int awardedFreeSpins)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (spinId == Guid.Empty || state.PendingSpin?.SpinId != spinId || state.LastSettledSpinId == spinId ||
                state.Balance < 0 || payout < 0 || state.FreeSpinsRemaining < 0 || awardedFreeSpins < 0)
                throw new ArgumentException("Invalid settlement.");
            var proposed = state.Copy();
            proposed.Balance = checked(state.Balance + payout);
            proposed.FreeSpinsRemaining = checked(state.FreeSpinsRemaining + awardedFreeSpins);
            proposed.BonusBet = proposed.FreeSpinsRemaining == 0 ? 0 :
                state.PendingSpin.Type == PendingSpinType.Paid ? state.PendingSpin.EffectiveBet : state.BonusBet;
            proposed.LastSettledSpinId = spinId;
            proposed.LastWin = payout;
            proposed.PendingSpin = null;
            return proposed;
        }
    }
}

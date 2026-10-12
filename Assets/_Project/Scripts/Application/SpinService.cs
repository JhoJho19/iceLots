using System;
using System.Threading;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;

namespace IceP.Application
{
    // Spin transaction owner; GameSession's shared gate serializes all economic writers.
    // Failed writes may have committed: retry the EXACT retained whole snapshot.
    // No recalculation, refunds, reloading older backups or second RNG draw during save retries.
    public sealed class SpinService
    {
        private enum WritePhase { None, Debit, Settlement, Recovery, BetSelection }
        private readonly GameSession session;
        private readonly SlotRuntimeConfig config;
        private readonly SlotEngine engine;
        private readonly BalanceService balance;
        private readonly ISaveRepository repository;
        private readonly SaveDataValidator validator;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly WeeklyJackpotService jackpot;
        private SpinResult active;
        private SaveData proposed;
        private WritePhase phase;

        public SpinService(GameSession session, SlotRuntimeConfig config, SlotEngine engine,
            BalanceService balance, ISaveRepository repository, Func<DateTimeOffset> utcNow,
            WeeklyJackpotService jackpot = null)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.balance = balance ?? throw new ArgumentNullException(nameof(balance));
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            jackpot?.ValidateBinding(session, config);
            this.jackpot = jackpot;
            validator = new SaveDataValidator(config);
            var errors = validator.Validate(session.Snapshot);
            if (errors.Count != 0) throw new ArgumentException(string.Join("\n", errors), nameof(session));
            lock (session.Gate)
            {
                if (session.HasJackpotOwner && jackpot == null)
                    throw new ArgumentException("The session's WeeklyJackpotService must be injected.", nameof(jackpot));
                if (session.HasOwner) throw new InvalidOperationException("GameSession already has a SpinService.");
                session.HasOwner = true;
            }
        }

        public SpinOperationResult StartSpin() => Execute(() =>
        {
            if (!session.CanStartSpin) return Failure(SpinError.Busy, "Session is blocked or a Spin is active.");
            var state = session.Snapshot;
            bool free = state.FreeSpinsRemaining > 0;
            long bet = free ? state.BonusBet : state.SelectedBet;
            if (!balance.IsValidBet(bet)) return Failure(SpinError.InvalidBet, "Bet is not configured.");
            if (!free && !balance.CanAfford(state, bet)) return Failure(SpinError.InsufficientBalance, "Insufficient balance.");
            var now = utcNow().ToUniversalTime();
            var pending = new PendingSpin
            {
                SpinId = Guid.NewGuid(), Type = free ? PendingSpinType.Free : PendingSpinType.Paid,
                EffectiveBet = bet, ConfigVersion = config.ConfigVersion, AcceptedUtc = now,
                JackpotEventId = free ? null : config.Jackpot.EventId,
                JackpotWeekId = free ? (DateTimeOffset?)null : WeeklyJackpotService.WeekAt(now, config.Jackpot)
            };
            proposed = balance.Reserve(state, pending);
            phase = WritePhase.Debit;
            session.Transition(GameSessionState.Preparing);
            return Persist();
        });

        public SpinOperationResult CompleteSpin(Guid spinId) => Execute(() =>
        {
            var state = session.Snapshot;
            if (spinId == Guid.Empty) return Failure(SpinError.WrongSpinId, "SpinId must be non-empty.");
            // A previous completion must never change a newer active transaction or unlock it.
            if (state.LastSettledSpinId == spinId)
                return new SpinOperationResult(alreadySettled: true, settledSpinId: spinId, confirmedPayout: state.LastWin);
            if (state.PendingSpin == null) return Failure(SpinError.NoActiveSpin, "No active Spin.");
            if (state.PendingSpin.SpinId != spinId) return Failure(SpinError.WrongSpinId, "SpinId does not match PendingSpin.");
            if (phase == WritePhase.Settlement && session.State == GameSessionState.Error)
            {
                session.Transition(GameSessionState.Settling);
                return Persist();
            }
            if (session.State != GameSessionState.Spinning || active == null)
                return Failure(SpinError.InvalidState, "Spin has no completable runtime outcome.");
            session.Transition(GameSessionState.Settling);
            proposed = balance.Settle(state, spinId, active.PotentialPayout, active.Evaluation.Scatter.AwardedFreeSpins);
            if (active.Jackpot != null) jackpot.ApplyWin(proposed, active.Jackpot);
            phase = WritePhase.Settlement;
            return Persist();
        });

        // Startup or explicit discard following outcome-generation failure. An active successful
        // Spin cannot be discarded via this API. A pending failed save must be retried first.
        public SpinOperationResult RecoverInterruptedSpin() => Execute(() =>
        {
            if (phase == WritePhase.Recovery) return RetryCore();
            if (phase != WritePhase.None || active != null || session.State == GameSessionState.Spinning)
                return Failure(SpinError.Busy, "An active command must be completed or retried.");
            var state = session.Snapshot;
            if (state.PendingSpin == null)
                return session.State == GameSessionState.Idle ? new SpinOperationResult() :
                    Failure(SpinError.InvalidState, "No interrupted transaction to recover.");
            proposed = state.Copy();
            proposed.PendingSpin = null;
            if (proposed.FreeSpinsRemaining == 0) proposed.BonusBet = 0;
            phase = WritePhase.Recovery;
            session.Transition(GameSessionState.Preparing);
            return Persist();
        });

        public SpinOperationResult SelectBet(long bet) => Execute(() =>
        {
            if (!session.CanSelectBet) return Failure(SpinError.Busy, "Bet selection is blocked.");
            if (!balance.IsValidBet(bet)) return Failure(SpinError.InvalidBet, "Bet is not configured.");
            proposed = session.Snapshot;
            proposed.SelectedBet = bet; // BonusBet belongs to the bonus chain, independent of selected paid bet.
            phase = WritePhase.BetSelection;
            session.Transition(GameSessionState.Preparing);
            return Persist();
        });

        public SpinOperationResult RetryPersistence() => Execute(RetryCore);
        private SpinOperationResult RetryCore()
        {
            if (session.State != GameSessionState.Error || proposed == null || phase == WritePhase.None)
                return Failure(SpinError.InvalidState, "No failed snapshot to retry.");
            session.Transition(phase == WritePhase.Settlement ? GameSessionState.Settling : GameSessionState.Preparing);
            return Persist();
        }

        private SpinOperationResult Persist()
        {
            var errors = validator.Validate(proposed);
            if (errors.Count != 0) return Block(SpinError.InvalidState, string.Join("\n", errors));
            SaveWriteResult save;
            try { save = repository.Save(proposed.Copy()); }
            catch (Exception e) { return Block(SpinError.PersistenceFailure, e.ToString()); }
            if (save == null || !save.Success)
                return Block(SpinError.PersistenceFailure, save?.Diagnostic ?? "Repository returned no write confirmation.");
            var completedPhase = phase;
            session.Commit(proposed);
            proposed = null;
            phase = WritePhase.None;
            if (completedPhase == WritePhase.Debit)
            {
                var pending = session.Snapshot.PendingSpin;
                try
                {
                    var context = pending.Type == PendingSpinType.Free ? SpinEvaluationContext.Free(pending.EffectiveBet) :
                        SpinEvaluationContext.Paid(pending.EffectiveBet);
                    var evaluation = engine.Generate(context);
                    var jackpotResult = jackpot?.Draw(pending, session.Snapshot);
                    active = new SpinResult(pending, config, evaluation, jackpotResult);
                    session.Transition(GameSessionState.Spinning);
                    return new SpinOperationResult(spin: active);
                }
                catch (OverflowException e) { return Block(SpinError.ArithmeticOverflow, e.Message); }
                catch (Exception e) { return Block(SpinError.OutcomeGenerationFailure, e.ToString()); }
            }
            var settled = completedPhase == WritePhase.Settlement ? active : null;
            active = null;
            session.Transition(GameSessionState.Idle);
            return new SpinOperationResult(spin: settled, settledSpinId: settled?.SpinId,
                confirmedPayout: settled?.PotentialPayout ?? 0);
        }

        private SpinOperationResult Execute(Func<SpinOperationResult> command)
        {
            // Reject concurrent calls rather than enqueue them into a second economic operation.
            if (!Monitor.TryEnter(session.Gate)) return Failure(SpinError.Busy, "Another command is running.");
            try
            {
                if (session.CommandExecuting) return Failure(SpinError.Busy, "Reentrant command rejected.");
                session.CommandExecuting = true;
                try { return command(); }
                catch (OverflowException e) { return Block(SpinError.ArithmeticOverflow, e.Message); }
                catch (Exception e) { return Block(SpinError.InvalidState, e.ToString()); }
                finally { session.CommandExecuting = false; }
            }
            finally { Monitor.Exit(session.Gate); }
        }
        private SpinOperationResult Block(SpinError error, string diagnostic)
        {
            if (session.State != GameSessionState.Error) session.Transition(GameSessionState.Error);
            return Failure(error, diagnostic);
        }
        private static SpinOperationResult Failure(SpinError error, string diagnostic) => new SpinOperationResult(error, diagnostic);
    }
}

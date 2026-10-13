// Standalone QA process: references the original project's compiled production assemblies.
// This file is outside Assets and is never included in a Unity player. No animation/UI runs here.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using IceP.Application;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;
using IceP.Infrastructure.Persistence;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class ProcessLifecycleProbe
{
    private sealed class ImmutableValues : JsonConverter
    {
        public override bool CanConvert(Type type) => type == typeof(SymbolDefinition) || type == typeof(SymbolPayout) || type == typeof(ScatterAward);
        public override bool CanWrite => false;
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotSupportedException();
        public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
        {
            var value = JObject.Load(reader);
            if (type == typeof(SymbolDefinition)) return new SymbolDefinition((SymbolId)(int)value["Id"], (int)value["RequiredCountPerReel"]);
            if (type == typeof(ScatterAward)) return new ScatterAward((int)value["ScatterCount"], (int)value["FreeSpins"]);
            return new SymbolPayout((SymbolId)(int)value["Symbol"], (long)value["Match3"], (long)value["Match4"], (long)value["Match5"]);
        }
    }
    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    }
    private sealed class Bits : IRandomSource { public int NextInt(int min, int max) => min; }
    private sealed class Stops : IRandomSource
    {
        private readonly int[] stops; private int calls;
        public Stops(SlotRuntimeConfig config)
        {
            stops = config.ReelStrips.Select(r => Enumerable.Range(0, r.Symbols.Count).First(i => r.Symbols[i] == config.ScatterId)).ToArray();
        }
        public int NextInt(int min, int max) => stops[calls++ % 5];
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static int Main(string[] args)
    {
        try
        {
            var configJson = File.ReadAllText(args[0]);
            var config = JsonConvert.DeserializeObject<SlotRuntimeConfig>(configJson, new ImmutableValues());
            Require(ConfigValidator.Validate(config).Count == 0, "Exported production configuration is invalid.");
            // Includes every strip, paytable entry and immutable rule; no substituted mathematics.
            Require(JToken.DeepEquals(JToken.Parse(configJson), JToken.Parse(JsonConvert.SerializeObject(config))), "Configuration export did not round-trip exactly.");
            string directory = Path.GetFullPath(args[1]), mode = args[2];
            bool verify = args.Length > 3 && args[3] == "verify";
            Directory.CreateDirectory(directory);
            var repository = new JsonSaveRepository(directory, config);
            var load = repository.Load();
            if (!verify)
            {
                Require(load.Status == SaveLoadStatus.Missing, "Probe requires a new isolated save directory.");
                var initial = InitialProfileFactory.Create(config); initial.SelectedBet = 1000;
                if (mode == "free") { initial.FreeSpinsRemaining = 3; initial.BonusBet = 800; }
                Require(repository.Save(initial).Success, "Initial save failed."); load = repository.Load();
            }
            Require(load.Data != null, "No valid committed snapshot.");
            var session = new GameSession(load.Data); var clock = new Clock();
            var jackpot = new WeeklyJackpotService(session, config, clock, new Bits());
            var spins = new SpinService(session, config, new SlotEngine(config, new Stops(config)),
                new BalanceService(config), repository, () => clock.UtcNow, jackpot);
            string ready = Path.Combine(directory, "ready.json");
            if (!verify)
            {
                SpinResult spin = null;
                if (mode != "before")
                {
                    var accepted = spins.StartSpin(); Require(accepted.Success, "Acceptance failed."); spin = accepted.Spin;
                    Require(spin.Evaluation.Scatter.AwardedFreeSpins == 20, "Expected deterministic five-Scatter result.");
                    if (mode != "free") Require(spin.Jackpot.JackpotWon, "Expected deterministic potential jackpot.");
                    if (mode == "settled") Require(spins.CompleteSpin(spin.SpinId).Success, "Settlement failed.");
                }
                File.WriteAllText(ready, JsonConvert.SerializeObject(new { Mode = mode, Snapshot = session.Snapshot,
                    PotentialPayout = spin == null ? 0 : spin.PotentialPayout, SpinId = spin == null ? Guid.Empty : spin.SpinId }));
                Console.WriteLine("READY " + mode); Console.Out.Flush();
                // The supervising PowerShell process now force-kills this exact PID without quit callbacks.
                Thread.Sleep(Timeout.Infinite); return 2;
            }
            var expected = JObject.Parse(File.ReadAllText(ready));
            long payout = (long)expected["PotentialPayout"];
            Require(spins.RecoverInterruptedSpin().Success, "Recovery failed.");
            var state = session.Snapshot;
            long balance = mode == "paid" ? 19000 : mode == "settled" ? 19000 + payout : 20000;
            Require(state.Balance == balance, "Debit/payout changed across process death.");
            Require(state.PendingSpin == null && repository.Load().Data.PendingSpin == null, "PendingSpin not cleared durably.");
            Require(state.LastWin == (mode == "settled" ? payout : 0), "Unconfirmed payout was awarded or confirmed payout was lost.");
            Require(state.FreeSpinsRemaining == (mode == "free" ? 2 : mode == "settled" ? 20 : 0), "Reserved Free Spin/retrigger policy broken.");
            Require(state.BonusBet == (mode == "free" ? 800 : mode == "settled" ? 1000 : 0), "BonusBet was lost or fabricated.");
            Require(state.JackpotWinsThisWeek == (mode == "settled" ? 1 : 0), "Potential jackpot consumed the allowance.");
            if (mode == "settled")
            {
                var id = Guid.Parse((string)expected["SpinId"]);
                Require(spins.CompleteSpin(id).AlreadySettled, "Duplicate settlement not recognized.");
                Require(session.Snapshot.Balance == balance, "Duplicate payout after restart.");
                Require(!jackpot.GetStatus().IsEligible, "Weekly cap lost.");
            }
            Require(new SaveDataValidator(config).Validate(repository.Load().Data).Count == 0, "Recovered save is invalid.");
            File.WriteAllText(Path.Combine(directory, "verified.json"), JsonConvert.SerializeObject(state, Formatting.Indented));
            Console.WriteLine("PASS " + mode + " balance=" + balance + " free=" + state.FreeSpinsRemaining + " jackpot=" + state.JackpotWinsThisWeek);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

# Section 4.4 — pure slot mathematics

Implemented only Plan.md section 4.4 in `C:\UnityGames\IceP`. The specification and plan were read before implementation. No gameplay transactions, session lifecycle, persistence, jackpot draw, UI integration, or animation work was added. Section 4.5 is not started.

## Structure and compatibility

The actual assembly location is `Assets/_Project/Scripts/Domain/IceP.Domain.asmdef`. There is one Domain assembly and one definition of each existing model. The old paths shown as deleted in Git are existing user changes from moving scripts into `Scripts`; they were preserved. No folders, assembly definitions, existing code, scene, configuration assets, or existing tests were reorganized or changed.

Reviewed SlotRuntimeConfig, Payline, Paytable, ScatterRules, JackpotRulesConfig, SlotGrid, SpinOutcome, OutcomeGenerator, IRandomSource, and ConfigValidator. Their existing immutable models already express the required rules. No corrections to sections 4.2 or 4.3 were necessary.

`SpinOutcome` and `OutcomeGenerator.Generate()` retain their existing definitions and APIs. `EvaluatedSpinOutcome` composes the original outcome with immutable line and Scatter evaluations, the spin context, applied multiplier, and final payout. Keeping generation and evaluation separate also permits deterministic evaluation of manually supplied outcomes without consuming RNG.

## Created files

All paths below are relative to the Unity project. Each new source/report also has a new `.meta` file with a unique GUID.

- `Assets/_Project/Scripts/Domain/SlotEngine/WinEvaluator.cs`
- `Assets/_Project/Scripts/Domain/SlotEngine/WinEvaluation.cs`
- `Assets/_Project/Scripts/Domain/SlotEngine/ScatterEvaluator.cs`
- `Assets/_Project/Scripts/Domain/SlotEngine/SpinEvaluationContext.cs`
- `Assets/_Project/Scripts/Domain/SlotEngine/EvaluatedSpinOutcome.cs`
- `Assets/_Project/Scripts/Domain/SlotEngine/SlotEngine.cs`
- `Assets/_Project/Scripts/Domain/SlotEngine/JackpotRules.cs`
- `Assets/_Project/Scripts/Tests/EditMode/SlotEngineTests.cs`
- `Assets/_Project/Scripts/Tests/EditMode/SlotMathematicsTests.cs`
- `Assets/Docs/Section44Report.md`

Modified existing files: none.

Validation XML, editor logs, and source-hash verification are supplied separately in `Section44Validation` beside this report.

## Line evaluation

`WinEvaluator(SlotRuntimeConfig)` validates and retains an immutable configuration snapshot. `Evaluate(grid, totalBet)` requires a positive configured bet divisible by the active line count. All 20 configured paylines are evaluated independently from reel 0. SCATTER cannot start or extend a normal combination. Fewer than three matching symbols pays nothing. Only the longest matching prefix is considered; a zero configured reward for that prefix does not fall back to shorter-prefix rewards.

`lineBet = totalBet / config.Paylines.Count`; `lineWin = lineBet * configuredMultiplier`; the result sums all line wins. Coin values and multipliers use `long`, with checked payout multiplication, total addition, and final Free Spins multiplication. ConfigValidator also rejects unsafe worst-case payout configurations before use.

`LineWin` contains a zero-based payline index, symbol, match count, configured multiplier, payout, and a copied read-only list of `(ReelIndex, RowIndex)` coordinates. `WinEvaluation` contains a copied read-only list of winning lines and `BaseWin`, the monetary payout before any Free Spins multiplier. There are no UI references.

## Scatter and Free Spins

`ScatterEvaluator` counts all 15 cells independently of paylines and uses exact configured count-to-reward entries. The default rewards remain 3→8, 4→12, 5→20. Counts 0–2 give no spins. Scatter awards never add coin payouts.

The immutable result reports ScatterCount, AwardedFreeSpins, and BonusTriggered (`AwardedFreeSpins > 0`). Evaluation during a Free Spin respects the configured RetriggerEnabled switch. The default enables retriggers. It calculates newly awarded spins only; no balance, queue, remaining counter, or session is stored.

`SpinEvaluationContext.Paid(totalBet)` and `.Free(bonusBet)` explicitly identify the mode and effective bet. For `.Free`, Application must supply the original triggering BonusBet. The same reels, lines, and paytable are used in both modes. Paid payouts use ×1; Free Spins use configured Scatter.FreeSpinsMultiplier (default ×1). The engine validates this request before generating stops. There is no bet deduction.

## Engine API and dependency injection

```csharp
var engine = new IceP.Domain.SlotEngine.SlotEngine(config, randomSource);
var paid = engine.Generate(SpinEvaluationContext.Paid(1000));
var free = engine.Generate(SpinEvaluationContext.Free(bonusBet));
var evaluated = engine.Evaluate(predefinedOutcome, SpinEvaluationContext.Paid(1000));
```

The configuration and RNG are constructor-injected. The engine creates its generator and evaluators from that same configuration snapshot. Generate chooses five stops, then Evaluate coordinates WinEvaluator and ScatterEvaluator. Evaluate consumes no RNG. Results retain the original immutable outcome, including its grid and stops. No static mutable state, singleton, service locator, Unity object, Application service, or Infrastructure dependency is introduced into Domain.

## Jackpot probability

`JackpotRules(JackpotRulesConfig).CalculateProbability(context, weeklyEligible, alreadyWonThisWeek, acceptedAtUtc)` is a pure calculation. It returns zero for disabled events, Free Spins, explicitly ineligible participation, or an explicitly exhausted weekly limit. If an event time is supplied, configured start is inclusive and end is exclusive. It reads no clock and manages no week transitions.

Otherwise `P = min(1, BaseProbability * EvaluationBet / ReferenceBet)`. ReferenceBet must be positive and BaseProbability finite and within [0,1]. Default paid probabilities for bets 200/400/600/800/1000 are 0.00002/0.00004/0.00006/0.00008/0.00010. This component does not draw RNG, award coins, or change reels/paylines; it remains separate from engine payout orchestration.

## Concrete predefined example

At total bet 1000, manually evaluate the following grid (columns are reels 0–4):

| Row | Reel 0 | Reel 1 | Reel 2 | Reel 3 | Reel 4 |
|---|---|---|---|---|---|
| Top, 0 | SCATTER | S2 | S1 | S2 | SCATTER |
| Center, 1 | S7 | S7 | S7 | S7 | S7 |
| Bottom, 2 | S1 | SCATTER | S1 | S2 | S1 |

Only configured line 01 wins (zero-based PaylineIndex=0, rows 1-1-1-1-1). It pays S7×5: `(1000 / 20) * 1700 = 85000` coins. Coordinates are (0,1), (1,1), (2,1), (3,1), (4,1). Three Scatter award eight Free Spins independently. BaseWin and final paid payout are 85000; final Free Spins payout is also 85000 at the default ×1. With a configured ×3 multiplier, the same Free Spin using BonusBet=1000 returns 255000 coins and eight newly awarded spins. The example is pinned by a deterministic test. Stops in a manually constructed SpinOutcome are supplied metadata; Evaluate does not regenerate its grid.

## Analytical verification

`SlotMathematicsTests` derives ordinary-symbol marginal probabilities by enumerating each configured reel stop at each configured line row. Across independent reels it derives exactly-three, exactly-four, and five matching-prefix probabilities. For probabilities p0…p4, these are `p0*p1*p2*(1-p3)`, `p0*p1*p2*p3*(1-p4)`, and `p0*p1*p2*p3*p4`. It multiplies those by the configured paytable and sums line expectations, divided by the active line count.

For Scatter, the test enumerates the complete three-row window of every stop on every configured strip, then convolves the five independent per-reel count distributions. It does not treat the 15 cells as independent. One Scatter per 30-position strip gives a visible Scatter probability of 3/30 per reel. Rewards come from the configuration. Expected total bonus spins include retriggers via `initialSpins / (1 - retriggerSpins)`. Combined line RTP is `baseRtp * (1 + totalBonusSpins * freeMultiplier)`.

The independently calculated default values are:

| Metric | Calculated value |
|---|---:|
| Base line RTP | 85.605855967% |
| Probability of 3+ Scatter | 0.00856 (0.856%) |
| Expected newly awarded Free Spins per paid spin | 0.0704 |
| Expected total bonus spins including retriggers | 0.0757314974182444 |
| Line RTP including Free Spins and retriggers | 92.088915627% |

These match the rounded TZ.md targets within their documented precision. The test's actual values are derived from the assets, independently of its expected constants.

The jackpot contribution is calculated separately as `P * JackpotAmount / totalBet`: 1% on eligible paid spins for the default parameters. The weekly limit and actual participation determine the long-term contribution; it is not a guaranteed additional 1% RTP. No Monte Carlo simulation is added.

## Validation and limitations

Validation is performed with Unity 6000.3.10f1 on Windows in an isolated copy of Assets, Packages, and ProjectSettings at `C:\Users\mskos\Documents\Codex\I44`. The original editor is open on IceP. Resolved packages are copied from the original Library/PackageCache to avoid waiting on Git downloads; manifests and package versions are preserved. No claim is made that the original editor's live Console or UI was inspected.

The first copy used the longer generated Codex workspace path. Its tests passed, but the initial package import produced DirectoryNotFoundException errors for package UXML files exceeding the Windows path limit. The copy was moved to the shorter path above, and both test suites were rerun. The final EditMode log contains no such package import errors and no C# errors or warnings. Initial logs are retained as evidence of that validation-environment issue. The existing `Assets/Video/IceVideo.mp4` also emitted an H.264 timestamp correction warning on first import. Licensing logs report an unavailable access token while permitting execution under the existing license. These are disclosed rather than represented as an entirely empty editor log.

The complete existing EditMode suite, new engine/analytical tests, and existing PlayMode scene smoke test are run. The smoke test covers startup, runtime configuration, Settings/Daily Bonus, repeated Play/Back in the same SC scene, and reload. Assembly references and DTO immutability are tested; input grids, stops, and strips are checked for mutation. Configuration changes, supported bets, zero rewards, invalid inputs, arithmetic bounds, and jackpot exclusions are covered.

Final runs on 12 October 2026 both exited with code 0:

| Test suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Existing ConfigurationTests | 82 | 0 | 0 |
| Existing OutcomeGeneratorTests | 24 | 0 | 0 |
| New SlotEngineTests | 69 | 0 | 0 |
| New SlotMathematicsTests | 1 | 0 | 0 |
| All EditMode | 176 | 0 | 0 |
| Existing PlayMode GameSceneSmokeTests | 1 | 0 | 0 |

`FinalEditMode.xml` and `FinalPlayMode.xml` are the final results. Both final logs have no C# errors/warnings or unexpected gameplay errors/warnings. The existing PlayMode test's LogAssert checks passed. Main Menu navigation and configuration asset validity are verified automatically, without manual inspection of the original editor. During validation Unity rewrote line endings in the copy's ProjectSettings.asset; its normalized setting content matches the source. This copy-only rewrite is not transferred to IceP.

Application remains responsible for retaining BonusBet and the appropriate config snapshot across a bonus chain, weekly/event eligibility and time, balances, transactions, RNG drawing for jackpot, and bonus session lifecycle. Generation callers must serialize access to the injected RNG. A manual SlotGrid may contain more than five Scatter; the evaluator counts them but returns no award for counts with no configured entry. Such counts cannot be generated from the validated v1 strips. The theoretical bonus expectation assumes the documented independent uniform reel stops and completed spins; interrupted-spin policy and weekly jackpot limits belong to later stages.

using IceP.Application;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;
using IceP.Infrastructure.Configuration;
using IceP.Infrastructure.Randomness;
using IceP.Infrastructure.Persistence;
using IceP.Infrastructure;
using IceP.Presentation;
using UnityEngine;

namespace IceP.Bootstrap
{
    public sealed class SlotBootstrapper : MonoBehaviour
    {
        [SerializeField] private GameNavigationView navigationView;
        [SerializeField] private SlotGameConfigSO gameConfig;
        [SerializeField] private SlotVisualConfigSO visualConfig;
        private GameNavigationPresenter presenter;
        private MainMenuPresenter menuPresenter;
        public SettingsService Settings { get; private set; }

        public SlotRuntimeConfig RuntimeConfig { get; private set; }
        public OutcomeGenerator OutcomeGenerator { get; private set; }
        public ISaveRepository SaveRepository { get; private set; }
        public GameSession Session { get; private set; }
        public SpinService Spins { get; private set; }
        public DailyBonusService DailyBonus { get; private set; }
        public WeeklyJackpotService WeeklyJackpot { get; private set; }
        public IClock Clock { get; private set; }
        public GameNavigationService Navigation { get; private set; }
        public SaveData LoadedProfile => Session?.Snapshot;
        public string InitializationError { get; private set; }
        public bool IsInitialized => presenter != null && RuntimeConfig != null && Session != null;

        private void Awake()
        {
            if (!navigationView || !navigationView.HasRequiredReferences)
            {
                InitializationError = "Missing required UI references.";
                Debug.LogError("SlotBootstrapper requires all navigation view references.", this);
                enabled = false;
                return;
            }

            try
            {
                ISlotConfigProvider configProvider = new LocalSlotConfigProvider(gameConfig);
                var config = configProvider.Load();
                VisualConfigValidator.ValidateAndThrow(visualConfig, config);
                SaveRepository = new JsonSaveRepository(GetStorageDirectory(), config);
                var load = SaveRepository.Load();
                if (load.Status == SaveLoadStatus.Failed)
                {
                    FailInitialization(load.Diagnostic);
                    return;
                }
                var profile = load.Data;
                if (load.Status == SaveLoadStatus.Missing)
                {
                    profile = InitialProfileFactory.Create(config);
                    var save = SaveRepository.Save(profile);
                    if (!save.Success) { FailInitialization(save.Diagnostic); return; }
                }
                if (load.Status == SaveLoadStatus.RecoveredBackup) Debug.LogWarning(load.Diagnostic, this);
                var errors = new SaveDataValidator(config).Validate(profile);
                if (errors.Count != 0) { FailInitialization(string.Join("\n", errors)); return; }
                RuntimeConfig = config;
                var random = RandomSourceFactory.Create(config.DebugRandom);
                OutcomeGenerator = new OutcomeGenerator(config, random);
                Session = new GameSession(profile);
                Clock = new SystemClock();
                var balance = new BalanceService(config);
                WeeklyJackpot = new WeeklyJackpotService(Session, config, Clock,
                    RandomSourceFactory.CreateJackpot(config.DebugRandom));
                Spins = new SpinService(Session, config, new SlotEngine(config, random),
                    balance, SaveRepository, () => Clock.UtcNow, WeeklyJackpot);
                DailyBonus = new DailyBonusService(Session, config, balance, SaveRepository, Clock);
                Settings = new SettingsService(Session, config, SaveRepository);
                var recovery = Spins.RecoverInterruptedSpin();
                if (!recovery.Success) { FailInitialization(recovery.Diagnostic); return; }
                ConnectNavigation();
                Debug.Log($"SlotBootstrapper initialized with configuration {config.ConfigVersion}.", this);
            }
            catch (ConfigValidationException exception)
            {
                FailInitialization(exception.Message);
            }
            catch (System.Exception exception) { FailInitialization(exception.ToString()); }
        }

        private void ConnectNavigation()
        {
            InitializationError = null;
            enabled = true;
            navigationView.PlayButton.interactable = true;
            navigationView.SettingsButton.interactable = true;
            navigationView.DailyBonusButton.interactable = true;
            Navigation = new GameNavigationService(Session);
            presenter = new GameNavigationPresenter(Navigation, navigationView);
            menuPresenter = new MainMenuPresenter(Session, Navigation, navigationView, DailyBonus, Settings);
            presenter.RefreshAvailability();
        }

        // Only available when services exist and startup PendingSpin recovery failed.
        public bool RetryInitializationRecovery()
        {
            if (presenter != null || Spins == null) return false;
            var retry = Spins.RecoverInterruptedSpin();
            if (!retry.Success) { InitializationError = retry.Diagnostic; return false; }
            ConnectNavigation();
            return true;
        }

        private void Update()
        {
            presenter?.RefreshAvailability();
            menuPresenter?.Tick(Time.unscaledDeltaTime);
        }
        private void OnApplicationPause(bool paused)
        { if (paused) menuPresenter?.FlushSettings(); else menuPresenter?.Refresh(); }
        private void OnApplicationQuit() => menuPresenter?.FlushSettings();
        private void OnApplicationFocus(bool focused) { if (focused) menuPresenter?.Refresh(); }

        private void FailInitialization(string diagnostic)
        {
            InitializationError = diagnostic;
            navigationView.ShowScreen(false);
            navigationView.PlayButton.interactable = false;
            navigationView.SettingsButton.interactable = false;
            navigationView.DailyBonusButton.interactable = false;
            navigationView.ShowMenuMessage("Unable to initialize. Controls unavailable.");
            Debug.LogError("SlotBootstrapper initialization failed: " + diagnostic, this);
            enabled = false;
        }

        private static string GetStorageDirectory()
        {
#if UNITY_EDITOR
            // Editor/CI only. Test runners must isolate scene bootstrap from real player saves.
            var testDirectory = System.Environment.GetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(testDirectory)) return testDirectory;
#endif
            return UnityEngine.Application.persistentDataPath;
        }

        private void OnDestroy() { menuPresenter?.Dispose(); presenter?.Dispose(); }
    }
}

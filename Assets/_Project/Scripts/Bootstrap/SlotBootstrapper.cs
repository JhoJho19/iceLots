using IceP.Application;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;
using IceP.Infrastructure.Configuration;
using IceP.Infrastructure.Randomness;
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

        public SlotRuntimeConfig RuntimeConfig { get; private set; }
        public OutcomeGenerator OutcomeGenerator { get; private set; }
        public bool IsInitialized => presenter != null && RuntimeConfig != null;

        private void Awake()
        {
            if (!navigationView || !navigationView.HasRequiredReferences)
            {
                Debug.LogError("SlotBootstrapper requires all navigation view references.", this);
                enabled = false;
                return;
            }

            try
            {
                ISlotConfigProvider configProvider = new LocalSlotConfigProvider(gameConfig);
                var config = configProvider.Load();
                VisualConfigValidator.ValidateAndThrow(visualConfig, config);
                OutcomeGenerator = new OutcomeGenerator(config, RandomSourceFactory.Create(config.DebugRandom));
                RuntimeConfig = config;
                presenter = new GameNavigationPresenter(new GameNavigationService(), navigationView);
                Debug.Log($"SlotBootstrapper initialized with configuration {config.ConfigVersion}.", this);
            }
            catch (ConfigValidationException exception)
            {
                navigationView.PlayButton.interactable = false;
                Debug.LogError(exception.Message, this);
                enabled = false;
            }
        }

        private void OnDestroy() => presenter?.Dispose();
    }
}

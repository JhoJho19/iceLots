using System;
using System.Collections.Generic;
using IceP.Application;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace IceP.Presentation
{
    // Scene-owned audio. Only presentation events can start celebrations; rendering cannot.
    public sealed class GameAudioController : MonoBehaviour
    {
        private AudioConfigSO config;
        private AudioSource music, reels, ui, stops, rewards, jackpotAudio;
        private JackpotView jackpotView;
        private SlotPresenter slot;
        private MainMenuPresenter menu;
        private ReelAnimationCoordinator animation;
        private GameNavigationService navigation;
        private SpinResult active;
        private Guid lastCelebrated;
        private float soundVolume = 1;
        private bool focused = true, paused;
        private readonly List<(Button button, UnityAction action)> buttons = new List<(Button, UnityAction)>();

        public void Initialize(AudioConfigSO audioConfig, UserSettings settings, SlotPresenter slotPresenter,
            MainMenuPresenter menuPresenter, GameNavigationService nav, GameNavigationView view, SlotView slotView,
            JackpotView jackpot = null)
        {
            config = audioConfig;
            slot = slotPresenter; menu = menuPresenter; navigation = nav;
            animation = slotView.AnimationCoordinator;
            music = Source("Music", true); reels = Source("Reels", true);
            ui = Source("UI", false); stops = Source("Reel stops", false); rewards = Source("Rewards", false);
            jackpotAudio = Source("Jackpot", false);
            jackpotAudio.clip = config.Jackpot;
            jackpotView = jackpot;
            if (jackpotView)
            {
                jackpotView.CelebrationStarted += StartJackpot;
                jackpotView.CelebrationEnded += StopJackpot;
            }
            music.clip = config.Music; reels.clip = config.ReelLoop;
            ApplySettings(settings);
            animation.Started += SpinStarted; animation.ReelStopped += ReelStopped; animation.Finished += SpinFinished;
            slot.SettlementConfirmed += Settled;
            menu.RewardConfirmed += DailyReward; menu.AudioSettingsChanged += ApplySettings;
            navigation.ScreenChanged += ScreenChanged;
            Bind(view.PlayButton, config.UiOpen); Bind(slotView.BackButton, config.UiOpen);
            Bind(view.SettingsButton, config.UiOpen); Bind(view.DailyBonusButton, config.UiOpen);
            Bind(view.CloseSettingsButton, config.UiClick); Bind(view.CloseDailyBonusButton, config.UiClick);
            Bind(slotView.SpinButton, config.UiClick); Bind(slotView.AutoButton, config.UiClick);
            Bind(slotView.BetDownButton, config.UiClick); Bind(slotView.BetUpButton, config.UiClick);
            Bind(slotView.RetryButton, config.UiClick); Bind(view.DailyBonusView.ClaimButton, config.UiClick);
            Bind(view.DailyBonusView.RetryButton, config.UiClick);
            if (music.clip) music.Play();
        }

        private AudioSource Source(string label, bool loop)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = loop; source.spatialBlend = 0;
            return source;
        }

        private void Bind(Button button, AudioClip clip)
        {
            UnityAction action = () => Play(ui, clip);
            button.onClick.AddListener(action); buttons.Add((button, action));
        }

        public void ApplySettings(UserSettings settings)
        {
            soundVolume = settings.SoundEnabled ? Mathf.Clamp01((float)settings.SoundVolume) : 0;
            music.volume = config.MusicGain * (settings.MusicEnabled ? Mathf.Clamp01((float)settings.MusicVolume) : 0);
            ui.volume = stops.volume = rewards.volume = jackpotAudio.volume = config.EffectsGain * soundVolume;
            reels.volume = config.ReelGain * soundVolume;
        }

        private void Play(AudioSource source, AudioClip clip)
        {
            if (clip && focused && !paused && soundVolume > 0) source.PlayOneShot(clip);
        }

        private void SpinStarted(SpinResult spin)
        {
            active = spin;
            // Autoplay must not stack long fanfares from earlier spins.
            rewards.Stop(); stops.Stop();
            Play(stops, config.SpinStart);
            if (reels.clip && focused && !paused) reels.Play();
        }

        private void ReelStopped(Guid id, int reel)
        {
            if (active == null || active.SpinId != id) return;
            stops.pitch = 1 + reel * .035f;
            Play(stops, config.UiClick);
            for (int row = 0; row < 3; row++)
                if (active.Evaluation.Outcome.Grid[reel, row] == SymbolId.SCATTER)
                { Play(stops, config.Scatter); break; }
        }

        private void SpinFinished(Guid id, bool success)
        {
            if (active == null || active.SpinId != id) return;
            reels.Stop(); stops.pitch = 1; active = null;
            if (!success) { stops.Stop(); rewards.Stop(); }
        }

        private void Settled(SpinResult spin, SpinOperationResult result)
        {
            if (!result.Success || result.SettledSpinId != spin.SpinId || lastCelebrated == spin.SpinId) return;
            lastCelebrated = spin.SpinId;
            // Jackpot audio follows the View's animation lifecycle, including cancellation.
            if (spin.Jackpot != null && spin.Jackpot.JackpotWon) return;
            if (jackpotView && jackpotView.IsCelebrating) return;
            var clip = SelectCelebration(config, spin);
            if (clip) { rewards.Stop(); Play(rewards, clip); }
        }

        public static AudioClip SelectCelebration(AudioConfigSO audioConfig, SpinResult spin)
        {
            if (spin.Jackpot != null && spin.Jackpot.JackpotWon) return audioConfig.Jackpot;
            if (spin.Evaluation.Scatter.AwardedFreeSpins > 0) return audioConfig.Scatter;
            // Jackpot is handled separately; normal win tiers compare the payout to the actual spin bet.
            double ratio = spin.Evaluation.FinalPayout / (double)Math.Max(1, spin.EffectiveBet);
            if (ratio >= audioConfig.BigWinMultiplier) return audioConfig.BigWin;
            if (ratio >= audioConfig.MediumWinMultiplier) return audioConfig.MediumWin;
            return ratio > 0 ? audioConfig.SmallWin : null;
        }

        private void DailyReward(DailyBonusClaimResult result)
        { rewards.Stop(); Play(rewards, config.MediumWin); }

        private void StartJackpot()
        {
            rewards.Stop(); jackpotAudio.Stop();
            if (jackpotAudio.clip && focused && !paused) jackpotAudio.Play();
        }
        private void StopJackpot() { if (jackpotAudio) jackpotAudio.Stop(); }

        private void ScreenChanged(GameScreen screen)
        { reels.Stop(); stops.Stop(); rewards.Stop(); StopJackpot(); active = null; }

        private void OnApplicationFocus(bool value) { focused = value; UpdateSuspension(); }
        private void OnApplicationPause(bool value) { paused = value; UpdateSuspension(); }
        private void UpdateSuspension()
        {
            if (!music) return;
            if (paused || !focused)
            { music.Pause(); reels.Pause(); ui.Stop(); stops.Stop(); rewards.Stop(); StopJackpot(); }
            else
            { music.UnPause(); if (active != null && animation.IsRunning) reels.UnPause(); }
        }

        private void OnDisable()
        {
            if (!music) return;
            music.Stop(); reels.Stop(); ui.Stop(); stops.Stop(); rewards.Stop(); StopJackpot();
        }

        private void OnDestroy()
        {
            if (jackpotView)
            { jackpotView.CelebrationStarted -= StartJackpot; jackpotView.CelebrationEnded -= StopJackpot; }
            if (animation != null)
            { animation.Started -= SpinStarted; animation.ReelStopped -= ReelStopped; animation.Finished -= SpinFinished; }
            if (slot != null) slot.SettlementConfirmed -= Settled;
            if (menu != null) { menu.RewardConfirmed -= DailyReward; menu.AudioSettingsChanged -= ApplySettings; }
            if (navigation != null) navigation.ScreenChanged -= ScreenChanged;
            foreach (var binding in buttons) if (binding.button) binding.button.onClick.RemoveListener(binding.action);
        }
    }
}

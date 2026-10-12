using System;
using IceP.Application;
using IceP.Domain.Configuration;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace IceP.Presentation
{
    public sealed class SlotView : MonoBehaviour, ISlotView
    {
        [SerializeField] private Button spinButton, betDownButton, betUpButton, backButton, retryButton;
        [SerializeField] private Text balanceLabel, betLabel, winLabel, freeSpinsLabel, message;
        [SerializeField] private ReelAnimationCoordinator animationCoordinator;
        [SerializeField] private PaylineView paylines;
        [SerializeField] private UnityEvent onConfirmedWin = new UnityEvent();
        private SlotDisplay previous;
        public Button SpinButton => spinButton;
        public Button BetDownButton => betDownButton;
        public Button BetUpButton => betUpButton;
        public Button BackButton => backButton;
        public Button RetryButton => retryButton;
        public ReelAnimationCoordinator AnimationCoordinator => animationCoordinator;
        public PaylineView Paylines => paylines;
        public string BalanceText => balanceLabel.text;
        public string BetText => betLabel.text;
        public string WinText => winLabel.text;
        public string FreeSpinsText => freeSpinsLabel.text;
        public string Message => message.text;
        public bool HasRequiredReferences => spinButton && betDownButton && betUpButton && backButton && retryButton &&
            balanceLabel && betLabel && winLabel && freeSpinsLabel && message && animationCoordinator && paylines;
        public event Action SpinRequested;
        public event Action<int> BetStepRequested;
        public event Action BackRequested;
        public event Action RetryRequested;
        public event Action<Guid, bool> AnimationFinished;
        public event Action Destroyed;
        public void Initialize(SlotRuntimeConfig config, SlotVisualSettings settings)
        {
            animationCoordinator.Initialize(config, settings); paylines.Initialize(animationCoordinator.Reels);
            spinButton.onClick.AddListener(Spin); betDownButton.onClick.AddListener(Down); betUpButton.onClick.AddListener(Up);
            backButton.onClick.AddListener(Back); retryButton.onClick.AddListener(Retry);
            animationCoordinator.Finished += Finished;
        }
        private void Spin() => SpinRequested?.Invoke();
        private void Down() => BetStepRequested?.Invoke(-1);
        private void Up() => BetStepRequested?.Invoke(1);
        private void Back() => BackRequested?.Invoke();
        private void Retry() => RetryRequested?.Invoke();
        private void Finished(Guid id, bool success) => AnimationFinished?.Invoke(id, success);
        public void Render(SlotDisplay display)
        {
            if (previous == null || previous.Balance != display.Balance) balanceLabel.text = $"Coins: {display.Balance:N0}";
            if (previous == null || previous.EffectiveBet != display.EffectiveBet) betLabel.text = $"Bet: {display.EffectiveBet:N0}";
            if (previous == null || previous.LastWin != display.LastWin) winLabel.text = $"Last win: {display.LastWin:N0}";
            if (previous == null || previous.FreeSpins != display.FreeSpins) freeSpinsLabel.text = $"Free Spins: {display.FreeSpins}";
            spinButton.interactable = display.CanSpin; betDownButton.interactable = betUpButton.interactable = display.CanSelectBet;
            backButton.interactable = display.CanBack; retryButton.gameObject.SetActive(display.CanRetry); retryButton.interactable = display.CanRetry;
            previous = display;
        }
        public void ShowMessage(string value) => message.text = value;
        public void ClearWins() => paylines.Clear();
        public void Animate(SpinResult spin) => animationCoordinator.Begin(spin);
        public void ShowConfirmedWin(SpinResult spin)
        {
            paylines.Show(spin);
            if (spin.PotentialPayout > 0) onConfirmedWin.Invoke();
        }
        public void CancelAnimation() { if (animationCoordinator) animationCoordinator.Cancel(); }
        private void OnDestroy()
        {
            Destroyed?.Invoke();
            if (animationCoordinator) animationCoordinator.Finished -= Finished;
            if (spinButton) spinButton.onClick.RemoveListener(Spin);
            if (betDownButton) betDownButton.onClick.RemoveListener(Down);
            if (betUpButton) betUpButton.onClick.RemoveListener(Up);
            if (backButton) backButton.onClick.RemoveListener(Back);
            if (retryButton) retryButton.onClick.RemoveListener(Retry);
        }
    }
}

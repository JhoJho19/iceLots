using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IceP.Presentation
{
    public sealed class DailyBonusView : MonoBehaviour
    {
        [SerializeField] private TMP_Text[] rewardLabels;
        [SerializeField] private Image[] rewardPanels;
        [SerializeField] private Sprite rewardDefaultSprite;
        [SerializeField] private Sprite rewardClaimedSprite;
        [SerializeField] private TMP_Text message;
        [SerializeField] private Button claimButton;
        [SerializeField] private Button retryButton;
        public Button ClaimButton => claimButton;
        public Button RetryButton => retryButton;
        public TMP_Text[] RewardLabels => (TMP_Text[])rewardLabels.Clone();
        public string Message => message.text;
        public bool HasRequiredReferences => rewardLabels != null && rewardLabels.Length == 7 &&
            rewardPanels != null && rewardPanels.Length == 7 && System.Array.TrueForAll(rewardLabels, x => x) &&
            System.Array.TrueForAll(rewardPanels, x => x) && rewardDefaultSprite && rewardClaimedSprite && message && claimButton && retryButton;
        public void Show(DailyBonusDisplay display, bool canRetry)
        {
            var status = display.Status;
            for (int i = 0; i < rewardLabels.Length; i++)
            {
                string state = display.Days[i] == RewardDisplayState.Claimed ? "Claimed" :
                    display.Days[i] == RewardDisplayState.Available ? "Available" :
                    display.Days[i] == RewardDisplayState.Current ? "Next reward" : "Upcoming";
                rewardLabels[i].text = $"Day {i + 1}\n{status.Rewards[i]:N0} coins\n{state}";
                bool claimed = display.Days[i] == RewardDisplayState.Claimed;
                rewardPanels[i].sprite = claimed ? rewardClaimedSprite : rewardDefaultSprite;
                // Fit the claimed badge to the card height without stretching its width.
                rewardPanels[i].preserveAspect = claimed;
                rewardPanels[i].color = claimed || display.Days[i] == RewardDisplayState.Available ? Color.white :
                    new Color(.18f, .16f, .28f);
            }
            claimButton.interactable = status.CanClaim;
            retryButton.gameObject.SetActive(canRetry);
            retryButton.interactable = canRetry;
        }
        public void ShowMessage(string value) => message.text = value;
    }
}

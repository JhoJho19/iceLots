using TMPro;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using IceP.Application;

namespace IceP.Presentation
{
    public sealed class JackpotView : MonoBehaviour, IJackpotView
    {
        [SerializeField] private TMP_Text titleLabel, amountLabel, availabilityLabel;
        private string status = string.Empty, reset = string.Empty;
        [SerializeField] private Image background;
        private Coroutine celebration;
        public event Action Activated;
        public event Action Destroyed;
        public bool HasRequiredReferences => titleLabel && amountLabel && availabilityLabel && background;
        public string AmountText => amountLabel.text;
        public string StatusText => status;
        public string ResetText => reset;
        public string AvailabilityText => availabilityLabel.text;
        public int CelebrationCount { get; private set; }
        public bool IsCelebrating => celebration != null;
        public void Render(JackpotDisplay value)
        {
            amountLabel.text = value.Amount; status = value.Status;
            reset = value.Reset; availabilityLabel.text = value.Availability;
        }
        public void Celebrate()
        {
            CancelCelebration();
            if (!isActiveAndEnabled) return;
            CelebrationCount++; celebration = StartCoroutine(Pulse());
        }
        private IEnumerator Pulse()
        {
            float elapsed = 0;
            titleLabel.text = "JACKPOT WON!";
            while (elapsed < 2)
            {
                elapsed += Time.unscaledDeltaTime;
                float pulse = Mathf.Sin(elapsed * Mathf.PI * 3) * .5f + .5f;
                amountLabel.transform.localScale = Vector3.one * (1 + pulse * .06f);
                background.color = Color.Lerp(BaseColor, new Color(.45f, .3f, .08f, .95f), pulse);
                yield return null;
            }
            celebration = null; Restore();
        }
        private static Color BaseColor => new Color(.025f, .1f, .18f, .92f);
        private void Restore()
        {
            if (titleLabel) titleLabel.text = "JACKPOT OF THE WEEK";
            if (amountLabel) { amountLabel.transform.localScale = Vector3.one; amountLabel.color = Color.white; }
            if (background) background.color = BaseColor;
        }
        public void CancelCelebration() { if (celebration != null) StopCoroutine(celebration); celebration = null; Restore(); }
        private void OnEnable() => Activated?.Invoke();
        private void OnDisable() => CancelCelebration();
        private void OnDestroy() { Destroyed?.Invoke(); CancelCelebration(); }
    }
}

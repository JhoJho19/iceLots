using AssetKits.ParticleImage;
using AssetKits.ParticleImage.Enumerations;
using UnityEngine;

namespace IceP.Presentation
{
    // Presentation only: adapt the authored emitter to the current Canvas dimensions.
    public sealed class JackpotCoinRainView : MonoBehaviour
    {
        [SerializeField] private ParticleImage coins;
        private const float TravelSeconds = 1.05f;
        public ParticleImage Coins => coins;

        public void Play(float celebrationSeconds)
        {
            if (!coins || !isActiveAndEnabled) return;
            Canvas.ForceUpdateCanvases();
            Stop();
            var viewport = (RectTransform)transform;
            float size = Mathf.Clamp(Mathf.Min(viewport.rect.width, viewport.rect.height) * .12f, 90, 170);
            float margin = size;
            coins.rectTransform.anchorMin = coins.rectTransform.anchorMax = new Vector2(.5f, 1);
            coins.rectTransform.pivot = new Vector2(.5f, .5f);
            coins.rectTransform.anchoredPosition = new Vector2(0, margin);
            coins.rectTransform.localRotation = Quaternion.identity;
            coins.rectTransform.localScale = Vector3.one;
            coins.lineLength = Mathf.Max(1, viewport.rect.width - size);
            coins.shape = EmitterShape.Line;
            coins.space = Simulation.Local;
            coins.timeScale = TimeScale.Unscaled;
            coins.loop = false;
            coins.startDelay = 0;
            // Stop emission early enough for the final coins to leave below the screen at 3s.
            coins.duration = Mathf.Max(.1f, celebrationSeconds - TravelSeconds);
            coins.lifetime = new ParticleSystem.MinMaxCurve(TravelSeconds);
            coins.startSpeed = new ParticleSystem.MinMaxCurve(-(viewport.rect.height + 2 * margin) / (TravelSeconds * 100));
            coins.startSize = new SeparatedMinMaxCurve(size)
            { mainCurve = new ParticleSystem.MinMaxCurve(size * .8f, size) };
            coins.rateOverTime = 30;
            coins.Play();
        }

        public void Stop() { if (coins) coins.Stop(true); }
        private void OnDisable() => Stop();
    }
}

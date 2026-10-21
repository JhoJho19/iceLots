using UnityEngine;

namespace IceP.Presentation
{
    [CreateAssetMenu(fileName = "AudioConfig", menuName = "IceP/Audio Config")]
    public sealed class AudioConfigSO : ScriptableObject
    {
        public AudioClip Music, UiClick, UiOpen, SpinStart, ReelLoop, Scatter;
        public AudioClip SmallWin, MediumWin, BigWin;
        public AudioClip Jackpot;
        [Range(0, 1)] public float MusicGain = .3f;
        [Range(0, 1)] public float EffectsGain = .7f;
        [Range(0, 1)] public float ReelGain = .35f;
        [Min(1)] public float MediumWinMultiplier = 5;
        [Min(1)] public float BigWinMultiplier = 20;
    }
}

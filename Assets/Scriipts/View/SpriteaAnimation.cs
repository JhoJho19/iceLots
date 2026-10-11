using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class UISpriteAnimation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Image targetImage;
    [SerializeField] private Sprite[] frames;

    [Header("Settings")]
    [SerializeField, Min(1)] private int fps = 12;
    [SerializeField] private bool playOnEnable = true;
    [SerializeField] private bool loop = false;

    private CancellationTokenSource _cts;

    private void OnEnable()
    {
        if (playOnEnable)
            Play();
    }

    private void OnDisable()
    {
        Stop();
    }

    public void Play()
    {
        Stop();

        if (targetImage == null || frames == null || frames.Length == 0)
            return;

        _cts = new CancellationTokenSource();
        PlayAsync(_cts.Token).Forget();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async UniTaskVoid PlayAsync(CancellationToken token)
    {
        try
        {
            do
            {
                for (int i = 0; i < frames.Length; i++)
                {
                    token.ThrowIfCancellationRequested();

                    targetImage.sprite = frames[i];

                    await UniTask.Delay(
                        TimeSpan.FromSeconds(1f / fps),
                        cancellationToken: token,
                        ignoreTimeScale: true
                    );
                }

            } while (loop);
        }
        catch (OperationCanceledException)
        {
            // Animation cancelled.
        }
    }
}
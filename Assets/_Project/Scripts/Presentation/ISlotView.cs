using System;
using IceP.Application;

namespace IceP.Presentation
{
    public interface ISlotView
    {
        event Action SpinRequested;
        event Action<int> BetStepRequested;
        event Action BackRequested;
        event Action RetryRequested;
        event Action<Guid, bool> AnimationFinished;
        event Action Destroyed;
        void Render(SlotDisplay display);
        void ShowMessage(string message);
        void ClearWins();
        void Animate(SpinResult spin);
        void ShowConfirmedWin(SpinResult spin);
        void CancelAnimation();
    }
}

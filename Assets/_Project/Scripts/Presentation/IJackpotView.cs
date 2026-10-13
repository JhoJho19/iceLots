using System;
namespace IceP.Presentation
{
    public interface IJackpotView
    {
        event Action Activated;
        event Action Destroyed;
        void Render(JackpotDisplay display);
        void Celebrate();
        void CancelCelebration();
    }
}

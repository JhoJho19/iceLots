using System;

namespace IceP.Presentation
{
    // One run token prevents delayed callbacks from an earlier animation joining a newer run.
    public sealed class ReelCompletionBarrier
    {
        private readonly bool[] finished = new bool[5];
        private Guid spinId;
        private int count;
        private bool running;
        public void Begin(Guid id) { spinId = id; count = 0; Array.Clear(finished, 0, finished.Length); running = true; }
        public bool Report(Guid id, int reel)
        {
            if (!running || id != spinId || reel < 0 || reel >= finished.Length || finished[reel]) return false;
            finished[reel] = true;
            if (++count != finished.Length) return false;
            running = false; return true;
        }
        public void Cancel() => running = false;
    }
}

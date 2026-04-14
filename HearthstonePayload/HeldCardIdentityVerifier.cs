using System;

namespace HearthstonePayload
{
    internal enum HeldCardIdentityStatus
    {
        None = 0,
        Expected = 1,
        Mismatch = 2
    }

    internal struct HeldCardProbeResult
    {
        public bool Found;
        public int HeldEntityId;
    }

    internal static class HeldCardIdentityVerifier
    {
        internal static HeldCardIdentityStatus WaitForExpectedHeldCardIdentity(
            Func<HeldCardProbeResult> probe,
            int expectedEntityId,
            out int heldEntityId,
            int timeoutMs = 100,
            int pollMs = 8,
            Func<int> tickCountProvider = null,
            Action<int> sleep = null)
        {
            heldEntityId = 0;
            if (probe == null || expectedEntityId <= 0)
                return HeldCardIdentityStatus.None;

            var getTickCount = tickCountProvider ?? (() => Environment.TickCount);
            var sleepAction = sleep ?? ThreadSleep;
            var deadline = getTickCount() + timeoutMs;

            while (true)
            {
                var result = probe();
                if (result.Found)
                {
                    heldEntityId = result.HeldEntityId;
                    return heldEntityId == expectedEntityId
                        ? HeldCardIdentityStatus.Expected
                        : HeldCardIdentityStatus.Mismatch;
                }

                if (getTickCount() - deadline >= 0)
                    break;

                sleepAction(pollMs);
            }

            heldEntityId = 0;
            return HeldCardIdentityStatus.None;
        }

        private static void ThreadSleep(int delayMs)
        {
            System.Threading.Thread.Sleep(delayMs);
        }
    }
}

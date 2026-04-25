using System;

namespace CereaBridge
{
    internal sealed partial class BridgeService
    {
        private readonly object _sync = new object();
        private readonly TimeSpan _steerCommandTimeout = TimeSpan.FromMilliseconds(250);
        private short _imuHeading16;
        private short _imuRoll16;
        private DateTime _lastSteerDataUtc = DateTime.MinValue;
    }
}

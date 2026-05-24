using System;

namespace Orbital.Galaxy
{
    [Serializable]
    public struct CaptureCriteria
    {
        public float CaptureRingRadius;
        public float CaptureMinSpeed;
        public float CaptureMaxSpeed;
        public float CaptureAngleToleranceDegrees;
    }
}

namespace Scripts.Runtime.Common
{
    public static class MathConstants
    {
        /// <summary>
        /// Length below which a vector is treated as zero, e.g. a gradient that cannot be
        /// normalised. A compile-time constant so Burst folds it into the code; a static readonly
        /// field would work too but would leave the question open.
        /// </summary>
        public const float Epsilon = 1e-6f;
    }
}

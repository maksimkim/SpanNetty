using System.Runtime.CompilerServices;

namespace DotNetty
{
    /// <summary>Helper class for constants for inlining methods</summary>
    public static class InlineMethod
    {
        /// <summary>Value for lining method</summary>
        public const MethodImplOptions AggressiveInlining = MethodImplOptions.AggressiveInlining;

        /// <summary>Value for lining method</summary>
        public const MethodImplOptions AggressiveOptimization =
#if NET6_0_OR_GREATER
            MethodImplOptions.AggressiveOptimization;
#else
            MethodImplOptions.AggressiveInlining;
#endif
    }
}
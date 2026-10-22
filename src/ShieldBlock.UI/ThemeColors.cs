using System.Drawing;

namespace ShieldBlock.UI
{
    public static class ThemeColors
    {
        // Dark Mode Base Palette
        public static readonly Color Background = Color.FromArgb(18, 20, 26);       // #12141A
        public static readonly Color Surface = Color.FromArgb(26, 29, 36);          // #1A1D24
        public static readonly Color SurfaceHover = Color.FromArgb(34, 38, 48);     // #222630
        public static readonly Color Card = Color.FromArgb(22, 25, 32);             // #161920
        public static readonly Color Border = Color.FromArgb(42, 47, 61);           // #2A2F3D

        // Accent Colors
        public static readonly Color Emerald = Color.FromArgb(0, 208, 132);         // #00D084 (Protected)
        public static readonly Color EmeraldGlow = Color.FromArgb(0, 150, 95);
        public static readonly Color Crimson = Color.FromArgb(255, 71, 87);         // #FF4757 (Emergency / Blocked)
        public static readonly Color CrimsonHover = Color.FromArgb(235, 59, 74);
        public static readonly Color Amber = Color.FromArgb(255, 177, 66);          // #FFB142 (Paused / Safe Mode)
        public static readonly Color Cyan = Color.FromArgb(0, 180, 216);            // #00B4D8 (Metrics)
        public static readonly Color Purple = Color.FromArgb(142, 68, 173);         // #8E44AD (HTTPS)

        // Typography
        public static readonly Color TextPrimary = Color.FromArgb(240, 243, 246);
        public static readonly Color TextSecondary = Color.FromArgb(148, 155, 168);
        public static readonly Color TextMuted = Color.FromArgb(96, 102, 115);
    }
}

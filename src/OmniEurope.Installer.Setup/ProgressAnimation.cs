using System;

namespace OmniEurope.Installer.Setup;

/// <summary>
/// Pure step of the progress bar animation: the displayed value chases the progress Windows Installer
/// reported and never passes it. Null means nothing reported yet, shown as an indeterminate bar rather
/// than a number nobody measured.
/// </summary>
internal static class ProgressAnimation
{
    internal const int CatchUpDivisor = 4;

    internal static int? Step(int? display, int? target)
    {
        if (target is not { } reported)
        {
            return null;
        }

        int current = display ?? 0;
        if (current >= reported)
        {
            return current;
        }

        int gap = reported - current;
        return Math.Min(reported, current + Math.Max(1, gap / CatchUpDivisor));
    }
}


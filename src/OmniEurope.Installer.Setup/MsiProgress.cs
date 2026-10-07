using System.Globalization;
using System.Text.RegularExpressions;

namespace OmniEurope.Installer.Setup;

/// <summary>
/// Turns Windows Installer progress messages into a percentage, following the documented protocol
/// ("Parsing Windows Installer Messages"): field 1 = 0 resets the total, 1 sets the per-ActionData
/// step, 2 moves the position, 3 extends the total. The script-generation pass fills the first
/// tenth of the bar, the execution pass the rest.
/// </summary>
internal sealed class MsiProgress
{
    private const int ScriptShare = 10;
    private static readonly Regex Field = new(@"(\d):\s*(-?\d+)", RegexOptions.CultureInvariant);

    private long _total;
    private long _position;
    private bool _forward = true;
    private bool _script;
    private bool _stepPerActionData;
    private long _step;

    /// <summary>Overall percentage, or null before Windows Installer has announced any total.</summary>
    public int? Percent
    {
        get
        {
            if (_total <= 0)
            {
                return null;
            }

            long done = _forward ? _position : _total - _position;
            long phase = Clamp(done * 100 / _total);
            return (int)(_script ? phase * ScriptShare / 100 : ScriptShare + phase * (100 - ScriptShare) / 100);
        }
    }

    public void OnProgress(string message)
    {
        var fields = new long[5];
        foreach (Match match in Field.Matches(message))
        {
            fields[int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)] = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        switch (fields[1])
        {
            case 0:
                _total = fields[2];
                _forward = fields[3] == 0;
                _position = _forward ? 0 : _total;
                _script = fields[4] == 1;
                _stepPerActionData = false;
                break;
            case 1:
                _stepPerActionData = fields[3] == 1;
                _step = fields[2];
                break;
            case 2:
                Move(fields[2]);
                break;
            case 3:
                _total += fields[2];
                break;
        }
    }

    public void OnActionData()
    {
        if (_stepPerActionData)
        {
            Move(_step);
        }
    }

    private void Move(long ticks)
    {
        _position += _forward ? ticks : -ticks;
    }

    private static long Clamp(long value) => value < 0 ? 0 : value > 100 ? 100 : value;
}

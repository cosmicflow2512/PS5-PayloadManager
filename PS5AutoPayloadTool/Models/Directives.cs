namespace PS5AutoPayloadTool.Models;

/// <summary>Marker interface for all executable profile directives.</summary>
public interface IDirective { }

/// <summary>Send a payload file to the PS5 on a specific port.</summary>
public class SendDirective : IDirective
{
    public string FilePath { get; init; } = "";
    public int Port { get; init; }
}

/// <summary>Wait a fixed number of milliseconds before the next step.</summary>
public class DelayDirective : IDirective
{
    public int DelayMs { get; init; }
}

/// <summary>Wait until a TCP port on the PS5 is reachable.</summary>
public class WaitPortDirective : IDirective
{
    public int Port { get; init; }
    public int TimeoutSeconds { get; init; } = 60;
    public int IntervalMs { get; init; } = 500;
}

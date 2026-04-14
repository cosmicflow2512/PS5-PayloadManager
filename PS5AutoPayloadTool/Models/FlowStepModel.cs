namespace PS5AutoPayloadTool.Models;
public enum FlowStepType { Payload, Wait, Delay }
public class FlowStepModel
{
    public FlowStepType Type { get; set; }
    public string PayloadName { get; set; } = "";
    public int Port { get; set; } = 9021;
    public int TimeoutSeconds { get; set; } = 60;
    public int IntervalMs { get; set; } = 500;
    public int DelayMs { get; set; } = 500;

    public string DisplayText => Type switch
    {
        FlowStepType.Payload => $"PAYLOAD  {PayloadName}  \u2192  :{Port}",
        FlowStepType.Wait    => $"WAIT  port {Port}  ({TimeoutSeconds}s timeout)",
        FlowStepType.Delay   => $"DELAY  {DelayMs} ms",
        _ => ""
    };

    public string TypeLabel => Type.ToString().ToUpperInvariant();
    public string TypeColor => Type switch
    {
        FlowStepType.Payload => "#89B4FA",
        FlowStepType.Wait    => "#F9E2AF",
        FlowStepType.Delay   => "#A6E3A1",
        _ => "#CDD6F4"
    };

    public string ToProfileLine() => Type switch
    {
        FlowStepType.Payload => $"{PayloadName} {Port}",
        FlowStepType.Wait    => $"?{Port} {TimeoutSeconds} {IntervalMs}",
        FlowStepType.Delay   => $"!{DelayMs}",
        _ => ""
    };
}

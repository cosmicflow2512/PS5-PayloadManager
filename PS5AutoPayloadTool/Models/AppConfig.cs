using System.Collections.Generic;
namespace PS5AutoPayloadTool.Models;

public class AppConfig
{
    public string PS5Host { get; set; } = "192.168.1.100";
    public string GitHubToken { get; set; } = "";
    public List<PayloadSource> Sources { get; set; } = new();
    public List<PayloadItem> Payloads { get; set; } = new();
    public List<FlowStepModel> CurrentFlow { get; set; } = new();
}

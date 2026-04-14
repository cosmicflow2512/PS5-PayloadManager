using System;
using System.Collections.Generic;
using System.IO;
namespace PS5AutoPayloadTool.Models;
public class PayloadItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SourceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string CurrentVersion { get; set; } = "";
    public List<string> AvailableVersions { get; set; } = new();
    public string LocalPath { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime LastUpdated { get; set; }
    public bool IsDownloaded => !string.IsNullOrEmpty(LocalPath) && File.Exists(LocalPath);
}

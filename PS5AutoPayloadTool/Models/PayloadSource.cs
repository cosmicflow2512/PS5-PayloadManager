using System;
namespace PS5AutoPayloadTool.Models;
public enum SourceType { GitHubRelease, GitHubFolder }
public class PayloadSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Owner { get; set; } = "";
    public string Repo { get; set; } = "";
    public SourceType Type { get; set; } = SourceType.GitHubRelease;
    public string Filter { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public string DisplayName => string.IsNullOrEmpty(Owner) ? "(unnamed)" : $"{Owner}/{Repo}";
}

namespace NvProfileSwitcher;

internal sealed class SavedDisplayInfo
{
    public SavedDisplayInfo(string monitorId, string name, bool connected) { MonitorId = monitorId; Name = name; Connected = connected; }
    public string MonitorId { get; }
    public string Name { get; set; }
    public bool Connected { get; }
    public string Status => Connected ? "Connected" : "Disconnected";
}

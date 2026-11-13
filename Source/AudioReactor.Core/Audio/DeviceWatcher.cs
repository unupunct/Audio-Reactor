using NAudio.CoreAudioApi;

namespace AudioReactor.Core.Audio;

/// <summary>Raises events when Windows audio endpoints are added, removed or the default output changes.</summary>
public sealed class DeviceWatcher : IDisposable
{
    private readonly MMDeviceEnumerator _en = new();
    private readonly MMDeviceNotificationClient _client;
    public event Action<string, string>? DeviceAdded;       // id, friendly name
    public event Action<string>? DeviceRemoved;             // id
    public event Action? DefaultOutputChanged;

    public DeviceWatcher()
    {
        _client = _en.CreateNotificationClient(false);
        _client.DeviceStateChanged += (_, e) =>
        {
            if (e.NewState == DeviceState.Active) { if (IsRender(e.DeviceId)) DeviceAdded?.Invoke(e.DeviceId, NameOf(e.DeviceId)); }
            else if (e.NewState is DeviceState.NotPresent or DeviceState.Unplugged or DeviceState.Disabled) DeviceRemoved?.Invoke(e.DeviceId);
        };
        _client.DeviceRemoved += (_, e) => DeviceRemoved?.Invoke(e.DeviceId);
        _client.DefaultDeviceChanged += (_, e) => { if (e.Flow == DataFlow.Render && e.Role == Role.Multimedia) DefaultOutputChanged?.Invoke(); };
    }

    private string NameOf(string id) { try { return _en.GetDevice(id).FriendlyName; } catch { return "Audio device"; } }
    private bool IsRender(string id) { try { return _en.GetDevice(id).DataFlow == DataFlow.Render; } catch { return false; } }

    public void Dispose() { try { _client.Dispose(); } catch { } _en.Dispose(); }
}

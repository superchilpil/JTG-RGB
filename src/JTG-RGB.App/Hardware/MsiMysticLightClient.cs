using System.Runtime.InteropServices;
using System.Text;

namespace JtgRgb.App.Hardware;

/// <summary>
/// Optional adapter for MSI's official Mystic Light SDK.
/// The native MSI SDK is not redistributed by JTG-RGB; users select the x64 SDK DLL locally.
/// </summary>
public sealed class MsiMysticLightClient : IDisposable
{
    private const int Ok = 0;
    private const string SettingsDirectoryName = "JTG-RGB";
    private const string SettingsFileName = "msi-sdk-path.txt";
    private readonly object _sync = new();
    private IntPtr _library;
    private string? _sdkPath;
    private InitializeDelegate? _initialize;
    private GetDeviceInfoDelegate? _getDeviceInfo;
    private SetLedColorDelegate? _setLedColor;
    private IReadOnlyList<MsiLightingDevice> _devices = [];

    public bool IsInitialized => _library != IntPtr.Zero;
    public string? SdkPath => _sdkPath;
    public IReadOnlyList<MsiLightingDevice> Devices => _devices;

    public static string? LoadSavedSdkPath()
    {
        try
        {
            var file = GetSettingsFilePath();
            return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static void SaveSdkPath(string path)
    {
        var file = GetSettingsFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, path, Encoding.UTF8);
    }

    public IReadOnlyList<MsiLightingDevice> Connect(string selectedDllPath)
    {
        lock (_sync)
        {
            DisconnectCore();
            if (string.IsNullOrWhiteSpace(selectedDllPath) || !File.Exists(selectedDllPath))
                throw new FileNotFoundException("Select the official MSI Mystic Light SDK DLL first.", selectedDllPath);

            var fullPath = Path.GetFullPath(selectedDllPath);
            var folder = Path.GetDirectoryName(fullPath)!;
            if (!fullPath.EndsWith("MysticLight_SDK_x64.dll", StringComparison.OrdinalIgnoreCase) &&
                !fullPath.EndsWith("MysticLight_SDK.dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose MysticLight_SDK_x64.dll (recommended) or MysticLight_SDK.dll from MSI's official SDK package.");

            if (!Environment.Is64BitProcess)
                throw new PlatformNotSupportedException("The MSI backend requires the 64-bit JTG-RGB build.");

            SetDllDirectory(folder);
            _library = NativeLibrary.Load(fullPath);
            _sdkPath = fullPath;
            try
            {
                _initialize = LoadDelegate<InitializeDelegate>("MLAPI_Initialize");
                _getDeviceInfo = LoadDelegate<GetDeviceInfoDelegate>("MLAPI_GetDeviceInfo");
                _setLedColor = LoadDelegate<SetLedColorDelegate>("MLAPI_SetLedColor");

                var initStatus = _initialize();
                if (initStatus != Ok)
                    throw new InvalidOperationException($"MSI Mystic Light SDK initialization failed (status {initStatus}). Ensure MSI Center/Mystic Light is installed and running.");

                var status = _getDeviceInfo(out var types, out var counts);
                if (status != Ok)
                    throw new InvalidOperationException($"MSI SDK could not enumerate lighting devices (status {status}). Ensure MSI Center/Mystic Light is installed and running.");

                if (types.Length != counts.Length)
                    throw new InvalidDataException("MSI SDK returned mismatched device and LED-count arrays.");

                var found = new List<MsiLightingDevice>();
                for (var i = 0; i < types.Length; i++)
                {
                    if (!uint.TryParse(counts[i], out var ledCount) || ledCount == 0)
                        continue;
                    found.Add(new MsiLightingDevice(types[i], ledCount));
                }

                _devices = found;
                return _devices;
            }
            catch
            {
                DisconnectCore();
                throw;
            }
        }
    }

    public void SetAllDevicesColor(byte red, byte green, byte blue)
    {
        lock (_sync)
        {
            if (!IsInitialized || _setLedColor is null)
                throw new InvalidOperationException("MSI Mystic Light is not connected. Select the official MSI SDK DLL and connect first.");

            foreach (var device in _devices)
            {
                for (uint ledIndex = 0; ledIndex < device.LedCount; ledIndex++)
                {
                    var status = _setLedColor(device.Type, ledIndex, red, green, blue);
                    if (status != Ok)
                        throw new InvalidOperationException($"MSI SDK failed to set {device.Type}, LED {ledIndex} (status {status}).");
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_sync) DisconnectCore();
    }

    private T LoadDelegate<T>(string exportName) where T : Delegate
    {
        var address = NativeLibrary.GetExport(_library, exportName);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private void DisconnectCore()
    {
        if (_library != IntPtr.Zero)
        {
            NativeLibrary.Free(_library);
            _library = IntPtr.Zero;
        }

        _initialize = null;
        _getDeviceInfo = null;
        _setLedColor = null;
        _devices = [];
        _sdkPath = null;
        SetDllDirectory(null);
    }

    private static string GetSettingsFilePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), SettingsDirectoryName, SettingsFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string? lpPathName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitializeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDeviceInfoDelegate(
        [Out, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)] out string[] deviceTypes,
        [Out, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)] out string[] ledCounts);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int SetLedColorDelegate(
        [In, MarshalAs(UnmanagedType.BStr)] string deviceType,
        [In, MarshalAs(UnmanagedType.U4)] uint ledIndex,
        [In, MarshalAs(UnmanagedType.U4)] uint red,
        [In, MarshalAs(UnmanagedType.U4)] uint green,
        [In, MarshalAs(UnmanagedType.U4)] uint blue);
}

public sealed record MsiLightingDevice(string Type, uint LedCount)
{
    public string DisplayName => $"{Type} ({LedCount} lighting zone(s)/LEDs)";
}

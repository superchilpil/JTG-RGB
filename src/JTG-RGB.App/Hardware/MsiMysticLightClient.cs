using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;

namespace JtgRgb.App.Hardware;

/// <summary>
/// Adapter for MSI's Mystic Light SDK. The SDK DLL can be packaged beside the app,
/// or selected by the user when a redistribution-approved copy is not included.
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
    private GetLedInfoDelegate? _getLedInfo;
    private IReadOnlyList<MsiLightingDevice> _devices = [];

    public bool IsInitialized => _library != IntPtr.Zero;
    public string? SdkPath => _sdkPath;
    public IReadOnlyList<MsiLightingDevice> Devices => _devices;

    public static string? FindBundledSdkPath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        foreach (var fileName in new[] { "MysticLight_SDK_x64.dll", "MysticLight_SDK.dll" })
        {
            var candidate = Path.Combine(baseDirectory, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public static async Task<string> DownloadSdkToAppDataAsync(CancellationToken cancellationToken = default)
    {
        // This is an explicit user-triggered download from MSI's official download host.
        const string downloadUrl = "https://download.msi.com/uti_exe/Mystic_light_SDK.zip";
        const long maxArchiveBytes = 100L * 1024 * 1024;
        const long maxDllBytes = 50L * 1024 * 1024;
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            SettingsDirectoryName,
            "MSI");
        Directory.CreateDirectory(settingsDirectory);

        var archivePath = Path.Combine(settingsDirectory, "Mystic_light_SDK.zip");
        var tempArchivePath = archivePath + ".download";
        var destinationPath = Path.Combine(settingsDirectory, "MysticLight_SDK_x64.dll");
        var tempDllPath = destinationPath + ".download";

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is long contentLength && contentLength > maxArchiveBytes)
                throw new InvalidDataException("The MSI SDK archive is larger than the allowed download size.");

            await using (var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(tempArchivePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long totalBytes = 0;
                int read;
                while ((read = await networkStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    totalBytes += read;
                    if (totalBytes > maxArchiveBytes)
                        throw new InvalidDataException("The MSI SDK archive exceeded the allowed download size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            using (var archive = ZipFile.OpenRead(tempArchivePath))
            {
                var sdkEntry = archive.Entries.FirstOrDefault(entry =>
                    string.Equals(Path.GetFileName(entry.FullName.Replace('/', Path.DirectorySeparatorChar)), "MysticLight_SDK_x64.dll", StringComparison.OrdinalIgnoreCase));

                if (sdkEntry is null)
                    throw new InvalidDataException("The MSI SDK ZIP did not contain MysticLight_SDK_x64.dll.");
                if (sdkEntry.Length <= 0 || sdkEntry.Length > maxDllBytes)
                    throw new InvalidDataException("The MSI SDK DLL has an unexpected file size.");

                await using var entryStream = sdkEntry.Open();
                await using var dllStream = new FileStream(tempDllPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await entryStream.CopyToAsync(dllStream, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempDllPath, destinationPath, overwrite: true);
            SaveSdkPath(destinationPath);
            return destinationPath;
        }
        finally
        {
            TryDelete(tempArchivePath);
            TryDelete(tempDllPath);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

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
                _getLedInfo = LoadDelegate<GetLedInfoDelegate>("MLAPI_GetLedInfo");

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
                    var ledNames = new List<string>();
                    for (uint ledIndex = 0; ledIndex < ledCount; ledIndex++)
                    {
                        var displayName = $"{types[i]} — LED {ledIndex + 1}";
                        try
                        {
                            if (_getLedInfo is not null &&
                                _getLedInfo(types[i], ledIndex, out var sdkName, out _) == Ok &&
                                !string.IsNullOrWhiteSpace(sdkName))
                            {
                                displayName = sdkName;
                            }
                        }
                        catch (Exception ex) when (ex is MarshalDirectiveException or InvalidOperationException or ArgumentException)
                        {
                            // Keep a stable fallback label if this SDK version does not return LED metadata.
                        }
                        ledNames.Add(displayName);
                    }

                    found.Add(new MsiLightingDevice(types[i], ledCount, ledNames));
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

    public void SetLedColor(string deviceType, uint ledIndex, byte red, byte green, byte blue)
    {
        lock (_sync)
        {
            if (!IsInitialized || _setLedColor is null)
                throw new InvalidOperationException("MSI Mystic Light is not connected. Select the official MSI SDK DLL and connect first.");

            var device = _devices.FirstOrDefault(d => string.Equals(d.Type, deviceType, StringComparison.Ordinal));
            if (device is null || ledIndex >= device.LedCount)
                throw new InvalidOperationException($"The MSI SDK did not report {deviceType}, LED {ledIndex}.");

            var status = _setLedColor(deviceType, ledIndex, red, green, blue);
            if (status != Ok)
            {
                var explanation = status switch
                {
                    -2 => "The MSI SDK request timed out. Check that MSI Center and Mystic Light are running.",
                    -3 => "The installed MSI Center/Mystic Light version may not be supported by this SDK.",
                    -102 => "The SDK could not find this lighting zone.",
                    -103 => "This lighting zone's current effect may not support direct color changes.",
                    _ => "Check MSI Center/Mystic Light and the SDK compatibility."
                };
                throw new InvalidOperationException($"MSI SDK failed to set {deviceType}, LED {ledIndex} (status {status}). {explanation}");
            }
        }
    }

    public void SetAllDevicesColor(byte red, byte green, byte blue)
    {
        foreach (var device in _devices)
            for (uint ledIndex = 0; ledIndex < device.LedCount; ledIndex++)
                SetLedColor(device.Type, ledIndex, red, green, blue);
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
        _getLedInfo = null;
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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate int GetLedInfoDelegate(
        [In, MarshalAs(UnmanagedType.BStr)] string deviceType,
        [In, MarshalAs(UnmanagedType.U4)] uint ledIndex,
        [Out, MarshalAs(UnmanagedType.BStr)] out string ledName,
        [Out, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)] out string[] supportedStyles);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int SetLedColorDelegate(
        [In, MarshalAs(UnmanagedType.BStr)] string deviceType,
        [In, MarshalAs(UnmanagedType.U4)] uint ledIndex,
        [In, MarshalAs(UnmanagedType.U4)] uint red,
        [In, MarshalAs(UnmanagedType.U4)] uint green,
        [In, MarshalAs(UnmanagedType.U4)] uint blue);
}

public sealed record MsiLightingDevice(string Type, uint LedCount, IReadOnlyList<string> LedNames)
{
    public string DisplayName => $"{Type} ({LedCount} lighting zone(s)/LEDs)";
    public string GetLedName(uint index) =>
        index < LedNames.Count ? LedNames[(int)index] : $"{Type} — LED {index + 1}";
}

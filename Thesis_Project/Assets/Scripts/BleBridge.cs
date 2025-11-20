using UnityEngine;
using UnityEngine.Android;
using System.Globalization;
using TMPro;
using System.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

public class BleBridge : MonoBehaviour
{
    // ---------- Singleton ----------
    public static BleBridge Instance { get; private set; }

    const int MAX_QUEUE_SIZE = 5000;

    // ---------- Public events (optional για άλλα scripts) ----------
    public event System.Action<string, string> OnDeviceFound; // (name, address)
    public event System.Action<string> OnStatus;              // status messages
    public event System.Action<string> OnLine;                // raw line: "id,qx,qy,qz,qw" ή "id,qw,qx,qy,qz"

    public enum QuaternionFormat { XYZW, WXYZ }

    [Header("IMU Quaternion Format")]
    [Tooltip("Πως στέλνει το Arduino τα 4 στοιχεία μετά το id. ΤΩΡΑ: id,qx,qy,qz,qw => XYZW")]
    public QuaternionFormat quaternionFormat = QuaternionFormat.XYZW;

    [Tooltip("Αν κάποιο axis βγαίνει ανάποδα, τικάρεις εδώ")]
    public bool invertX = false;
    public bool invertY = false;
    public bool invertZ = false;

    [Header("UI")]
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI devicesText;
    public TextMeshProUGUI dataText;
    public GameObject connectButton;      // optional
    public MotionManager motionManager;   // διαχειρίζεται το rig του χεριού

    private AndroidJavaObject bleManager;
    private string arduinoAddress;

    // Reassembly buffer για BLE κείμενα (σπαστές γραμμές)
    private readonly StringBuilder lineBuffer = new StringBuilder(4096);
    private readonly ConcurrentQueue<string> _lineQueue =
        new ConcurrentQueue<string>();
    private readonly object _lineLock = new object();

    // Batch/Throttle
    [Header("Perf")]
    public bool showLiveDataText = false;
    public int maxLinesPerFrame = 800;
    private float _nextUiUpdateTime = 0f;
    private string _lastDataLine;

    private static int IndexOfNewline(StringBuilder sb)
    {
        for (int i = 0; i < sb.Length; i++)
            if (sb[i] == '\n') return i;
        return -1;
    }

    // ---------- Unity lifecycle ----------
    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        if (connectButton) connectButton.SetActive(false);
        SetStatus("Ready for BLE connection");

        using (var managerClass = new AndroidJavaClass("com.mbrats01.ble_manager.BleManager"))
        {
            bleManager = managerClass.CallStatic<AndroidJavaObject>("getInstance", GetUnityActivity());
        }

        if (bleManager != null)
        {
            bleManager.Call("setCallback", new BleCallbackProxy(this));
            SetStatus("BLE manager initialized");
        }
        else
        {
            SetStatus("BLE manager not found!");
        }
    }

    void Update()
    {
        // Consume lines in batches so we don't choke the main thread
        int processed = 0;
        while (processed < maxLinesPerFrame && _lineQueue.TryDequeue(out var line))
        {
            // 1) Fire raw line event
            OnLine?.Invoke(line);

            // 2) Apply to scene (parse + send to MotionManager)
            ParseLine_MainThread(line);

            // 3) Remember last for optional UI
            _lastDataLine = line;
            processed++;
        }

        // Throttle UI (π.χ. 10Hz)
        if (showLiveDataText && dataText && Time.unscaledTime >= _nextUiUpdateTime)
        {
            dataText.text = _lastDataLine ?? "";
            _nextUiUpdateTime = Time.unscaledTime + 0.1f; // 10 Hz
        }
    }

    // ---------- UI Buttons ----------
    public void RequestBlePermissions()
    {
        var callbacks = new PermissionCallbacks();
        int sdk = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");

        if (sdk >= 31)
        {
            Permission.RequestUserPermissions(new[]
            {
                "android.permission.BLUETOOTH_SCAN",
                "android.permission.BLUETOOTH_CONNECT",
                // some vendors still require FINE_LOCATION for scan
                "android.permission.ACCESS_FINE_LOCATION"
            }, callbacks);
        }
        else
        {
            Permission.RequestUserPermissions(new[]
            {
                "android.permission.ACCESS_FINE_LOCATION"
            }, callbacks);
        }

        SetStatus("Requesting BLE permissions…");
    }

    public void StartScan()
    {
        if (bleManager == null) return;
        if (devicesText) devicesText.text = "";
        if (connectButton) connectButton.SetActive(false);
        arduinoAddress = null;
        bleManager.Call("startScan");
        SetStatus("Scanning…");
    }

    public void ConnectToArduino()
    {
        if (bleManager == null || string.IsNullOrEmpty(arduinoAddress))
        {
            SetStatus("No device selected");
            return;
        }
        bleManager.Call("connectToDevice", arduinoAddress);
        SetStatus("Connecting to " + arduinoAddress + " …");
    }

    // ---------- Callbacks from Java plugin ----------
    public void HandleDeviceFound(string name, string address)
    {
        // Binder thread → main thread
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            if (devicesText) devicesText.text += $"{name} [{address}]\n";

            // Event to subscribers
            OnDeviceFound?.Invoke(name, address);

            // Simple auto-connect filter
            if (!string.IsNullOrEmpty(name) && name.Contains("ArmIMU"))
            {
                arduinoAddress = address;
                if (connectButton) connectButton.SetActive(true);
                SetStatus("Found target: " + name);
            }
        });
    }

    bool _mtuRequested;

    public void OnStatusUpdate(string msg)
    {
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            SetStatus("Status: " + msg);
            OnStatus?.Invoke(msg);

            var low = msg.ToLowerInvariant();
            if (!_mtuRequested && (low.Contains("connected") || low.Contains("ready")))
            {
                _mtuRequested = true;
                StopScan();
                RequestMtu(185); // ή 247 αν το firmware το υποστηρίζει
            }
        });
    }

    // May arrive in chunks; here we reassemble lines by '\n' (THREAD-SAFE)
    public void OnDataReceived(string chunk)
    {
        if (string.IsNullOrEmpty(chunk)) return;

        var readyLines = new List<string>();

        lock (_lineLock)
        {
            lineBuffer.Append(chunk);

            while (true)
            {
                int nl = IndexOfNewline(lineBuffer);
                if (nl < 0) break;

                // Take line without '\n', trim '\r' if present
                string line = lineBuffer.ToString(0, nl);
                if (line.EndsWith("\r")) line = line.Substring(0, line.Length - 1);

                // Remove consumed (+1 για το '\n')
                lineBuffer.Remove(0, nl + 1);

                if (!string.IsNullOrWhiteSpace(line))
                    readyLines.Add(line);
            }

            // Protect from huge growth if no newlines
            const int MAX_BUFFER = 32 * 1024;
            if (lineBuffer.Length > MAX_BUFFER)
                lineBuffer.Remove(0, lineBuffer.Length - MAX_BUFFER / 2);
        }

        // If queue is huge, drop old lines to avoid lag
        if (_lineQueue.Count > MAX_QUEUE_SIZE)
        {
            while (_lineQueue.TryDequeue(out _)) { }
        }

        // Enqueue lines for consumption in Update()
        foreach (var line in readyLines)
            _lineQueue.Enqueue(line);
    }

    // Supports:
    //  - "id,qx,qy,qz,qw"  (XYZW)
    //  - "id,qw,qx,qy,qz"  (WXYZ)  -> αλλάζεις από το Inspector
    private void ParseLine_MainThread(string line)
    {
        var parts = line.Split(',');
        if (parts.Length != 5) return;

        // 0: id
        if (!int.TryParse(parts[0], out int id)) return;
        if (id < 0 || id > 2) return; // μόνο 0,1,2

        // 1–4: quaternion components as floats
        if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float a)) return;
        if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float b)) return;
        if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float c)) return;
        if (!float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float d)) return;

        float qx, qy, qz, qw;

        if (quaternionFormat == QuaternionFormat.XYZW)
        {
            // Arduino: id,qx,qy,qz,qw
            qx = a;
            qy = b;
            qz = c;
            qw = d;
        }
        else
        {
            // WXYZ: id,qw,qx,qy,qz
            qw = a;
            qx = b;
            qy = c;
            qz = d;
        }

        // Optional axis inversion
        if (invertX) qx = -qx;
        if (invertY) qy = -qy;
        if (invertZ) qz = -qz;

        // Unity Quaternion(x, y, z, w)
        var q = new Quaternion(qx, qy, qz, qw).normalized;

        motionManager?.UpdateSensorRotation(id, q);

        _lastDataLine = line;
    }

    // ---------- Helpers ----------
    private void SetStatus(string s)
    {
        if (statusText) statusText.text = s;
        else UnityEngine.Debug.Log("[BLE] " + s);
    }

    private AndroidJavaObject GetUnityActivity()
    {
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
    }

    // ---------- Compatibility API ----------
    public void StopScan()
    {
        if (bleManager == null) { SetStatus("StopScan: bleManager is null"); return; }
        try { bleManager.Call("stopScan"); SetStatus("Scan stopped"); }
        catch (System.Exception e) { SetStatus("StopScan error: " + e.Message); }
    }

    public void RequestMtu(int mtu)
    {
        if (bleManager == null) { SetStatus("RequestMtu: bleManager is null"); return; }
        try { bleManager.Call("requestMtu", mtu); SetStatus("Requested MTU " + mtu); }
        catch (System.Exception e) { SetStatus("RequestMtu error: " + e.Message); }
    }

    public void Connect(string address)
    {
        if (bleManager == null) { SetStatus("Connect: bleManager is null"); return; }

        string addr = string.IsNullOrWhiteSpace(address) ? arduinoAddress : address.Trim();
        if (string.IsNullOrEmpty(addr)) { SetStatus("Connect: empty address"); return; }

        try { bleManager.Call("connectToDevice", addr); SetStatus("Connecting to " + addr + " …"); }
        catch (System.Exception e) { SetStatus("Connect error: " + e.Message); }
    }

    public void Disconnect()
    {
        if (bleManager == null) { SetStatus("Disconnect: bleManager is null"); return; }
        try { bleManager.Call("disconnect"); SetStatus("Disconnect requested"); }
        catch (System.Exception e) { SetStatus("Disconnect error: " + e.Message); }
    }

    public void Write(string ascii)
    {
        if (bleManager == null) { SetStatus("Write: bleManager is null"); return; }
        if (string.IsNullOrEmpty(ascii)) return;
        try { bleManager.Call<bool>("writeAscii", ascii); }
        catch (System.Exception e) { SetStatus("Write error: " + e.Message); }
    }

    // ---------- Convenience for UI buttons ----------

    // Sends "reset" to Arduino (resets Madgwick; you can also add gyro bias there)
    public void ResetImus()
    {
        Write("reset\n");
        SetStatus("Sent 'reset' to IMUs");
    }

    // Calibration button: use current pose as reference
    public void CalibrateArmToCurrentPose()
    {
        if (motionManager != null)
        {
            motionManager.CalibrateToCurrentPose();
            SetStatus("Calibration requested");
        }
        else
        {
            SetStatus("Calibration failed: MotionManager not assigned");
        }
    }
}

// --- BLE callback proxy ---
class BleCallbackProxy : AndroidJavaProxy
{
    private readonly BleBridge bridge;
    public BleCallbackProxy(BleBridge b) : base("com.mbrats01.ble_manager.BleCallback")
    {
        bridge = b;
    }

    // Signatures must match the Java interface
    public void onStatusUpdate(string message) => bridge.OnStatusUpdate(message);
    public void onDeviceFound(string name, string addr) => bridge.HandleDeviceFound(name, addr);
    public void onDataReceived(string data) => bridge.OnDataReceived(data);

    public void onDataReceivedBytes(byte[] bytes)
    {
        // If you ever want binary packets instead of text:
        // var text = System.Text.Encoding.UTF8.GetString(bytes);
        // bridge.OnDataReceived(text);
    }
}

using UnityEngine;
using UnityEngine.Android;
using System.Globalization;
using TMPro;
using System.Text;
using System.Diagnostics;

public class BleBridge : MonoBehaviour
{
    // ---------- Singleton ----------
    public static BleBridge Instance { get; private set; }

    const int MAX_QUEUE_SIZE = 5000;

    // ---------- Public events (για ImuVisualizer / άλλα scripts) ----------
    public event System.Action<string, string> OnDeviceFound; // (name, address)
    public event System.Action<string> OnStatus;              // status messages
    public event System.Action<string> OnLine;                // raw line: "id,roll,pitch,yaw" ή "id,qx,qy,qz,qw"

    [Header("UI")]
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI devicesText;
    public TextMeshProUGUI dataText;
    public GameObject connectButton; // optional
    public MotionManager motionManager; // διαχειρίζεται τα 3 GameObjects

    private AndroidJavaObject bleManager;
    private string arduinoAddress;

    // Reassembly buffer για BLE κείμενα (σπαστές γραμμές)
    private readonly StringBuilder lineBuffer = new StringBuilder(4096);
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lineQueue =
        new System.Collections.Concurrent.ConcurrentQueue<string>();
    private readonly object _lineLock = new object();

    // Batch/Throttle
    [Header("Perf")]
    public bool showLiveDataText = false;
    public int maxLinesPerFrame = 60;
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
        if (Instance != null) { Destroy(gameObject); return; }
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
        // Κατανάλωσε γραμμές σε batches για να μην “πνίγεται” το main thread
        int processed = 0;
        while (processed < maxLinesPerFrame && _lineQueue.TryDequeue(out var line))
        {
            // 1) δώσε raw line σε listeners (π.χ. ImuVisualizer)
            OnLine?.Invoke(line);

            // 2) εφάρμοσε στο scene
            ParseLine_MainThread(line);

            // 3) κράτα τελευταία για εμφάνιση
            _lastDataLine = line;
            processed++;
        }

        // Throttle UI (π.χ. 10Hz)
        if (dataText && Time.unscaledTime >= _nextUiUpdateTime)
        {
            dataText.text = _lastDataLine ?? "";
            _nextUiUpdateTime = Time.unscaledTime + 0.1f;
        }
    }

    // ---- UI Buttons ----
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
                // μερικοί vendors ακόμη ζητούν FINE_LOCATION για scan
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

    // ---- Callbacks από το Java plugin ----
    public void HandleDeviceFound(string name, string address)
    {
        // Binder thread → γύρνα main thread
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            if (devicesText) devicesText.text += $"{name} [{address}]\n";

            // Event προς subscribers
            OnDeviceFound?.Invoke(name, address);

            // Simple filter για auto-connect UI
            if (!string.IsNullOrEmpty(name) && name.Contains("ArmIMU"))
            {
                arduinoAddress = address;
                if (connectButton) connectButton.SetActive(true);
                SetStatus("Found target: " + name);
            }
        });
    }

    public void OnStatusUpdate(string msg)
    {
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            SetStatus("Status: " + msg);
            OnStatus?.Invoke(msg);
        });
    }

    // μπορεί να έρθουν chunks. Εδώ κάνουμε reassembly ανά γραμμή \n (THREAD-SAFE)
    public void OnDataReceived(string chunk)
    {
        if (string.IsNullOrEmpty(chunk)) return;

        // 1) Μαζεύουμε τις πλήρεις γραμμές με lock (αποφυγή races)
        System.Collections.Generic.List<string> readyLines = new System.Collections.Generic.List<string>();

        lock (_lineLock)
        {
            lineBuffer.Append(chunk);

            while (true)
            {
                int nl = IndexOfNewline(lineBuffer);
                if (nl < 0) break;

                // Πάρε την πλήρη γραμμή (χωρίς '\n'), trim '\r' αν υπάρχει
                string line = lineBuffer.ToString(0, nl);
                if (line.EndsWith("\r")) line = line.Substring(0, line.Length - 1);

                // Αφαίρεσε ό,τι κατανάλωσες (+1 για το '\n')
                lineBuffer.Remove(0, nl + 1);

                if (!string.IsNullOrWhiteSpace(line))
                    readyLines.Add(line);
            }

            // Προστασία από υπερβολικό growth αν δεν έρχονται newlines
            const int MAX_BUFFER = 32 * 1024;
            if (lineBuffer.Length > MAX_BUFFER)
                lineBuffer.Remove(0, lineBuffer.Length - MAX_BUFFER / 2);
        }

        // 2) Αν η ουρά έχει φουσκώσει, πέτα παλιές γραμμές για να μη γίνει lag
        if (_lineQueue.Count > MAX_QUEUE_SIZE)
        {
            while (_lineQueue.TryDequeue(out _)) { }
        }

        // 3) Enqueue τις πλήρεις γραμμές — θα καταναλωθούν batch στο Update()
        foreach (var line in readyLines)
            _lineQueue.Enqueue(line);
    }


    // Υποστηρίζουμε:
    //  - "id,qx,qy,qz,qw"  (quaternion)
    //  - "id,roll,pitch,yaw"  (μοίρες)
    private void ParseLine_MainThread(string line)
    {
        var parts = line.Split(',');
        if (parts.Length < 4) return;
        if (!int.TryParse(parts[0], out int id)) return;

        // Προσπάθεια ως quaternion
        if (parts.Length >= 5 &&
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float qx) &&
            float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float qy) &&
            float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float qz) &&
            float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float qw))
        {
            var q = new Quaternion(qx, qy, qz, qw);
            motionManager?.UpdateSensorRotation(id, q);
            return;
        }

        // Αλλιώς, προσπάθεια ως Euler (μοίρες)
        if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float roll) &&
            float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float pitch) &&
            float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float yaw))
        {
            var q = Quaternion.Euler(roll, pitch, yaw); // Unity παίρνει deg
            motionManager?.UpdateSensorRotation(id, q);
        }
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

    // ---------- Compatibility API για άλλα scripts ----------
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
}

// --- BLE callback proxy ---
class BleCallbackProxy : AndroidJavaProxy
{
    private readonly BleBridge bridge;
    public BleCallbackProxy(BleBridge b) : base("com.mbrats01.ble_manager.BleCallback") { bridge = b; }

    // Υπογραφές να ταιριάζουν 1:1 με το Java interface
    public void onStatusUpdate(string message) => bridge.OnStatusUpdate(message);
    public void onDeviceFound(string name, string addr) => bridge.HandleDeviceFound(name, addr);
    public void onDataReceived(string data) => bridge.OnDataReceived(data);

    public void onDataReceivedBytes(byte[] bytes)
    {
        // Αν το χρειαστείς αργότερα:
        // var text = System.Text.Encoding.UTF8.GetString(bytes);
        // bridge.OnDataReceived(text);
    }
}
    
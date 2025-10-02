using UnityEngine;
using UnityEngine.Android;
using TMPro;

public class JavaBridge : MonoBehaviour
{
    // --- UI Στοιχεία ---
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI foundDevicesText;
    public TextMeshProUGUI perm; // Για το feedback των αδειών
    public TextMeshProUGUI text;
    public GameObject connectButton;

    // --- Εσωτερικές Μεταβλητές ---
    private AndroidJavaObject bleManager;
    private string arduinoAddress = null;

    void Start()
    {
        // Αρχικοποίηση UI
        connectButton.SetActive(false);
        statusText.text = "Bridge Initialized. Ready.";
        foundDevicesText.text = "Devices";
        perm.text = "";

        // Αρχικοποίηση του Java Plugin
        using (AndroidJavaClass managerClass = new AndroidJavaClass("com.mbrats01.ble_manager.BleManager"))
        {
            bleManager = managerClass.CallStatic<AndroidJavaObject>("getInstance", GetUnityActivity());
        }
        BleCallbackProxy callbackProxy = new BleCallbackProxy(this);
        bleManager.Call("setCallback", callbackProxy);
    }

    // --- Κύριες Λειτουργίες (για τα κουμπιά) ---

    // Ζητάει τις σωστές άδειες ανάλογα με την έκδοση του Android
    public void RequestBlePermissions()
    {
        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += (permissionName) => { PrintAllPermissionStates(); };
        callbacks.PermissionDenied += (permissionName) => { PrintAllPermissionStates(); };
        callbacks.PermissionDeniedAndDontAskAgain += (permissionName) => { PrintAllPermissionStates(); };

        int sdkInt = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");

        if (sdkInt >= 31) // Android 12+
        {
            Permission.RequestUserPermissions(new string[] {
                "android.permission.BLUETOOTH_SCAN",
                "android.permission.BLUETOOTH_CONNECT",
                "android.permission.ACCESS_COARSE_LOCATION",
                "android.permission.ACCESS_FINE_LOCATION" // Η ΝΕΑ ΠΡΟΣΘΗΚΗ
            }, callbacks);
        }
        else
        {
            Permission.RequestUserPermissions(new string[]
            {
                "android.permission.ACCESS_FINE_LOCATION"
            }, callbacks);
        }
    }

    // Ξεκινά τη σάρωση για συσκευές
    public void StartBleScan()
{
    // Reset UI
    foundDevicesText.text = "";
    arduinoAddress = null;
    connectButton.SetActive(false);

    // Κλήση στο Java plugin
    bleManager.Call("startScan");
}


    // Συνδέεται στο Arduino όταν πατηθεί το κουμπί "Connect"
    public void ConnectToArduino()
    {
        if (!string.IsNullOrEmpty(arduinoAddress))
        {
            bleManager.Call("connectToDevice", arduinoAddress);
        }
        else
        {
            statusText.text = "No Arduino device selected.";
        }
    }

    // --- Callbacks από Java ---
    public void OnStatusUpdate(string message)
    {
        UnityMainThreadDispatcher.Instance().Enqueue(() => {
            statusText.text = "Status: " + message;
        });
    }

    public void OnDeviceFound(string name, string address)
    {
        UnityMainThreadDispatcher.Instance().Enqueue(() => {
            foundDevicesText.text += $"{name} ({address})\n";
            if (name.Contains("ArduinoBLE_Test"))
            {
                arduinoAddress = address;
                connectButton.SetActive(true);
            }
        });
    }

    public void OnDataReceived(string message)
    {
        UnityMainThreadDispatcher.Instance().Enqueue(() => {
            text.text += $"<color=green>Received: {message}</color>\n";
        });
    }

    // --- Βοηθητικές Μέθοδοι ---
    private void PrintAllPermissionStates()
    {
        string scanStatus = "BLUETOOTH_SCAN: " + Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_SCAN");
        string connectStatus = "BLUETOOTH_CONNECT: " + Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_CONNECT");
        string coarseLocationStatus = "ACCESS_COARSE_LOCATION: " + Permission.HasUserAuthorizedPermission("android.permission.ACCESS_COARSE_LOCATION");
        string locationStatus = "ACCESS_FINE_LOCATION: " + Permission.HasUserAuthorizedPermission("android.permission.ACCESS_FINE_LOCATION");

        perm.text = $"{scanStatus}\n{connectStatus}\n{locationStatus}\n{coarseLocationStatus}";
    }

    private AndroidJavaObject GetUnityActivity()
    {
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        }
    }
}

// --- Proxy που μεταφράζει τις κλήσεις από Java σε C# ---
class BleCallbackProxy : AndroidJavaProxy
{
    private JavaBridge bridge;

    public BleCallbackProxy(JavaBridge bridge) : base("com.mbrats01.ble_manager.BleCallback")
    {
        this.bridge = bridge;
    }

    public void onStatusUpdate(string message) { bridge.OnStatusUpdate(message); }
    public void onDeviceFound(string name, string address) { bridge.OnDeviceFound(name, address); }
    public void onDataReceived(string message) { bridge.OnDataReceived(message); }
}

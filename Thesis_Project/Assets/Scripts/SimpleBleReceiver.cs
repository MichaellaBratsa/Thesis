using System.Diagnostics;
using UnityEngine;
using UnityEngine.Android;

public class SimpleBleReceiver : MonoBehaviour
{
    public static SimpleBleReceiver Instance;

    // Event that other scripts can subscribe to in order to receive incoming BLE data packets.
    public System.Action<string> OnPacketReceived;

    // The (partial) BLE device name we are looking for when scanning.
    public string DeviceName = "ArmTracker";

    // Reference to the Java-side BLE manager (Custom Android plugin).
    private AndroidJavaObject bleManager;

    private string targetAddress;

    // Tracks whether we are currently scanning to avoid repeated connect attempts.
    private bool isScanning = false;

    void Awake()
    {
        // Assign the singleton instance.
        Instance = this;

        // Keep this object alive across scene changes.
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // Request runtime permissions (especially needed on Android 12+ for BLE scan/connect).
        if (!Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_SCAN") ||
            !Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_CONNECT"))
        {
            // Request BLE scan permission.
            Permission.RequestUserPermission("android.permission.BLUETOOTH_SCAN");

            // Request BLE connect permission.
            Permission.RequestUserPermission("android.permission.BLUETOOTH_CONNECT");

            // Request location permission (often required for BLE scanning on older Android versions / some devices).
            Permission.RequestUserPermission("android.permission.ACCESS_FINE_LOCATION");
        }

        // Initialize the Android BLE plugin bridge.
        InitializePlugin();
    }

    void InitializePlugin()
    {
        try
        {
            // Load the Java BleManager class from the Android plugin.
            using (var managerClass = new AndroidJavaClass("com.mbrats01.ble_manager.BleManager"))
            {
                // Get singleton instance from Java side and pass Unity Activity.
                bleManager = managerClass.CallStatic<AndroidJavaObject>("getInstance", GetActivity());

                // Set callback proxy so Java plugin can call back into Unity.
                bleManager.Call("setCallback", new BleCallback(this));
            }
        } 
        catch { UnityEngine.Debug.LogError("BLE Plugin not found (Are you in Editor?)"); }
    }

    public void StartScan()
    {
        // Only start scanning if the Java BLE manager is available.
        if (bleManager != null)
        {
            // Mark scan as active.
            isScanning = true;

            // Call into Java plugin to start BLE scan.
            bleManager.Call("startScan");
        }
    }

    // Called by the Android plugin when a BLE device is found.
    public void OnDeviceFound(string name, string address)
    {
        // If device name matches and we are currently scanning, stop scanning and connect.
        if (name.Contains(DeviceName) && isScanning)
        {
            // Stop further scan handling.
            isScanning = false;

            // Store found device address.
            targetAddress = address;

            // Stop scan on Java side.
            bleManager.Call("stopScan");

            // Connect to the matched device.
            bleManager.Call("connectToDevice", address);
        }
    }

    // Called by the Android plugin when connection/status updates happen.
    public void OnStatus(string status)
    {
        // When connected, request a larger MTU for faster / bigger packets.
        if (status.ToLower().Contains("connected"))
        {
            // Request MTU 185 (common value for improved throughput).
            bleManager.Call("requestMtu", 185);
        }
    }

    // Called by the Android plugin when data is received.
    public void OnData(string data)
    {
        // If data is valid, forward it to listeners via the event.
        if (!string.IsNullOrEmpty(data)) OnPacketReceived?.Invoke(data);
    }

    AndroidJavaObject GetActivity()
    {
        // Get Unity's current Android Activity so the plugin can use it.
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
    }

    // Internal callback proxy class that implements the Java callback interface.
    class BleCallback : AndroidJavaProxy
    {
        // Reference back to the owning SimpleBleReceiver instance.
        SimpleBleReceiver owner;

        // Create proxy and specify the fully-qualified Java interface name.
        public BleCallback(SimpleBleReceiver o) : base("com.mbrats01.ble_manager.BleCallback") { owner = o; }

        // Forward device found event from Java to Unity owner.
        public void onDeviceFound(string n, string a) => owner.OnDeviceFound(n, a);

        // Forward status update event from Java to Unity owner.
        public void onStatusUpdate(string s) => owner.OnStatus(s);

        // Forward incoming string data from Java to Unity owner.
        public void onDataReceived(string d) => owner.OnData(d);

        // Optional bytes callback (unused here).
        public void onDataReceivedBytes(sbyte[] d) { }
    }
}

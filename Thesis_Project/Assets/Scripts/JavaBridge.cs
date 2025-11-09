//using UnityEngine;
//using UnityEngine.Android;
//using System.Globalization;
//using TMPro;
//using System.Text;

//public class JavaBridge : MonoBehaviour
//{
//    public TextMeshProUGUI statusText;
//    public TextMeshProUGUI devicesText;
//    public TextMeshProUGUI quaternionText;
//    public GameObject connectButton;
//    public MotionManager motionManager; // Θα διαχειρίζεται τα 3 GameObjects

//    private AndroidJavaObject bleManager;
//    private string arduinoAddress;

//    void Start()
//    {
//        connectButton.SetActive(false);
//        statusText.text = "Ready for BLE connection";

//        using (AndroidJavaClass managerClass = new AndroidJavaClass("com.mbrats01.ble_manager.BleManager"))
//        {
//            bleManager = managerClass.CallStatic<AndroidJavaObject>("getInstance", GetUnityActivity());
//        }

//        if (bleManager != null)
//        {
//            bleManager.Call("setCallback", new BleCallbackProxy(this));
//            statusText.text = "BLE manager initialized";
//        }
//        else
//        {
//            statusText.text = "BLE manager not found!";
//        }
//    }

//    public void RequestBlePermissions()
//    {
//        var callbacks = new PermissionCallbacks();
//        int sdk = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");

//        if (sdk >= 31)
//        {
//            Permission.RequestUserPermissions(new string[] {
//                "android.permission.BLUETOOTH_SCAN",
//                "android.permission.BLUETOOTH_CONNECT",
//                "android.permission.ACCESS_FINE_LOCATION"
//            }, callbacks);
//        }
//        else
//        {
//            Permission.RequestUserPermissions(new string[] {
//                "android.permission.ACCESS_FINE_LOCATION"
//            }, callbacks);
//        }
//    }

//    public void StartScan()
//    {
//        if (bleManager == null) return;
//        devicesText.text = "";
//        connectButton.SetActive(false);
//        bleManager.Call("startScan");
//    }

//    public void ConnectToArduino()
//    {
//        if (bleManager == null || string.IsNullOrEmpty(arduinoAddress)) return;
//        bleManager.Call("connectToDevice", arduinoAddress);
//    }

//    // --- Callbacks ---
//    public void OnDeviceFound(string name, string address)
//    {
//        UnityMainThreadDispatcher.Instance().Enqueue(() =>
//        {
//            devicesText.text += $"{name}\n";
//            if (name != null && name.Contains("ArmIMU"))
//            {
//                arduinoAddress = address;
//                connectButton.SetActive(true);
//            }
//        });
//    }

//    public void OnStatusUpdate(string msg)
//    {
//        UnityMainThreadDispatcher.Instance().Enqueue(() =>
//        {
//            statusText.text = "Status: " + msg;
//        });
//    }

//    public void OnDataReceived(string data)
//    {
//        if (string.IsNullOrEmpty(data)) return;
//        string[] parts = data.Split(',');
//        if (parts.Length < 5) return;

//        int id = int.Parse(parts[0]);
//        float qx = float.Parse(parts[1], CultureInfo.InvariantCulture);
//        float qy = float.Parse(parts[2], CultureInfo.InvariantCulture);
//        float qz = float.Parse(parts[3], CultureInfo.InvariantCulture);
//        float qw = float.Parse(parts[4], CultureInfo.InvariantCulture);

//        Quaternion q = new Quaternion(qx, qy, qz, qw);

//        motionManager?.UpdateSensorRotation(id, q);

//        quaternionText.text = $"IMU {id}: {qx:F2}, {qy:F2}, {qz:F2}, {qw:F2}";
//    }

//    private AndroidJavaObject GetUnityActivity()
//    {
//        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
//            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
//    }
//}

//// --- BLE callback proxy ---
//class BleCallbackProxy : AndroidJavaProxy
//{
//    private JavaBridge bridge;
//    public BleCallbackProxy(JavaBridge b) : base("com.mbrats01.ble_manager.BleCallback") { bridge = b; }

//    public void onStatusUpdate(string message) => bridge.OnStatusUpdate(message);
//    public void onDeviceFound(string name, string addr) => bridge.OnDeviceFound(name, addr);
//    public void onDataReceived(string data) => bridge.OnDataReceived(data);
//}

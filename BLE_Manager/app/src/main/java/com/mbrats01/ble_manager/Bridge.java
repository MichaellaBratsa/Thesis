//package com.mbrats01.ble_manager;
//
//import android.bluetooth.BluetoothAdapter;
//import android.bluetooth.le.BluetoothLeScanner;
//import android.bluetooth.le.ScanCallback;
//import android.bluetooth.le.ScanResult;
//import android.content.Context;
//import android.util.Log;
//
//import com.unity3d.player.UnityPlayer;
//
//public class Bridge {
//    private static Bridge _instance;
//    private Context context;
//    private BluetoothAdapter bluetoothAdapter;
//    private BluetoothLeScanner scanner;
//
//    // Το όνομα του GameObject στο Unity που θα λαμβάνει τα μηνύματα
//    private final String UNITY_GAME_OBJECT_NAME = "BridgeManager";
//
//    public static Bridge getInstance() {
//        if (_instance == null) {
//            _instance = new Bridge();
//        }
//        return _instance;
//    }
//
//    // Μια νέα μέθοδος για να αρχικοποιήσουμε τη γέφυρα από το Unity
//    public void initializeBridge(Context ctx) {
//        this.context = ctx;
//        Log.d("BleBridge", "Bridge Initialized!");
//    }
//
//    // Η μέθοδος που θα καλεί το Unity για να ξεκινήσει τη σάρωση
//    public void startScan() {
//        // Παίρνουμε τον Bluetooth Adapter της συσκευής
//        bluetoothAdapter = BluetoothAdapter.getDefaultAdapter();
//        if (bluetoothAdapter == null || !bluetoothAdapter.isEnabled()) {
//            Log.e("BleBridge", "Bluetooth is not enabled or not available.");
//            sendToUnity("OnBleError", "Bluetooth is not enabled or not available.");
//            return;
//        }
//
//        // Παίρνουμε τον scanner
//        scanner = bluetoothAdapter.getBluetoothLeScanner();
//        if (scanner == null) {
//            Log.e("BleBridge", "Failed to get BLE scanner.");
//            sendToUnity("OnBleError", "Failed to get BLE scanner.");
//            return;
//        }
//
//        Log.d("BleBridge", "Starting BLE Scan...");
//        sendToUnity("OnBleStatusUpdate", "Scanning for devices...");
//        scanner.startScan(leScanCallback);
//    }
//
//    // Αυτή η μέθοδος θα σταματάει τη σάρωση
//    public void stopScan() {
//        if (scanner != null) {
//            Log.d("BleBridge", "Stopping BLE Scan.");
//            scanner.stopScan(leScanCallback);
//        }
//    }
//
//    // --- Ο Μηχανισμός Επιστροφής Δεδομένων στο Unity ---
//    // Αυτή η μέθοδος είναι το "τηλέφωνο" μας προς το Unity.
//    private void sendToUnity(String methodName, String message) {
//        // Καλούμε μια μέθοδο σε ένα συγκεκριμένο GameObject στο Unity
//        UnityPlayer.UnitySendMessage(UNITY_GAME_OBJECT_NAME, methodName, message);
//    }
//
//    // --- Το Callback για τη Σάρωση ---
//    // Αυτό είναι το πιο σημαντικό κομμάτι. Αυτός ο κώδικας εκτελείται
//    // αυτόματα κάθε φορά που ο scanner βρίσκει μια BLE συσκευή.
//    private final ScanCallback leScanCallback = new ScanCallback() {
//        @Override
//        public void onScanResult(int callbackType, ScanResult result) {
//            super.onScanResult(callbackType, result);
//
//            // Παίρνουμε το όνομα της συσκευής που βρέθηκε
//            String deviceName = result.getDevice().getName();
//            String deviceAddress = result.getDevice().getAddress();
//
//            // Αν το όνομα δεν είναι κενό, το στέλνουμε στο Unity
//            if (deviceName != null && !deviceName.isEmpty()) {
//                Log.d("BleBridge", "Found device: " + deviceName + " with address: " + deviceAddress);
//                // Στέλνουμε στο Unity το όνομα και τη διεύθυνση, χωρισμένα με ένα |
//                sendToUnity("OnDeviceFound", deviceName + "|" + deviceAddress);
//            }
//        }
//
//        @Override
//        public void onScanFailed(int errorCode) {
//            super.onScanFailed(errorCode);
//            Log.e("BleBridge", "Scan Failed with error code: " + errorCode);
//            sendToUnity("OnBleError", "Scan Failed with error code: " + errorCode);
//        }
//    };
//}
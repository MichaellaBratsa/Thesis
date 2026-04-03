package com.mbrats01.ble_manager;

import android.Manifest;
import android.annotation.SuppressLint;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothGatt;
import android.bluetooth.BluetoothGattCallback;
import android.bluetooth.BluetoothGattCharacteristic;
import android.bluetooth.BluetoothGattDescriptor;
import android.bluetooth.BluetoothGattService;
import android.bluetooth.le.BluetoothLeScanner;
import android.bluetooth.le.ScanCallback;
import android.bluetooth.le.ScanResult;
import android.content.Context;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import androidx.core.app.ActivityCompat;

import java.nio.charset.StandardCharsets;
import java.util.UUID;

public class BleManager {
    private static final String TAG = "BleManager";

    private static BleManager instance;
    private Context context;
    private BleCallback unityCallback;

    private BluetoothAdapter bluetoothAdapter;
    private BluetoothLeScanner scanner;
    private boolean isScanning = false;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private BluetoothGatt bluetoothGatt;

    private BluetoothGattCharacteristic txCharacteristic; // notify (Arduino→Unity)
    private BluetoothGattCharacteristic rxCharacteristic; // write  (Unity→Arduino)

    // UART-like UUIDs
    private static final UUID UART_SERVICE_UUID            = UUID.fromString("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    private static final UUID UART_TX_CHARACTERISTIC_UUID  = UUID.fromString("6E400003-B5A3-F393-E0A9-E50E24DCCA9E");
    private static final UUID UART_RX_CHARACTERISTIC_UUID  = UUID.fromString("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");
    private static final UUID CLIENT_CHARACTERISTIC_CONFIG_UUID = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb");

    private BleManager(Context context) {
        this.context = context.getApplicationContext();
        this.bluetoothAdapter = BluetoothAdapter.getDefaultAdapter();
    }

    public static synchronized BleManager getInstance(Context context) {
        if (instance == null) {
            instance = new BleManager(context.getApplicationContext());
        }
        instance.context = context.getApplicationContext();
        return instance;
    }

    public void setCallback(BleCallback callback) {
        this.unityCallback = callback;
    }

    // ---------- Permission helpers ----------
    private boolean hasScanPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            return context.checkSelfPermission(Manifest.permission.BLUETOOTH_SCAN) == PackageManager.PERMISSION_GRANTED;
        } else {
            return context.checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED;
        }
    }

    private boolean hasConnectPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            return context.checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED;
        } else {
            return true; // no dedicated CONNECT perm before Android 12
        }
    }

    // ---------- Public API ----------

    /** Έναρξη σάρωσης */
    public void startScan() {
        Log.d(TAG, "startScan called.");

        if (bluetoothAdapter == null || !bluetoothAdapter.isEnabled()) {
            if (unityCallback != null) unityCallback.onStatusUpdate("Bluetooth is not enabled");
            Log.e(TAG, "Bluetooth not enabled.");
            return;
        }

        if (!hasScanPermission()) {
            if (unityCallback != null) unityCallback.onStatusUpdate("Scan permission NOT granted.");
            Log.e(TAG, "Scan permission missing.");
            return;
        }

        try {
            scanner = bluetoothAdapter.getBluetoothLeScanner();
            if (scanner == null) {
                if (unityCallback != null) unityCallback.onStatusUpdate("BLE scanner not available.");
                return;
            }
        scanner = bluetoothAdapter.getBluetoothLeScanner();
        handler.postDelayed(this::stopScan, 10000); // 10 sec
        isScanning = true;

            handler.postDelayed(this::stopScan, 10_000); // 10 sec timeout
            isScanning = true;

            java.util.List<android.bluetooth.le.ScanFilter> filters = new java.util.ArrayList<>();
            android.bluetooth.le.ScanFilter uartFilter = new android.bluetooth.le.ScanFilter.Builder()
                    .setServiceUuid(new android.os.ParcelUuid(UART_SERVICE_UUID))
                    .build();
            filters.add(uartFilter);

            android.bluetooth.le.ScanSettings settings = new android.bluetooth.le.ScanSettings.Builder()
                    .setScanMode(android.bluetooth.le.ScanSettings.SCAN_MODE_LOW_LATENCY)
                    .build();

            if (ActivityCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_SCAN) != PackageManager.PERMISSION_GRANTED
                    && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                if (unityCallback != null) unityCallback.onStatusUpdate("BLUETOOTH_SCAN permission not granted.");
                return;
            }
            scanner.startScan(filters, settings, scanCallback);
            if (unityCallback != null) unityCallback.onStatusUpdate("Scanning for Arduino...");
        } catch (SecurityException se) {
            Log.e(TAG, "startScan SecurityException", se);
            if (unityCallback != null) unityCallback.onStatusUpdate("SecurityException on startScan: missing permission?");
        }
    }

    public void stopScan() {
        if (!isScanning || scanner == null) return;
        if (!hasScanPermission()) {
            if (unityCallback != null) unityCallback.onStatusUpdate("StopScan: permission not granted.");
            return;
        }
        try {
            if (ActivityCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_SCAN) != PackageManager.PERMISSION_GRANTED
                    && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                if (unityCallback != null) unityCallback.onStatusUpdate("BLUETOOTH_SCAN permission not granted (stopScan).");
                return;
            }
            scanner.stopScan(scanCallback);
            isScanning = false;
            if (unityCallback != null) unityCallback.onStatusUpdate("Scan stopped.");
            Log.d(TAG, "Scan stopped.");
        } catch (SecurityException se) {
            Log.e(TAG, "stopScan SecurityException", se);
            if (unityCallback != null) unityCallback.onStatusUpdate("SecurityException on stopScan.");
        }
    }

    public void connectToDevice(String address) {
        if (!hasConnectPermission()) {
            if (unityCallback != null) unityCallback.onStatusUpdate("Bluetooth CONNECT permission not granted.");
            return;
        }

        try {
            BluetoothDevice device = bluetoothAdapter.getRemoteDevice(address);
            if (device == null) {
                if (unityCallback != null) unityCallback.onStatusUpdate("Device not found.");
                return;
            }
            if (unityCallback != null) unityCallback.onStatusUpdate("Connecting to " + address);

            if (ActivityCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED
                    && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                if (unityCallback != null) unityCallback.onStatusUpdate("BLUETOOTH_CONNECT permission not granted (connectGatt).");
                return;
            }
            bluetoothGatt = device.connectGatt(context, false, gattCallback);
        } catch (IllegalArgumentException iae) {
            Log.e(TAG, "connectToDevice: invalid address " + address, iae);
            if (unityCallback != null) unityCallback.onStatusUpdate("Invalid device address.");
        } catch (SecurityException se) {
            Log.e(TAG, "connectToDevice SecurityException", se);
            if (unityCallback != null) unityCallback.onStatusUpdate("SecurityException on connect: missing permission?");
        }
    }

    public void disconnect() {
        try {
            if (bluetoothGatt != null) {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S &&
                        ActivityCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED) {
                    if (unityCallback != null) unityCallback.onStatusUpdate("BLUETOOTH_CONNECT permission not granted (disconnect).");
                    return;
                }
                bluetoothGatt.disconnect();
                bluetoothGatt.close();
                bluetoothGatt = null;
                if (unityCallback != null) unityCallback.onStatusUpdate("Disconnected and resources released.");
                Log.d(TAG, "BluetoothGatt closed.");
            }
        } catch (SecurityException se) {
            Log.e(TAG, "disconnect SecurityException", se);
            if (unityCallback != null) unityCallback.onStatusUpdate("SecurityException on disconnect.");
        }
    }

    /** Ζήτα MTU (π.χ. 185/247/517) */
    public void requestMtu(int mtu) {
        if (bluetoothGatt == null) return;
        if (!hasConnectPermission()) {
            if (unityCallback != null) unityCallback.onStatusUpdate("requestMtu: CONNECT permission not granted.");
            return;
        }
        try {
            bluetoothGatt.requestMtu(mtu);
        } catch (SecurityException se) {
            Log.e(TAG, "requestMtu SecurityException", se);
            if (unityCallback != null) unityCallback.onStatusUpdate("SecurityException on requestMtu.");
        }
    }

    /** Γράψε ASCII (εντολές τύπου "CAL\n") */
    public boolean writeAscii(String text) {
        return writeBinary(text.getBytes(StandardCharsets.UTF_8), false);
    }

    /** Γράψε δυαδικά (π.χ. config/bytes προς RX) */
    public boolean writeBinary(byte[] data, boolean noResponse) {
        if (bluetoothGatt == null || rxCharacteristic == null) return false;
        if (!hasConnectPermission()) {
            if (unityCallback != null) unityCallback.onStatusUpdate("writeBinary: CONNECT permission not granted.");
            return false;
        }
        try {
            rxCharacteristic.setWriteType(noResponse
                    ? BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE
                    : BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT);
            rxCharacteristic.setValue(data);
            return bluetoothGatt.writeCharacteristic(rxCharacteristic);
        } catch (SecurityException se) {
            Log.e(TAG, "writeBinary SecurityException", se);
            if (unityCallback != null) unityCallback.onStatusUpdate("SecurityException on writeBinary.");
            return false;
        }
    }

    // ---------- Scan callback ----------
    private boolean hasScanPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            return context.checkSelfPermission(Manifest.permission.BLUETOOTH_SCAN) == PackageManager.PERMISSION_GRANTED;
        } else {
            return context.checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED;
        }
    }

    private boolean hasConnectPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            return context.checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED;
        } else {
            return true;
        }
    }

    /** Callback scan */
    private final ScanCallback scanCallback = new ScanCallback() {
        @Override
        public void onScanResult(int callbackType, ScanResult result) {
            super.onScanResult(callbackType, result);

            BluetoothDevice device = result.getDevice();
            String deviceName = null;

            if (result.getScanRecord() != null) {
                deviceName = result.getScanRecord().getDeviceName();
            }
            if (deviceName == null || deviceName.isEmpty()) {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S &&
                        ActivityCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED) {
                    // On Android 12+, reading device name could be gated; fall back gracefully.
                    deviceName = "Unknown";
                } else {
                    deviceName = device.getName();
                    if (deviceName == null || deviceName.isEmpty()) deviceName = "Unknown";
                }
            }

            Log.d(TAG, "Device found: " + deviceName + " (" + device.getAddress() + ")");
            if (unityCallback != null) {
                unityCallback.onDeviceFound(deviceName, device.getAddress());
            }

            if (result.getScanRecord() != null) {
                byte[] raw = result.getScanRecord().getBytes();
                StringBuilder sb = new StringBuilder();
                for (byte b : raw) sb.append(String.format("%02X ", b));
                Log.d(TAG, "Raw Advertisement: " + sb.toString());
            }
        }
    };

    // ---------- GATT callback ----------
    private final BluetoothGattCallback gattCallback = new BluetoothGattCallback() {
        @Override
        public void onConnectionStateChange(BluetoothGatt gatt, int status, int newState) {
            if (!hasConnectPermission()) return;

            if (newState == BluetoothGatt.STATE_CONNECTED) {
                if (unityCallback != null) unityCallback.onStatusUpdate("Connected. Discovering services...");
                try {
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S &&
                            ActivityCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED) {
                        if (unityCallback != null) unityCallback.onStatusUpdate("CONNECT permission not granted (discoverServices).");
                        return;
                    }
                    gatt.discoverServices();
                } catch (SecurityException se) {
                    Log.e(TAG, "discoverServices SecurityException", se);
                    if (unityCallback != null) unityCallback.onStatusUpdate("SecurityException on discoverServices.");
                }
            } else if (newState == BluetoothGatt.STATE_DISCONNECTED) {
                if (unityCallback != null) unityCallback.onStatusUpdate("Disconnected.");
            }
        }


        @Override
        public void onDescriptorWrite(BluetoothGatt gatt, BluetoothGattDescriptor descriptor, int status) {
            if (CLIENT_CHARACTERISTIC_CONFIG_UUID.equals(descriptor.getUuid())) {
                if (status == BluetoothGatt.GATT_SUCCESS) {
                    unityCallbackOn("Notifications enabled (CCCD OK). Ready.");
                } else {
                    unityCallbackOn("CCCD write failed: " + status);
                }
            }
        }

        @Override
        public void onServicesDiscovered(BluetoothGatt gatt, int status) {
            if (status != BluetoothGatt.GATT_SUCCESS) return;

            BluetoothGattService service = gatt.getService(UART_SERVICE_UUID);
            if (service == null) { unityCallbackOn("UART service not found."); return; }

            txCharacteristic = service.getCharacteristic(UART_TX_CHARACTERISTIC_UUID);
            rxCharacteristic = service.getCharacteristic(UART_RX_CHARACTERISTIC_UUID);

            if (txCharacteristic == null || rxCharacteristic == null) {
                unityCallbackOn("UART characteristics missing."); return;
            }

            // --- 1) Permission check IN PLACE (Lint-friendly) ---
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S &&
                    ActivityCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_CONNECT)
                            != PackageManager.PERMISSION_GRANTED) {
                unityCallbackOn("CONNECT permission not granted (notify).");
                return;
            }

            // --- 2) setCharacteristicNotification με try/catch ---
            try {
                //noinspection MissingPermission
                boolean ok = gatt.setCharacteristicNotification(txCharacteristic, true);
                Log.d(TAG, "setCharacteristicNotification returned: " + ok);
            } catch (SecurityException se) {
                Log.e(TAG, "setCharacteristicNotification SecurityException", se);
                unityCallbackOn("SecurityException on setCharacteristicNotification.");
                return;
            }

            // --- 3) CCCD write με try/catch ---
            BluetoothGattDescriptor cccd = txCharacteristic.getDescriptor(CLIENT_CHARACTERISTIC_CONFIG_UUID);
            if (cccd != null) {
                try {
                    cccd.setValue(BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE);
                    //noinspection MissingPermission
                    gatt.writeDescriptor(cccd);
                } catch (SecurityException se) {
                    Log.e(TAG, "writeDescriptor SecurityException", se);
                    unityCallbackOn("SecurityException on writeDescriptor.");
                }
            } else {
                unityCallbackOn("CCCD not present; relying on setCharacteristicNotification only.");
                unityCallbackOn("Ready. You can request MTU and receive data.");
            }
        }



        // μικρό helper να μη γράφουμε συνέχεια null-check
        private void unityCallbackOn(String msg){
            if (unityCallback != null) unityCallback.onStatusUpdate(msg);
        }

        @Override
        public void onCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic) {
            try {
                if (UART_TX_CHARACTERISTIC_UUID.equals(characteristic.getUuid())) {
                    byte[] bytes = characteristic.getValue();
                    if (unityCallback != null) {
                        // Binary route (π.χ. PacketV1 60 bytes)
                        unityCallback.onDataReceivedBytes(bytes);
                        // Optional text route for debugging
                        try {
                            String message = new String(bytes, StandardCharsets.UTF_8);
                            unityCallback.onDataReceived(message);
                        } catch (Exception ignored) { }
                    }
                }
            } catch (SecurityException se) {
                Log.e(TAG, "onCharacteristicChanged SecurityException", se);
            }
        }

        @Override
        public void onMtuChanged(BluetoothGatt gatt, int mtu, int status) {
            if (unityCallback != null) unityCallback.onStatusUpdate("MTU changed: " + mtu + " (status " + status + ")");
        }
    };
}

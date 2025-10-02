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
    private static BleManager instance;
    private Context context;
    private BleCallback unityCallback;

    private BluetoothAdapter bluetoothAdapter;
    private BluetoothLeScanner scanner;
    private boolean isScanning = false;
    private Handler handler = new Handler(Looper.getMainLooper());
    private BluetoothGatt bluetoothGatt;

    private static final String TAG = "BleManager";

    // UUIDs for Arduino UART Service
    private static final UUID UART_SERVICE_UUID = UUID.fromString("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    private static final UUID UART_TX_CHARACTERISTIC_UUID = UUID.fromString("6E400003-B5A3-F393-E0A9-E50E24DCCA9E");
    private static final UUID UART_RX_CHARACTERISTIC_UUID = UUID.fromString("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");
    private static final UUID CLIENT_CHARACTERISTIC_CONFIG_UUID = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb");

    private BleManager(Context context) {
        this.context = context.getApplicationContext();
        this.bluetoothAdapter = BluetoothAdapter.getDefaultAdapter();
    }

    public static synchronized BleManager getInstance(Context context) {
        if (instance == null) {
            instance = new BleManager(context.getApplicationContext());
        }
        instance.context = context;
        return instance;
    }

    public void setCallback(BleCallback callback) {
        this.unityCallback = callback;
    }

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

        scanner = bluetoothAdapter.getBluetoothLeScanner();
        handler.postDelayed(this::stopScan, 10000); // 10 δευτ.
        isScanning = true;

        java.util.List<android.bluetooth.le.ScanFilter> filters = new java.util.ArrayList<>();

        android.bluetooth.le.ScanFilter uartFilter = new android.bluetooth.le.ScanFilter.Builder()
                .setServiceUuid(new android.os.ParcelUuid(UART_SERVICE_UUID))
                .build();

        filters.add(uartFilter);

        android.bluetooth.le.ScanSettings settings = new android.bluetooth.le.ScanSettings.Builder()
                .setScanMode(android.bluetooth.le.ScanSettings.SCAN_MODE_LOW_LATENCY)
                .build();

        scanner.startScan(filters, settings, scanCallback);

        if (unityCallback != null) unityCallback.onStatusUpdate("Scanning for Arduino...");
    }

    /** Σταμάτημα σάρωσης */
    public void stopScan() {
        if (isScanning && scanner != null) {
            if (hasScanPermission()) {
                if (ActivityCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_SCAN) != PackageManager.PERMISSION_GRANTED) {
                    // TODO: Consider calling
                    //    ActivityCompat#requestPermissions
                    // here to request the missing permissions, and then overriding
                    //   public void onRequestPermissionsResult(int requestCode, String[] permissions,
                    //                                          int[] grantResults)
                    // to handle the case where the user grants the permission. See the documentation
                    // for ActivityCompat#requestPermissions for more details.
                    return;
                }
                scanner.stopScan(scanCallback);
                isScanning = false;
                if (unityCallback != null) unityCallback.onStatusUpdate("Scan stopped.");
                Log.d(TAG, "Scan stopped.");
            }
        }
    }

    /** Σύνδεση σε συσκευή */
    public void connectToDevice(String address) {
        if (!hasConnectPermission()) {
            if (unityCallback != null) unityCallback.onStatusUpdate("Bluetooth Connect permission not granted.");
            return;
        }
        BluetoothDevice device = bluetoothAdapter.getRemoteDevice(address);
        if (device == null) {
            if (unityCallback != null) unityCallback.onStatusUpdate("Device not found.");
            return;
        }
        if (unityCallback != null) unityCallback.onStatusUpdate("Connecting to " + address);
        if (ActivityCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED) {
            // TODO: Consider calling
            //    ActivityCompat#requestPermissions
            // here to request the missing permissions, and then overriding
            //   public void onRequestPermissionsResult(int requestCode, String[] permissions,
            //                                          int[] grantResults)
            // to handle the case where the user grants the permission. See the documentation
            // for ActivityCompat#requestPermissions for more details.
            return;
        }
        bluetoothGatt = device.connectGatt(context, false, gattCallback);
    }

    /** Αποσύνδεση και καθαρισμός */
    public void disconnect() {
        if (bluetoothGatt != null) {
            if (ActivityCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED) {
                // TODO: Consider calling
                //    ActivityCompat#requestPermissions
                // here to request the missing permissions, and then overriding
                //   public void onRequestPermissionsResult(int requestCode, String[] permissions,
                //                                          int[] grantResults)
                // to handle the case where the user grants the permission. See the documentation
                // for ActivityCompat#requestPermissions for more details.
                return;
            }
            bluetoothGatt.disconnect();
            bluetoothGatt.close();
            bluetoothGatt = null;
            if (unityCallback != null) unityCallback.onStatusUpdate("Disconnected and resources released.");
            Log.d(TAG, "BluetoothGatt closed.");
        }
    }

    /** Έλεγχος permission για σάρωση */
    private boolean hasScanPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            return context.checkSelfPermission(Manifest.permission.BLUETOOTH_SCAN) == PackageManager.PERMISSION_GRANTED;
        } else {
            return context.checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED;
        }
    }

    /** Έλεγχος permission για σύνδεση */
    private boolean hasConnectPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            return context.checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED;
        } else {
            return true; // στα Android < 12 δεν υπήρχε ξεχωριστό CONNECT permission
        }
    }

    /** Callback σάρωσης */
    private final ScanCallback scanCallback = new ScanCallback() {
        public void onScanResult(int callbackType, ScanResult result) {
            super.onScanResult(callbackType, result);
            BluetoothDevice device = result.getDevice();

            String deviceName = null;

            if (result.getScanRecord() != null) {
                deviceName = result.getScanRecord().getDeviceName();
            }
            if (deviceName == null || deviceName.isEmpty()) {
                deviceName = device.getName();
            }
            if (deviceName == null || deviceName.isEmpty()) {
                deviceName = "Unknown";
            }

            Log.d(TAG, "Device found: " + deviceName + " (" + device.getAddress() + ")");
            if (unityCallback != null) {
                unityCallback.onDeviceFound(deviceName, device.getAddress());
            }
            if (result.getScanRecord() != null) {
                byte[] raw = result.getScanRecord().getBytes();
                StringBuilder sb = new StringBuilder();
                for (byte b : raw) {
                    sb.append(String.format("%02X ", b));
                }
                Log.d(TAG, "Raw Advertisement: " + sb.toString());
            }

        }
    };

    /** Callback GATT */
    private final BluetoothGattCallback gattCallback = new BluetoothGattCallback() {
        @Override
        public void onConnectionStateChange(BluetoothGatt gatt, int status, int newState) {
            if (!hasConnectPermission()) return;

            if (newState == BluetoothGatt.STATE_CONNECTED) {
                if (unityCallback != null) unityCallback.onStatusUpdate("Connected. Discovering services...");
                if (ActivityCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED) {
                    // TODO: Consider calling
                    //    ActivityCompat#requestPermissions
                    // here to request the missing permissions, and then overriding
                    //   public void onRequestPermissionsResult(int requestCode, String[] permissions,
                    //                                          int[] grantResults)
                    // to handle the case where the user grants the permission. See the documentation
                    // for ActivityCompat#requestPermissions for more details.
                    return;
                }
                gatt.discoverServices();
            } else if (newState == BluetoothGatt.STATE_DISCONNECTED) {
                if (unityCallback != null) unityCallback.onStatusUpdate("Disconnected.");
            }
        }

        @Override
        public void onServicesDiscovered(BluetoothGatt gatt, int status) {
            if (status == BluetoothGatt.GATT_SUCCESS) {
                BluetoothGattService service = gatt.getService(UART_SERVICE_UUID);
                if (service != null) {
                    BluetoothGattCharacteristic txCharacteristic = service.getCharacteristic(UART_TX_CHARACTERISTIC_UUID);
                    if (txCharacteristic != null && hasConnectPermission()) {
                        if (ActivityCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED) {
                            // TODO: Consider calling
                            //    ActivityCompat#requestPermissions
                            // here to request the missing permissions, and then overriding
                            //   public void onRequestPermissionsResult(int requestCode, String[] permissions,
                            //                                          int[] grantResults)
                            // to handle the case where the user grants the permission. See the documentation
                            // for ActivityCompat#requestPermissions for more details.
                            return;
                        }
                        gatt.setCharacteristicNotification(txCharacteristic, true);

                        // Ενεργοποίηση descriptor για αξιόπιστες ειδοποιήσεις
                        BluetoothGattDescriptor descriptor = txCharacteristic.getDescriptor(CLIENT_CHARACTERISTIC_CONFIG_UUID);
                        if (descriptor != null) {
                            descriptor.setValue(BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE);
                            gatt.writeDescriptor(descriptor);
                        }

                        if (unityCallback != null) unityCallback.onStatusUpdate("Ready to receive data.");
                    }
                }
            }
        }

        @Override
        public void onCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic) {
            if (UART_TX_CHARACTERISTIC_UUID.equals(characteristic.getUuid())) {
                String message = new String(characteristic.getValue(), StandardCharsets.UTF_8);
                Log.d(TAG, "Received: " + message);
                if (unityCallback != null) {
                    unityCallback.onDataReceived(message);
                }
            }
        }
    };
}

package com.mbrats01.ble_manager;

public interface BleCallback {
    void onStatusUpdate(String message);
    void onDeviceFound(String name, String address);
    void onDataReceived(String data);
    void onDataReceivedBytes(byte[] data);
}

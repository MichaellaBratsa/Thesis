package com.mbrats01.ble_manager;

// Αυτό είναι ένα "interface". Είναι σαν ένα συμβόλαιο.
// Λέει ότι όποιος θέλει να "ακούσει" τον BleManager,
// ΠΡΕΠΕΙ να έχει αυτές τις τρεις μεθόδους.
public interface BleCallback {
    void onStatusUpdate(String message);
    void onDeviceFound(String name, String address);
    void onDataReceived(String message); // Η νέα μέθοδος για τα δεδομένα
}
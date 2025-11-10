#include <ArduinoBLE.h>

#define SERVICE_UUID           "6E400001-B5A3-F393-E0A9-E50E24DCCA9E"
#define CHARACTERISTIC_UUID_TX "6E400003-B5A3-F393-E0A9-E50E24DCCA9E" // TX = Transmit (Arduino -> Κινητό)
#define CHARACTERISTIC_UUID_RX "6E400002-B5A3-F393-E0A9-E50E24DCCA9E" // RX = Receive (Κινητό -> Arduino)

// Δημιουργία της υπηρεσίας και των χαρακτηριστικών με τα standard UUIDs
BLEService uartService(SERVICE_UUID);
BLECharacteristic txCharacteristic(CHARACTERISTIC_UUID_TX, BLERead | BLENotify, 20);
// Το RX characteristic είναι για να λαμβάνουμε δεδομένα, δεν το χρειαζόμαστε τώρα, αλλά το ορίζουμε για μελλοντική χρήση
BLECharacteristic rxCharacteristic(CHARACTERISTIC_UUID_RX, BLEWrite, 20);


void setup() {
  Serial.begin(9600);
  while (!Serial);

  if (!BLE.begin()) {
    Serial.println("Starting BLE failed!");
    while (1);
  }

  BLE.setLocalName("ArduinoBLE_Test"); // Άλλαξα το όνομα για να μην μπερδευόμαστε
  BLE.setAdvertisedService(uartService);

  // Προσθήκη των χαρακτηριστικών στην υπηρεσία
  uartService.addCharacteristic(txCharacteristic);
  uartService.addCharacteristic(rxCharacteristic); // Προσθέτουμε και το RX

  BLE.addService(uartService);

  BLE.advertise();

  Serial.println("BLE UART device is now advertising, waiting for connections...");
}

void loop() {
  BLEDevice central = BLE.central();

  if (central) {
    Serial.print("Connected to central: ");
    Serial.println(central.address());

    while (central.connected()) { ---
      txCharacteristic.writeValue("hello1\n");
      txCharacteristic.writeValue("hello\n");
      Serial.println("Sent: hello");
      delay(1000);
    }

    Serial.print("Disconnected from central: ");
    Serial.println(central.address());
  }
}

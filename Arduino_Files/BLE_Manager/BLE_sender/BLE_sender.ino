#include <ArduinoBLE.h>

// Define the BLE service and characteristic UUIDs
BLEService ledService("19B10000-E8F2-537E-4F6C-D104768A1214");
BLEStringCharacteristic switchCharacteristic("19B10001-E8F2-537E-4F6C-D104768A1214", BLERead | BLENotify, 20);

int counter = 0;

void setup() {
  Serial.begin(9600);
  while (!Serial);

  // Initialize BLE
  if (!BLE.begin()) {
    Serial.println("Starting BLE failed!");
    while (1);
  }

  // Set the local name for the BLE device
  BLE.setLocalName("ArduinoNano33BLE");
  
  // Set the advertised service UUID
  BLE.setAdvertisedService(ledService);

  // Add the service and characteristic
  ledService.addCharacteristic(switchCharacteristic);
  BLE.addService(ledService);

  // Set an initial value for the characteristic
  switchCharacteristic.writeValue("Hello Unity!");

  // Start advertising
  BLE.advertise();
  Serial.println("Bluetooth device active, waiting for connections...");
}

void loop() {
  // Wait for a BLE central to connect
  BLEDevice central = BLE.central();

  if (central) {
    Serial.print("Connected to central: ");
    Serial.println(central.address());

    while (central.connected()) {
      // Create a string with the current counter value
      String message = "Counter: " + String(counter);
      
      // Update the characteristic value
      switchCharacteristic.writeValue(message);
      Serial.println("Sent: " + message);
      
      counter++;
      delay(1000); // Wait a second
    }

    Serial.print("Disconnected from central: ");
    Serial.println(central.address());
  }
}
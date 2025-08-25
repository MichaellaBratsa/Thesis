#include <ArduinoBLE.h>

// Nordic UART Service (NUS)
BLEService uartService("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");


BLECharacteristic txChar("6E400003-B5A3-F393-E0A9-E50E24DCCA9E", BLENotify, 20);


BLECharacteristic rxChar("6E400002-B5A3-F393-E0A9-E50E24DCCA9E", BLEWrite, 20);

int counter = 0;


void onRX(BLEDevice, BLECharacteristic c) {
  int len = c.valueLength();          // input bytes
  const uint8_t* data = c.value();    

  String msg = "";
  for (int i = 0; i < len; i++) {
    msg += (char)data[i];             // casting bytes -> char
  }

  Serial.print("RX: ");
  Serial.println(msg);
}

void setup() {
  Serial.begin(9600);
  while (!Serial);

  if (!BLE.begin()) {
    Serial.println("BLE δεν ξεκίνησε!");
    while (1);
  }

  BLE.setLocalName("Nano33-UART");
  BLE.setAdvertisedService(uartService);

  uartService.addCharacteristic(txChar);
  uartService.addCharacteristic(rxChar);
  BLE.addService(uartService);

  rxChar.setEventHandler(BLEWritten, onRX);

  BLE.advertise();
  Serial.println("BLE UART έτοιμο");
}

void loop() {
  BLEDevice central = BLE.central();

  if (central) {
    Serial.print("Συνδέθηκε: ");
    Serial.println(central.address());

    while (central.connected()) {
      counter++;
      String line = "Counter = " + String(counter) + "\r\n";
      txChar.writeValue(line.c_str());   
      delay(1000);
    }

    Serial.print("Αποσυνδέθηκε: ");
    Serial.println(central.address());
  }
}

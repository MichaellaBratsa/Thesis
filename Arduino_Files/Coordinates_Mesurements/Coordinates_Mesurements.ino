#include <Arduino_LSM9DS1.h>

float ax, ay, az;

void setup() {
  Serial.begin(115200);
  while (!Serial);

  if (!IMU.begin()) {
    Serial.println("Failed to initialize IMU!");
    while(1);
  }
}

void loop() {
  if (IMU.accelerationAvailable()) {
    IMU.readAcceleration(ax, ay, az);

    Serial.print("ax:"); Serial.print(ax);
    Serial.print(" ay:"); Serial.print(ay);
    Serial.print(" az:"); Serial.println(az);
  }
  delay(50); 
}

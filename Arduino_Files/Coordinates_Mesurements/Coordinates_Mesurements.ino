#include <Wire.h>

const int MPU_addr = 0x68; // Διεύθυνση I2C του MPU6050

int16_t AcX, AcY, AcZ;

void setup() {
  Wire.begin();
  Serial.begin(9600);

  // Ξύπνα τον αισθητήρα (βρίσκεται σε sleep mode από default)
  Wire.beginTransmission(MPU_addr);
  Wire.write(0x6B); // Ρυθμιστικό sleep register
  Wire.write(0);    // Θέσε το σε 0 για να ξυπνήσει
  Wire.endTransmission(true);
}

void loop() {
  // Ζήτα δεδομένα από το register 0x3B (Accelerometer X)
  Wire.beginTransmission(MPU_addr);
  Wire.write(0x3B);
  Wire.endTransmission(false);
  Wire.requestFrom(MPU_addr, 6, true); // Ζήτα 6 bytes (AcX, AcY, AcZ)

  AcX = Wire.read() << 8 | Wire.read(); // Συνδυάζει τα MSB & LSB
  AcY = Wire.read() << 8 | Wire.read();
  AcZ = Wire.read() << 8 | Wire.read();

  Serial.print("x = "); Serial.print(AcX);
  Serial.print(" | y = "); Serial.print(AcY);
  Serial.print(" | z = "); Serial.println(AcZ);

  delay(200);
}

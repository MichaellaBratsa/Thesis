#include <MPU9250_asukiaaa.h>
#include <MadgwickAHRS.h> // Η ΔΙΚΗ ΣΟΥ βιβλιοθήκη! Σωστά!
#include <ArduinoBLE.h>

// --- Ρυθμίσεις BLE ---
BLEService motionService("6E400001-B5A3-F393-E0A9-E50E24DCCA9E"); 
BLECharacteristic motionCharacteristic("6E400003-B5A3-F393-E0A9-E50E24DCCA9E", BLERead | BLENotify, 256);

// --- Ρυθμίσεις IMU & Filter ---
MPU9250_asukiaaa mpu;
Madgwick filter; // Δημιουργούμε το αντικείμενο από τη δική σου βιβλιοθήκη

void setup() {
    Serial.begin(115200);
    Wire.begin();
    delay(100);

    // --- Αρχικοποίηση BLE ---
    if (!BLE.begin()) {
        Serial.println("Starting BLE failed!");
        while (1);
    }
    BLE.setLocalName("ArduinoBLE_Test");
    BLE.setAdvertisedService(motionService);
    motionService.addCharacteristic(motionCharacteristic);
    BLE.addService(motionService);
    BLE.advertise();
    Serial.println("Bluetooth device active...");

    // --- Αρχικοποίηση MPU9250 ---
    mpu.setWire(&Wire);
    mpu.beginAccel();
    mpu.beginGyro();
    mpu.beginMag();
    
    // --- Αρχικοποίηση του φίλτρου ---
    // Αυτή η βιβλιοθήκη δεν χρειάζεται calibration ή begin().
    // Απλά ξεκινάμε να την ταΐζουμε δεδομένα.
    Serial.println("IMU Ready.");
}

void loop() {
    BLEDevice central = BLE.central();
    if (central) {
        while (central.connected()) {
            // Διαβάζουμε όλους τους αισθητήρες
            mpu.accelUpdate();
            mpu.gyroUpdate();
            mpu.magUpdate();

            // "Ταΐζουμε" τον αλγόριθμο Madgwick.
            // ΠΡΟΣΟΧΗ: Χωρίς το dt στο τέλος!
            filter.update(
                mpu.gyroX(), mpu.gyroY(), mpu.gyroZ(),
                mpu.accelX(), mpu.accelY(), mpu.accelZ(),
                mpu.magX(), mpu.magY(), mpu.magZ()
            );

            // Παίρνουμε το Quaternion από τις public συναρτήσεις της βιβλιοθήκης
            // Αυτή η βιβλιοθήκη δεν δίνει Quaternions, αλλά γωνίες Euler.
            // Θα τις μετατρέψουμε σε Quaternions χειροκίνητα!
            float roll = filter.getRoll();
            float pitch = filter.getPitch();
            float yaw = filter.getYaw();

            // --- Μετατροπή από Euler σε Quaternion ---
            // (Αυτό είναι λίγο advanced, αλλά απαραίτητο)
            float cy = cos(yaw * 0.5 * DEG_TO_RAD);
            float sy = sin(yaw * 0.5 * DEG_TO_RAD);
            float cp = cos(pitch * 0.5 * DEG_TO_RAD);
            float sp = sin(pitch * 0.5 * DEG_TO_RAD);
            float cr = cos(roll * 0.5 * DEG_TO_RAD);
            float sr = sin(roll * 0.5 * DEG_TO_RAD);

            float qw = cr * cp * cy + sr * sp * sy;
            float qx = sr * cp * cy - cr * sp * sy;
            float qy = cr * sp * cy + sr * cp * sy;
            float qz = cr * cp * sy - sr * sp * cy;

            // Παίρνουμε την "ακατέργαστη" επιτάχυνση
            float ax = mpu.accelX();
            float ay = mpu.accelY();
            float az = mpu.accelZ();

            // Φτιάξε το μήνυμα: ID,qx,qy,qz,qw,ax,ay,az
            String message = "0," + String(qx, 4) + "," + String(qy, 4) + "," + String(qz, 4) + "," + String(qw, 4) + ","
                             + String(ax, 4) + "," + String(ay, 4) + "," + String(az, 4) + "\n";
            
            motionCharacteristic.writeValue((const uint8_t*)message.c_str(), message.length());
            delay(10);
        }
        Serial.println("Disconnected.");
    }
}
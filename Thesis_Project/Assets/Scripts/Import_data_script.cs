using UnityEngine;
using System.IO.Ports;

public class Import_data_script : MonoBehaviour
{
    SerialPort serialPort;
    public string portName = "COM9"; // Αλλαξε σε όποιο COM χρησιμοποιεί το Arduino
    public int baudRate = 115200;
    public TextMesh text_mesh;

    void Start()
    {
        serialPort = new SerialPort(portName, baudRate);
        serialPort.ReadTimeout = 50;

        try
        {
            serialPort.Open();
            text_mesh.text = "Serial Port Opened: " + portName;
        }
        catch (System.Exception e)
        {
            Debug.LogError("Δεν μπόρεσα να ανοίξω το serial port: " + e.Message); //test
        }
    }

    void Update()
    {
        if (serialPort != null && serialPort.IsOpen)
        {
            try
            {
                string line = serialPort.ReadLine();
                text_mesh.text = "Raw Data: " + line;

                string[] values = line.Split(',');
                if (values.Length == 12)
                {
                    // Παράδειγμα πρόσβασης: values[0] = ax1, values[6] = ax2
                    float ax1 = float.Parse(values[0]);
                    float ay1 = float.Parse(values[1]);
                    float az1 = float.Parse(values[2]);
                    float gx1 = float.Parse(values[3]);
                    float gy1 = float.Parse(values[4]);
                    float gz1 = float.Parse(values[5]);

                    float ax2 = float.Parse(values[6]);
                    float ay2 = float.Parse(values[7]);
                    float az2 = float.Parse(values[8]);
                    float gx2 = float.Parse(values[9]);
                    float gy2 = float.Parse(values[10]);
                    float gz2 = float.Parse(values[11]);

                    // Εμφάνιση στο Console
                    text_mesh.text = $"IMU1: A({ax1}, {ay1}, {az1}) G({gx1}, {gy1}, {gz1})";
                    text_mesh.text += $"\nIMU2: A({ax2}, {ay2}, {az2}) G({gx2}, {gy2}, {gz2})";

                }
            }
            catch (System.TimeoutException)
            {
                // Αγνόησε timeouts
            }
        }
    }

    void OnApplicationQuit()
    {
        if (serialPort != null && serialPort.IsOpen)
            serialPort.Close();
    }
}

